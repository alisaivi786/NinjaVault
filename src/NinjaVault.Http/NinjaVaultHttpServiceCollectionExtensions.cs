using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NinjaVault.Http.Correlation;
using NinjaVault.Http.Logging;

namespace NinjaVault.Http;

/// <summary>
/// Registration entry points for the reusable NinjaVault HTTP client factory.
/// </summary>
public static class NinjaVaultHttpServiceCollectionExtensions
{
    /// <summary>
    /// Registers the shared HTTP infrastructure: correlation accessor, external-API logging options,
    /// and a default structured-logging sink. Call once at startup. Register an application-specific
    /// <see cref="IExternalApiCallLogSink"/> after this call to persist logs (the later registration wins).
    /// </summary>
    public static IServiceCollection AddNinjaVaultHttp(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<ExternalApiLoggingOptions>(configuration.GetSection(ExternalApiLoggingOptions.SectionName));
        return services.AddNinjaVaultHttp();
    }

    /// <summary>
    /// Registers the shared HTTP infrastructure with default <see cref="ExternalApiLoggingOptions"/>
    /// (configure them with <c>services.Configure&lt;ExternalApiLoggingOptions&gt;(...)</c>). Safe to call more than once.
    /// </summary>
    public static IServiceCollection AddNinjaVaultHttp(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpClient();
        services.AddOptions<ExternalApiLoggingOptions>();
        services.TryAddSingleton<ICorrelationContextAccessor, CorrelationContextAccessor>();
        services.TryAddSingleton<IExternalApiCallLogSink, LoggingExternalApiCallLogSink>();

        return services;
    }

    /// <summary>
    /// Registers a typed <see cref="HttpClient"/> that automatically logs every request/response and
    /// propagates the correlation id. Use this instead of <c>AddHttpClient</c> for any third-party API.
    /// </summary>
    /// <typeparam name="TClient">The typed client interface.</typeparam>
    /// <typeparam name="TImplementation">The typed client implementation.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="serviceName">Logical name recorded on each log entry (e.g. "PaymentsApi").</param>
    /// <param name="configureClient">Optional configuration of the underlying <see cref="HttpClient"/>.</param>
    public static IHttpClientBuilder AddNinjaVaultHttpClient<TClient, TImplementation>(
        this IServiceCollection services,
        string serviceName,
        Action<IServiceProvider, HttpClient>? configureClient = null)
        where TClient : class
        where TImplementation : class, TClient
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        IHttpClientBuilder builder = configureClient is null
            ? services.AddHttpClient<TClient, TImplementation>()
            : services.AddHttpClient<TClient, TImplementation>(configureClient);

        builder.AddHttpMessageHandler(provider => new ExternalApiLoggingHandler(
            serviceName,
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<ICorrelationContextAccessor>(),
            provider.GetRequiredService<IOptionsMonitor<ExternalApiLoggingOptions>>(),
            provider.GetRequiredService<ILogger<ExternalApiLoggingHandler>>()));

        return builder;
    }
}