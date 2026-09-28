namespace NinjaVault.Cdn
{
    public static class DependencyInjection
    {
        /// <summary>
        /// Name of the <see cref="IHttpClientFactory"/> client used by <see cref="INinjaVaultCdnClient"/>.
        /// Use it as the service name when you attach your own logging handler.
        /// </summary>
        public const string ServiceName = "NinjaVaultCdn";

        /// <summary>
        /// Registers <see cref="INinjaVaultCdnClient"/> bound to the <see cref="NinjaVaultCdnOptions.SectionName"/> section.
        /// Logging, retries and correlation are left to the application: chain your own handlers onto the returned
        /// <see cref="IHttpClientBuilder"/> (for example <c>.AddHttpMessageHandler(...)</c> or <c>.AddStandardResilienceHandler()</c>).
        /// </summary>
        public static IHttpClientBuilder AddNinjaVaultCdn(this IServiceCollection services, IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            services.Configure<NinjaVaultCdnOptions>(configuration.GetSection(NinjaVaultCdnOptions.SectionName));

            return AddClient(services);
        }

        /// <summary>Registers <see cref="INinjaVaultCdnClient"/> with options configured in code instead of configuration.</summary>
        public static IHttpClientBuilder AddNinjaVaultCdn(this IServiceCollection services, Action<NinjaVaultCdnOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configure);

            services.Configure(configure);

            return AddClient(services);
        }

        // The client builds absolute request URIs from NinjaVaultCdnOptions.BaseUrl, so no BaseAddress is set here.
        private static IHttpClientBuilder AddClient(IServiceCollection services)
            => services.AddHttpClient<INinjaVaultCdnClient, NinjaVaultCdnClient>(ServiceName);
    }
}