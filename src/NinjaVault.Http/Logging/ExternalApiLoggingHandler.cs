using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NinjaVault.Http.Correlation;

namespace NinjaVault.Http.Logging;

/// <summary>
/// <see cref="DelegatingHandler"/> that transparently logs every outbound request/response for a
/// typed client. It stamps the ambient correlation id on the outgoing request, captures timing,
/// status, headers and (optionally) bodies, and forwards the result to the registered
/// <see cref="IExternalApiCallLogSink"/>. Logging failures never affect the underlying call.
/// </summary>
public sealed class ExternalApiLoggingHandler(
    string serviceName,
    IServiceScopeFactory scopeFactory,
    ICorrelationContextAccessor correlationAccessor,
    IOptionsMonitor<ExternalApiLoggingOptions> optionsMonitor,
    ILogger<ExternalApiLoggingHandler> logger) : DelegatingHandler
{
    private static readonly Action<ILogger, string, Exception?> SinkFailed =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(3002, nameof(SinkFailed)),
            "External API call log sink failed for service {ServiceName}.");

    // Responses that declare a larger Content-Length are never buffered for logging.
    private const long MaxBufferedResponseBytes = 1024 * 1024;
    private const string BinaryReason = "binary or multipart body - not logged";
    private const string TooLargeReason = "body larger than 1 MB - not buffered or logged";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ExternalApiLoggingOptions options = optionsMonitor.CurrentValue;
        if (!options.Enabled)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        CorrelationContext? correlation = correlationAccessor.Current;
        string correlationId = correlation?.CorrelationId ?? correlationAccessor.GetOrCreateCorrelationId();
        if (!request.Headers.Contains(CorrelationConstants.HeaderName))
        {
            request.Headers.TryAddWithoutValidation(CorrelationConstants.HeaderName, correlationId);
        }

        int? endpointId = request.GetEndpointId();
        string requestBody = options.CaptureRequestBody ? await ReadContentAsync(request.Content, options.MaxBodyLength, cancellationToken) : string.Empty;
        string requestHeaders = SerializeHeaders(request.Headers, request.Content?.Headers, options);

        DateTime startedOn = DateTime.UtcNow;
        Stopwatch stopwatch = Stopwatch.StartNew();

        HttpResponseMessage response;
        try
        {
            response = await base.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            stopwatch.Stop();
            await SafeWriteAsync(new ExternalApiCallLog
            {
                ServiceName = serviceName,
                EndpointId = endpointId,
                CorrelationId = correlationId,
                TraceId = correlation?.TraceId,
                Method = request.Method.Method,
                RequestUrl = request.RequestUri?.ToString() ?? string.Empty,
                RequestHeaders = requestHeaders,
                RequestBody = requestBody,
                StatusCode = 0,
                ElapsedMs = stopwatch.ElapsedMilliseconds,
                Error = ex.Message,
                StartedOnUtc = startedOn,
                CompletedOnUtc = DateTime.UtcNow
            });
            throw;
        }

        stopwatch.Stop();

        string responseBody = options.CaptureResponseBody ? await ReadResponseAsync(response.Content, options.MaxBodyLength, cancellationToken) : string.Empty;
        string responseHeaders = SerializeHeaders(response.Headers, response.Content?.Headers, options);

        await SafeWriteAsync(new ExternalApiCallLog
        {
            ServiceName = serviceName,
            EndpointId = endpointId,
            CorrelationId = correlationId,
            TraceId = correlation?.TraceId,
            Method = request.Method.Method,
            RequestUrl = request.RequestUri?.ToString() ?? string.Empty,
            RequestHeaders = requestHeaders,
            RequestBody = requestBody,
            StatusCode = (int)response.StatusCode,
            ResponseHeaders = responseHeaders,
            ResponseBody = responseBody,
            ElapsedMs = stopwatch.ElapsedMilliseconds,
            StartedOnUtc = startedOn,
            CompletedOnUtc = DateTime.UtcNow
        });

        return response;
    }

    private async Task SafeWriteAsync(ExternalApiCallLog log)
    {
#pragma warning disable CA1031 // A logging failure must never break the underlying API call.
        try
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            IExternalApiCallLogSink sink = scope.ServiceProvider.GetRequiredService<IExternalApiCallLogSink>();
            await sink.WriteAsync(log, CancellationToken.None);
        }
        catch (Exception ex)
        {
            SinkFailed(logger, serviceName, ex);
        }
