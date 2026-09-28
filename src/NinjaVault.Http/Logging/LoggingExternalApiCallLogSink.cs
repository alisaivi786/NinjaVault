using Microsoft.Extensions.Logging;

namespace NinjaVault.Http.Logging;

/// <summary>
/// Default <see cref="IExternalApiCallLogSink"/> that emits a structured log entry per call.
/// Used automatically when the application does not register its own persistence sink.
/// </summary>
public sealed class LoggingExternalApiCallLogSink(ILogger<LoggingExternalApiCallLogSink> logger) : IExternalApiCallLogSink
{
    private static readonly Action<ILogger, string, string, int, long, string?, string?, Exception?> ExternalCallCompleted =
        LoggerMessage.Define<string, string, int, long, string?, string?>(
            LogLevel.Information,
            new EventId(3001, nameof(ExternalCallCompleted)),
            "External API Call | Service: {ServiceName} | Url: {RequestUrl} | StatusCode: {StatusCode} | ElapsedMs: {ElapsedMs} | CorrelationId: {CorrelationId} | Error: {Error}");

    public Task WriteAsync(ExternalApiCallLog log, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(log);

        ExternalCallCompleted(
            logger,
            log.ServiceName,
            log.RequestUrl,
            log.StatusCode,
            log.ElapsedMs,
            log.CorrelationId,
            log.Error,
            null);

        return Task.CompletedTask;
    }
}