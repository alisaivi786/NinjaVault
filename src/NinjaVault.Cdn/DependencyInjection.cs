namespace NinjaVault.Cdn
{
    public static class DependencyInjection
    {
        /// <summary>Logical service name recorded on every NinjaVault.Http log entry for CDN calls.</summary>
        public const string ServiceName = "NinjaVaultCdn";

        /// <summary>
        /// Registers <see cref="INinjaVaultCdnClient"/> bound to the <see cref="NinjaVaultCdnOptions.SectionName"/>
        /// section, plus NinjaVault.Http logging/correlation. Returns the <see cref="IHttpClientBuilder"/> so callers
        /// can chain their own handlers (e.g. resilience) onto the CDN pipeline.
        /// </summary>
        public static IHttpClientBuilder AddNinjaVaultCdn(this IServiceCollection services, IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            services.Configure<NinjaVaultCdnOptions>(configuration.GetSection(NinjaVaultCdnOptions.SectionName));
            services.AddNinjaVaultHttp(configuration);

            return AddClient(services);
        }

        /// <summary>Registers <see cref="INinjaVaultCdnClient"/> with options configured in code instead of configuration.</summary>
        public static IHttpClientBuilder AddNinjaVaultCdn(this IServiceCollection services, Action<NinjaVaultCdnOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configure);

            services.Configure(configure);
            services.AddNinjaVaultHttp();

            return AddClient(services);
        }

        // Register CDN through NinjaVault.Http so every CDN request is logged and correlation-aware.
        // The client builds absolute request URIs from NinjaVaultCdnOptions.BaseUrl, so no BaseAddress is set here.
        private static IHttpClientBuilder AddClient(IServiceCollection services)
            => services.AddNinjaVaultHttpClient<INinjaVaultCdnClient, NinjaVaultCdnClient>(ServiceName);
    }
}