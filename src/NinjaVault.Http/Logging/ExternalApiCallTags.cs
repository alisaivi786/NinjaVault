namespace NinjaVault.Http.Logging;

/// <summary>
/// Helpers for tagging an outbound <see cref="HttpRequestMessage"/> with metadata that the
/// <see cref="ExternalApiLoggingHandler"/> copies onto the captured <see cref="ExternalApiCallLog"/>.
/// </summary>
public static class ExternalApiCallTags
{
    /// <summary>
    /// Request-options key carrying the caller's endpoint/operation id for the call. A typed client
    /// sets this (e.g. from its own endpoint enum) and the logging handler records it on the log.
    /// </summary>
    public static readonly HttpRequestOptionsKey<int> EndpointIdKey = new("NinjaVault.ExternalApi.EndpointId");

    /// <summary>
    /// Tags the request with an endpoint/operation id that will be recorded on the
    /// <see cref="ExternalApiCallLog.EndpointId"/>. Returns the same request for chaining.
    /// </summary>
    public static HttpRequestMessage WithEndpointId(this HttpRequestMessage request, int endpointId)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Options.Set(EndpointIdKey, endpointId);
        return request;
    }

    /// <summary>Reads the endpoint id previously set via <see cref="WithEndpointId"/>, if any.</summary>
    public static int? GetEndpointId(this HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Options.TryGetValue(EndpointIdKey, out int value) ? value : null;
    }
}