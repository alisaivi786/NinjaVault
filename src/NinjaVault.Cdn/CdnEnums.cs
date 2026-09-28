namespace NinjaVault.Cdn
{
    public enum CdnBucketVisibility
    {
        Private,
        Public
    }

    public enum CdnFileCategory
    {
        Image,
        Video,
        Audio,
        Document,
        Other
    }

    public enum CdnThumbnailStatus
    {
        NotApplicable,
        Pending,
        Ready,
        Failed
    }
}