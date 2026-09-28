namespace NinjaVault.Http.Logging;

/// <summary>
/// Captured details of a single outbound third-party API call. Produced by
/// <see cref="ExternalApiLoggingHandler"/> and handed to every registered
/// <see cref="IExternalApiCallLogSink"/>.
/// </summary>
public sealed class ExternalApiCallLog
{
    /// <summary>Logical name of the external service (e.g. "PaymentsApi").</summary>
    public string ServiceName { get; init; } = string.Empty;

    /// <summary>
    /// Optional caller-supplied endpoint/operation identifier for the call (e.g. an enum value the
    /// typed client maps to a specific endpoint). Set it via
    /// <see cref="ExternalApiCallTags.WithEndpointId"/>; null when the caller did not tag the call.
    /// </summary>
    public int? EndpointId { get; init; }

    /// <summary>Correlation id shared with the inbound request that triggered this call.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>Inbound trace id, when available.</summary>
    public string? TraceId { get; init; }

    /// <summary>HTTP method used for the call.</summary>
    public string Method { get; init; } = string.Empty;

    /// <summary>Absolute request URL.</summary>
    public string RequestUrl { get; init; } = string.Empty;

    /// <summary>Serialized request headers (sensitive values redacted).</summary>
    public string? RequestHeaders { get; init; }

    /// <summary>Request body, truncated to the configured maximum length.</summary>
    public string? RequestBody { get; init; }

    /// <summary>HTTP status code returned, or 0 when the call failed before a response.</summary>
    public int StatusCode { get; init; }

    /// <summary>Serialized response headers (sensitive values redacted).</summary>
    public string? ResponseHeaders { get; init; }

    /// <summary>Response body, truncated to the configured maximum length.</summary>
    public string? ResponseBody { get; init; }

    /// <summary>Total elapsed time for the call in milliseconds.</summary>
    public long ElapsedMs { get; init; }

    /// <summary>Exception message when the call threw before/while sending, otherwise null.</summary>
    public string? Error { get; init; }

    /// <summary>UTC timestamp when the call started.</summary>
    public DateTime StartedOnUtc { get; init; }

    /// <summary>UTC timestamp when the call completed (or faulted).</summary>
    public DateTime CompletedOnUtc { get; init; }
}