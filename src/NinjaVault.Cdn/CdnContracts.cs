namespace NinjaVault.Cdn
{
    /// <summary>
    /// Result of GET /api/v1/me. Call this once during startup to verify the configured API key and inspect
    /// its scope before relying on it - empty <see cref="AllowedBuckets"/>/<see cref="AllowedTenantIds"/> means
    /// unrestricted access to all buckets/tenants, not "no access".
    /// </summary>
    public sealed record CdnAccessContext(
        string Name,
        bool IsUnrestricted,
        bool IsAdmin,
        IReadOnlyList<string> AllowedBuckets,
        IReadOnlyList<long> AllowedTenantIds,
        IReadOnlyList<CdnAccessBucket> Buckets);

    public sealed record CdnAccessBucket(string Name, CdnBucketVisibility Visibility);

    public sealed record CdnBucket(
        string Name,
        CdnBucketVisibility Visibility,
        long FileCount,
        long TotalSizeBytes);

    /// <summary>
    /// Bucket must exist and be allowed for the configured API key; TenantId must be inside the key's
    /// allowed tenant scope (or the key must be unrestricted).
    /// </summary>
    /// <remarks>
    /// <para>
    /// FolderPath is not validated client-side. The server enforces: max 8 '/'-separated segments, each
    /// segment matching <c>^[A-Za-z0-9][A-Za-z0-9_-]*$</c>. A violation surfaces as 400 ValidationFailed
    /// (40001) only after the request reaches the server.
    /// </para>
    /// <para>
    /// The server also enforces a content-type allow-list and a max upload size that are both configurable
    /// per deployment (commonly far smaller than the 100 MB ASP.NET route ceiling, e.g. 25 MB by default).
    /// Neither limit is discoverable via the API - an oversized or disallowed file will fail with 40001.
    /// </para>
    /// </remarks>
    public sealed record CdnUploadRequest(
        string Bucket,
        long TenantId,
        Stream File,
        string FileName,
        string? ContentType = null,
        long? OwnerId = null,
        string? FolderPath = null);

    /// <summary>
    /// Result of a successful upload. Mirrors the API's FileObjectResponseV1 shape, which is
    /// intentionally smaller than <see cref="CdnFileObject"/> (no tenant, checksum, category, or
    /// thumbnail fields are returned from the upload endpoint). Use <see cref="INinjaVaultCdnClient.GetMetadataAsync"/>
    /// after upload if the full file object detail is needed.
    /// </summary>
    public sealed record CdnUploadResult(
        Guid Id,
        string Bucket,
        string ObjectKey,
        string OriginalFileName,
        string ContentType,
        long SizeBytes,
        CdnBucketVisibility Visibility,
        string Url);

    /// <summary>
    /// Full file detail as returned by list/metadata endpoints. Do not expect this shape from
    /// <see cref="INinjaVaultCdnClient.UploadAsync"/> - the upload response is the smaller <see cref="CdnUploadResult"/>.
    /// </summary>
    public sealed record CdnFileObject(
        Guid Id,
        long TenantId,
        long? OwnerId,
        string Bucket,
        string ObjectKey,
        string OriginalFileName,
        string ContentType,
        long SizeBytes,
        string? Checksum,
        CdnBucketVisibility Visibility,
        string? Extension,
        CdnFileCategory Category,
        CdnThumbnailStatus ThumbnailStatus,
        string? ThumbnailUrl,
        DateTimeOffset CreatedAtUtc,
        string Url);

    public sealed record CdnPagedResult<T>(
        IReadOnlyList<T> Items,
        long TotalCount,
        int Page,
        int PageSize);

    public sealed record CdnFileSummary(
        long TotalFileCount,
        long TotalSizeBytes,
        IReadOnlyList<CdnFileCategorySummary> Categories);

    public sealed record CdnFileCategorySummary(
        CdnFileCategory Category,
        long FileCount,
        long TotalSizeBytes);

    public sealed record CdnPresignedUrl(
        string Url,
        DateTimeOffset ExpiresAtUtc);

    /// <summary>
    /// Leave ExpirySeconds null to use the server's configured default (300s at time of writing). The
    /// server clamps this to its own max (commonly 3600s) regardless of what is requested.
    /// </summary>
    public sealed record CdnPresignRequest(
        string Bucket,
        string ObjectKey,
        int? ExpirySeconds = null);

    /// <summary>
    /// Batch presign returns HTTP 200 even when some targets fail - always inspect
    /// <see cref="CdnPresignBatchResult.Failed"/>, don't assume success from a non-throwing call.
    /// </summary>
    public sealed record CdnPresignBatchRequest(
        IReadOnlyList<CdnPresignTarget> Targets,
        int? ExpirySeconds = null);

    public sealed record CdnPresignTarget(string Bucket, string ObjectKey);

    public sealed record CdnPresignBatchResult(
        IReadOnlyList<CdnPresignedTarget> Succeeded,
        IReadOnlyList<CdnPresignFailure> Failed);

    public sealed record CdnPresignedTarget(
        string Bucket,
        string ObjectKey,
        string Url,
        DateTimeOffset ExpiresAtUtc);

    public sealed record CdnPresignFailure(
        string Bucket,
        string ObjectKey,
        string Reason);

    public sealed record CdnFileDownload(
        Stream Content,
        string? ContentType,
        string? FileName,
        long? SizeBytes) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await Content.DisposeAsync();
    }
}