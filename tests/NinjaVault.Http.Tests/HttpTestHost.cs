using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NinjaVault.Http.Correlation;
using NinjaVault.Http.Logging;

namespace NinjaVault.Http.Tests;

/// <summary>
/// Wires a real DI container around <see cref="ExternalApiLoggingHandler"/> and a typed client,
/// with a stub primary handler, so tests exercise the full registration + handler pipeline.
/// </summary>
internal static class HttpTestHost
{
    public static HttpTestHostContext Build(
        StubHttpMessageHandler stub,
        LogStore store,
        Action<ExternalApiLoggingOptions>? configureOptions = null,
        ServiceLifetime sinkLifetime = ServiceLifetime.Scoped,
        bool useThrowingSink = false)
    {
        ServiceCollection services = new();
        services.AddLogging();

        IConfiguration configuration = new ConfigurationBuilder().Build();
        services.AddNinjaVaultHttp(configuration);

        services.AddSingleton(store);
        if (useThrowingSink)
        {
            services.AddSingleton<IExternalApiCallLogSink, ThrowingSink>();
        }
        else if (sinkLifetime == ServiceLifetime.Singleton)
        {
            services.AddSingleton<IExternalApiCallLogSink, RecordingSink>();
        }
        else
        {
            services.AddScoped<IExternalApiCallLogSink, RecordingSink>();
        }

        if (configureOptions is not null)
        {
            services.Configure<ExternalApiLoggingOptions>(configureOptions);
        }

        services.AddNinjaVaultHttpClient<ITestApiClient, TestApiClient>("TestApi", (_, client) =>
        {
            client.BaseAddress = new Uri("https://unit.test/", UriKind.Absolute);
        }).ConfigurePrimaryHttpMessageHandler(() => stub);

        return new HttpTestHostContext(services.BuildServiceProvider());
    }
}

internal sealed class HttpTestHostContext(ServiceProvider provider) : IDisposable
{
    public ITestApiClient Client => provider.GetRequiredService<ITestApiClient>();

    public ICorrelationContextAccessor Correlation => provider.GetRequiredService<ICorrelationContextAccessor>();

    public void Dispose() => provider.Dispose();
}