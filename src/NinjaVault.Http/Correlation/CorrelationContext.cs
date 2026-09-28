namespace NinjaVault.Http.Correlation;

/// <summary>
/// Ambient identifiers for a single logical operation (typically one inbound request).
/// The same <see cref="CorrelationId"/> is shared by every concurrent outbound call made
/// while handling that operation, so all related logs can be stitched together.
/// </summary>
public sealed record CorrelationContext(string CorrelationId, string? TraceId = null);