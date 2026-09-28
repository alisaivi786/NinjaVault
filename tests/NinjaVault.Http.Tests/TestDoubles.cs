using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using NinjaVault.Http.Logging;

namespace NinjaVault.Http.Tests;

/// <summary>Thread-safe shared collector so scoped sinks can still be asserted from the test.</summary>
internal sealed class LogStore
{
    private readonly ConcurrentQueue<ExternalApiCallLog> _logs = new();

    public void Add(ExternalApiCallLog log) => _logs.Enqueue(log);

    public IReadOnlyList<ExternalApiCallLog> Logs => _logs.ToArray();
}

/// <summary>Sink that records every log into the shared <see cref="LogStore"/>.</summary>
internal sealed class RecordingSink(LogStore store) : IExternalApiCallLogSink
{
    public Task WriteAsync(ExternalApiCallLog log, CancellationToken cancellationToken)
    {
        store.Add(log);
        return Task.CompletedTask;
    }
}

/// <summary>Sink that always throws — used to prove sink failures never break the HTTP call.</summary>
internal sealed class ThrowingSink : IExternalApiCallLogSink
{
    public Task WriteAsync(ExternalApiCallLog log, CancellationToken cancellationToken)
        => throw new InvalidOperationException("sink boom");
}

/// <summary>Primary handler test double: returns canned responses (or throws) and records requests seen.</summary>
internal sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    private readonly ConcurrentQueue<CapturedRequest> _requests = new();

    public IReadOnlyList<CapturedRequest> Requests => _requests.ToArray();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? correlationId = request.Headers.TryGetValues(Correlation.CorrelationConstants.HeaderName, out IEnumerable<string>? values)
            ? values.FirstOrDefault()
            : null;
        string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        _requests.Enqueue(new CapturedRequest(request.Method.Method, request.RequestUri, correlationId, body));
        return responder(request);
    }
}

internal sealed record CapturedRequest(string Method, Uri? Uri, string? CorrelationId, string? Body);

/// <summary>Simple typed client exercised by the tests.</summary>
internal interface ITestApiClient
{
    Task<string> PostAsync(string path, string json, CancellationToken cancellationToken);
    Task<string> PostWithAuthAsync(string path, string json, string bearerToken, CancellationToken cancellationToken);
    Task<string> PostWithHeaderAsync(string path, string json, string headerName, string headerValue, CancellationToken cancellationToken);
    Task<string> PostWithEndpointIdAsync(string path, string json, int endpointId, CancellationToken cancellationToken);
    Task<byte[]> PostMultipartFileAsync(string path, byte[] fileBytes, string fileName, CancellationToken cancellationToken);
}

internal sealed class TestApiClient(HttpClient httpClient) : ITestApiClient
{
    public async Task<string> PostAsync(string path, string json, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task<string> PostWithAuthAsync(string path, string json, string bearerToken, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task<string> PostWithHeaderAsync(string path, string json, string headerName, string headerValue, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation(headerName, headerValue);
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task<string> PostWithEndpointIdAsync(string path, string json, int endpointId, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.WithEndpointId(endpointId);
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task<byte[]> PostMultipartFileAsync(string path, byte[] fileBytes, string fileName, CancellationToken cancellationToken)
    {
        using MultipartFormDataContent content = [];
        ByteArrayContent fileContent = new(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(fileContent, "File", fileName);

        using HttpRequestMessage request = new(HttpMethod.Post, path) { Content = content };
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }
}

internal static class StubResponses
{
    public static HttpResponseMessage Ok(string body)
        => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage OkBinary(byte[] body, string contentType)
    {
        HttpResponseMessage response = new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return response;
    }
}

/// <summary>Response content that throws if it is read before a log entry has been written.</summary>
internal sealed class ReadAfterLogContent : HttpContent
{
    private readonly byte[] _body;
    private readonly LogStore _store;

    public ReadAfterLogContent(byte[] body, string contentType, LogStore store)
    {
        _body = body;
        _store = store;
        Headers.ContentType = new MediaTypeHeaderValue(contentType);
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        if (_store.Logs.Count == 0)
        {
            throw new InvalidOperationException("Response body was read by the logging handler before the log was written.");
        }

        return stream.WriteAsync(_body, 0, _body.Length);
    }

    protected override bool TryComputeLength(out long length)
    {
        length = _body.Length;
        return true;
    }
}