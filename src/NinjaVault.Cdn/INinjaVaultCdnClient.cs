namespace NinjaVault.Cdn
{
    /// <summary>
    /// Server-to-server client for the NinjaVault CDN Server, authenticated with an X-Api-Key.
    /// Never expose the underlying API key to browsers/mobile clients - use <see cref="BuildPublicUrl"/>
    /// for public buckets or <see cref="CreatePresignedUrlAsync"/>/<see cref="CreatePresignedUrlsAsync"/>
    /// for private files that a browser/mobile client needs to download directly.
    /// </summary>
    public interface INinjaVaultCdnClient
    {
        /// <summary>Validates the configured API key and returns its tenant/bucket scope. Call once during startup.</summary>
        Task<CdnAccessContext> GetAccessContextAsync(CancellationToken cancellationToken = default);

        /// <summary>Lists buckets visible to the API key, with per-bucket visibility and usage counters.</summary>
        Task<IReadOnlyList<CdnBucket>> ListBucketsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Uploads a file via multipart/form-data. Persist the returned <see cref="CdnUploadResult.Bucket"/>
        /// and <see cref="CdnUploadResult.ObjectKey"/> - the object key, not the original file name, is required
        /// to fetch the file later.
        /// </summary>
        Task<CdnUploadResult> UploadAsync(CdnUploadRequest request, CancellationToken cancellationToken = default);

        /// <summary>Lists/searches file metadata with pagination and filtering. Does not transfer file bytes.</summary>
        Task<CdnPagedResult<CdnFileObject>> ListFilesAsync(CdnFileListQuery? query = null, CancellationToken cancellationToken = default);

        /// <summary>Reads metadata for one file without downloading its bytes.</summary>
        Task<CdnFileObject> GetMetadataAsync(string bucket, string objectKey, CancellationToken cancellationToken = default);

        /// <summary>Gets file count/size totals, optionally scoped to one bucket, broken down by category.</summary>
        Task<CdnFileSummary> GetSummaryAsync(string? bucket = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Downloads a file through the authenticated server-to-server route. Dispose the returned
        /// <see cref="CdnFileDownload"/> to release the underlying HTTP response. Use this only from backend
        /// code - never forward the API key to a browser/mobile client to hit this route directly.
        /// </summary>
        Task<CdnFileDownload> DownloadAsync(string bucket, string objectKey, CancellationToken cancellationToken = default);

        /// <summary>Soft-deletes a file.</summary>
        Task DeleteAsync(string bucket, string objectKey, CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates one short-lived anonymous download URL for a private file, safe to hand to a browser/mobile
        /// client. Keep expiry short (5-15 minutes is typical).
        /// </summary>
        Task<CdnPresignedUrl> CreatePresignedUrlAsync(CdnPresignRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates presigned URLs for many files in one call. Returns HTTP 200 even when some targets fail -
        /// always inspect <see cref="CdnPresignBatchResult.Failed"/>.
        /// </summary>
        Task<CdnPresignBatchResult> CreatePresignedUrlsAsync(CdnPresignBatchRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Builds a public anonymous download URL for a file in a Public-visibility bucket. Purely local string
        /// construction, no request is made - it does not verify the bucket is actually Public or that the
        /// object exists.
        /// </summary>
        string BuildPublicUrl(string bucket, string objectKey);
    }
}