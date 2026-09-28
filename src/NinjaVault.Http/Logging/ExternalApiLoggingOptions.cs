namespace NinjaVault.Http.Logging;

/// <summary>
/// Configuration for outbound external API request/response logging.
/// Bound from the "NinjaVault:ExternalApiLogging" configuration section.
/// </summary>
public sealed class ExternalApiLoggingOptions
{
    public const string SectionName = "NinjaVault:ExternalApiLogging";

    /// <summary>Master switch for capturing outbound calls. Defaults to enabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Whether request bodies are captured.</summary>
    public bool CaptureRequestBody { get; set; } = true;

    /// <summary>Whether response bodies are captured.</summary>
    public bool CaptureResponseBody { get; set; } = true;

    /// <summary>Maximum number of characters stored for each captured body.</summary>
    public int MaxBodyLength { get; set; } = 8192;

    /// <summary>Header names whose values are redacted before logging.</summary>
    public IList<string> SensitiveHeaders { get; } =
    [
        "Authorization",
        "Cookie",
        "Set-Cookie",
        "X-Api-Key",
        "X-Access-Token"
    ];
}