namespace NinjaVault.Cdn
{
    public sealed record CdnFileListQuery
    {
        public string? Bucket { get; init; }
        public long? TenantId { get; init; }
        public long? OwnerId { get; init; }
        public string? ObjectKeyPrefix { get; init; }
        public string? FileNameContains { get; init; }
        public string? ContentType { get; init; }
        public CdnFileCategory? Category { get; init; }
        public CdnBucketVisibility? Visibility { get; init; }
        public DateTimeOffset? CreatedFromUtc { get; init; }
        public DateTimeOffset? CreatedToUtc { get; init; }
        public bool? IncludeDeleted { get; init; }
        public CdnFileSortBy? SortBy { get; init; }
        public bool? SortDescending { get; init; }
        public int? Page { get; init; }
        public int? PageSize { get; init; }
    }

    public enum CdnFileSortBy
    {
        CreatedAtUtc,
        OriginalFileName,
        SizeBytes,
        ContentType,
        Bucket
    }
}