#pragma warning restore CA1031
    }

    private static async Task<string> ReadContentAsync(HttpContent? content, int maxLength, CancellationToken cancellationToken)
    {
        if (content is null)
        {
            return string.Empty;
        }

        if (IsBinaryOrMultipart(content))
        {
            return BuildOmittedBodyPlaceholder(content);
        }

        string body = await content.ReadAsStringAsync(cancellationToken);
        return Truncate(body, maxLength);
    }

    private static async Task<string> ReadResponseAsync(HttpContent? content, int maxLength, CancellationToken cancellationToken)
    {
        if (content is null)
        {
            return string.Empty;
        }

        // Decide from the headers BEFORE buffering: buffering a file download would pull the whole
        // payload into memory just to log a placeholder, and would stop the caller from streaming it.
        if (IsBinaryOrMultipart(content))
        {
            return BuildOmittedBodyPlaceholder(content);
        }

        if (content.Headers.ContentLength > MaxBufferedResponseBytes)
        {
            return BuildOmittedBodyPlaceholder(content, TooLargeReason);
        }

        // Buffer so the caller can still read the response stream after we capture it.
#if NET9_0_OR_GREATER
        await content.LoadIntoBufferAsync(cancellationToken);
#else
        cancellationToken.ThrowIfCancellationRequested();
        await content.LoadIntoBufferAsync();
#endif

        string body = await content.ReadAsStringAsync(cancellationToken);
        return Truncate(body, maxLength);
    }

    // Reading these as text (e.g. a multipart upload carrying a raw JPEG) decodes binary bytes as
    // UTF-8, which mangles the content, can exceed downstream log-event size limits, and can produce
    // byte sequences some log sinks reject outright (e.g. Postgres' "invalid byte sequence for
    // encoding UTF8"). Recording a placeholder is strictly safer than best-effort text decoding.
    private static bool IsBinaryOrMultipart(HttpContent content)
    {
        string? mediaType = content.Headers.ContentType?.MediaType;
        if (string.IsNullOrEmpty(mediaType))
        {
            return false;
        }

        return mediaType.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase)
            || mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            || mediaType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)
            || mediaType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("application/zip", StringComparison.OrdinalIgnoreCase);
    }

    private static readonly JsonSerializerOptions OmittedBodyJsonOptions = new(JsonSerializerDefaults.Web);

    // A small JSON record beats a fixed string here: it still tells the reader what happened, but also
    // carries the content type and size, which is often exactly what you'd want to eyeball a binary
    // upload/download in the log without ever writing its bytes anywhere.
    private static string BuildOmittedBodyPlaceholder(HttpContent content, string reason = BinaryReason)
        => JsonSerializer.Serialize(
            new OmittedBodyPlaceholder(
                Omitted: true,
                Reason: reason,
                ContentType: content.Headers.ContentType?.ToString(),
                SizeBytes: content.Headers.ContentLength),
            OmittedBodyJsonOptions);

    private sealed record OmittedBodyPlaceholder(bool Omitted, string Reason, string? ContentType, long? SizeBytes);

    private static string Truncate(string value, int maxLength)
    {
        if (maxLength <= 0 || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }

    private static string SerializeHeaders(HttpHeaders headers, HttpContentHeaders? contentHeaders, ExternalApiLoggingOptions options)
    {
        HashSet<string> sensitive = new(options.SensitiveHeaders, StringComparer.OrdinalIgnoreCase);

        IEnumerable<KeyValuePair<string, IEnumerable<string>>> all = headers;
        if (contentHeaders is not null)
        {
            all = all.Concat(contentHeaders);
        }

        Dictionary<string, string> serialized = all
            .GroupBy(header => header.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => sensitive.Contains(group.Key) ? "[REDACTED]" : string.Join(", ", group.Last().Value),
                StringComparer.OrdinalIgnoreCase);

        return JsonSerializer.Serialize(serialized);
    }
}