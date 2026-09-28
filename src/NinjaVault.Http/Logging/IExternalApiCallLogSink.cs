namespace NinjaVault.Http.Logging;

/// <summary>
/// Receives a completed <see cref="ExternalApiCallLog"/> so it can be persisted or forwarded.
/// Consuming applications implement this to store external calls in their own audit table;
/// a structured-logging default is registered when no application sink is provided.
/// </summary>
public interface IExternalApiCallLogSink
{
    /// <summary>Persists or forwards a single external API call log entry.</summary>
    Task WriteAsync(ExternalApiCallLog log, CancellationToken cancellationToken);
}