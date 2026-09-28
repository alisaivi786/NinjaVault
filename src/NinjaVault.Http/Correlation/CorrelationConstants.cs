namespace NinjaVault.Http.Correlation;

/// <summary>
/// Well-known values used to propagate a correlation id across service boundaries.
/// </summary>
public static class CorrelationConstants
{
    /// <summary>
    /// HTTP header that carries the correlation id on inbound and outbound requests.
    /// </summary>
    public const string HeaderName = "X-Correlation-Id";
}