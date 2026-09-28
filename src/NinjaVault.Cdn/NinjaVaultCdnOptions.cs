namespace NinjaVault.Cdn
{
    public sealed class NinjaVaultCdnOptions
    {
        public const string SectionName = "NinjaVault:Cdn";

        public string BaseUrl { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string? PublicBaseUrl { get; set; }
    }
}