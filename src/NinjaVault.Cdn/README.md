# NinjaVault.Cdn

Typed .NET client for the **NinjaVault CDN Server** API-key integration: bucket discovery, upload,
listing, metadata, download, delete, and presigned/public URLs. It handles the multipart form
fields, object-key encoding and JSON success/error envelope for you.

Targets `net8.0`, `net9.0` and `net10.0`.

## 1. Install

```bash
dotnet add package NinjaVault.Cdn
```

`NinjaVault.Http` (request logging + correlation id) is installed automatically as a dependency.

## 2. appsettings.json

```json
{
  "NinjaVault": {
    "Cdn": {
      "BaseUrl": "https://cdn.example.com",
      "PublicBaseUrl": "https://cdn.example.com",
      "ApiKey": "cdn_xxxxx"
    }
  }
}
```

| Key | Required | Description |
|---|---|---|
| `NinjaVault:Cdn:BaseUrl` | Yes | Base URL for the authenticated `/api/v1/...` routes. A path prefix (e.g. `https://gateway.example.com/cdn`) is supported. |
| `NinjaVault:Cdn:ApiKey` | Yes | The `X-Api-Key` value issued for this integration. **Backend-only secret** - store it in configuration/secret manager, never ship it to a browser, mobile app, or client-side JS. |
| `NinjaVault:Cdn:PublicBaseUrl` | No | Base host for anonymous `/public/...` delivery. Falls back to `BaseUrl` if omitted. |

In production, source `ApiKey` from your secret manager or an environment variable
(`NinjaVault__Cdn__ApiKey`) rather than committing it to `appsettings.json`.

## 3. Register the client

```csharp
builder.Services.AddNinjaVaultCdn(builder.Configuration);

// or configure in code
builder.Services.AddNinjaVaultCdn(options =>
{
    options.BaseUrl = "https://cdn.example.com";
    options.ApiKey = builder.Configuration["CdnApiKey"]!;
});
```

`AddNinjaVaultCdn` also registers `NinjaVault.Http`, so every CDN call is logged (API key redacted,
file bytes never logged) and carries an `X-Correlation-Id` header. To persist those logs, register
your own `IExternalApiCallLogSink` after this call.

Both overloads return `IHttpClientBuilder`, so you can chain your own handlers, e.g. retries with
`Microsoft.Extensions.Http.Resilience`:

```csharp
builder.Services.AddNinjaVaultCdn(builder.Configuration)
    .AddStandardResilienceHandler();
```

Inject `INinjaVaultCdnClient` wherever you need it:

```csharp
public sealed class DocumentService(INinjaVaultCdnClient cdn)
{
    // ...
}
```

## 4. Verify access at startup (recommended)

Call this once during startup/health-check to confirm the API key and inspect its scope before
relying on it:

```csharp
CdnAccessContext access = await cdn.GetAccessContextAsync(cancellationToken);

// access.IsUnrestricted / access.AllowedTenantIds empty => access to every tenant, not "no access"
// access.AllowedBuckets empty => access to every bucket
foreach (CdnAccessBucket bucket in access.Buckets)
{
    Console.WriteLine($"{bucket.Name}: {bucket.Visibility}");
}
```

## 5. Discover buckets

```csharp
IReadOnlyList<CdnBucket> buckets = await cdn.ListBucketsAsync(cancellationToken);

foreach (CdnBucket bucket in buckets)
{
    Console.WriteLine($"{bucket.Name} ({bucket.Visibility}) - {bucket.FileCount} files, {bucket.TotalSizeBytes} bytes");
}
```

## 6. Upload a file

`UploadAsync` sends `multipart/form-data`. Any readable `Stream` works - below are examples for
the file kinds this client is commonly used with (PDF, image, Office document, video, and a
generic/unknown binary). The server enforces its own content-type allow-list and max size per
deployment (see the note at the end of this section) - it is not something the client validates
locally.

```csharp
public sealed class DocumentService(INinjaVaultCdnClient cdn)
{
    // PDF, e.g. a generated invoice
    public Task<CdnUploadResult> UploadInvoicePdfAsync(Stream pdf, long tenantId, long ownerId, CancellationToken ct)
        => cdn.UploadAsync(
            new CdnUploadRequest(
                Bucket: "documents",
                TenantId: tenantId,
                File: pdf,
                FileName: "invoice.pdf",
                ContentType: "application/pdf",
                OwnerId: ownerId,
                FolderPath: "invoices/2026"),
            ct);

    // Image, e.g. a profile photo or scanned receipt
    public Task<CdnUploadResult> UploadImageAsync(Stream image, long tenantId, CancellationToken ct)
        => cdn.UploadAsync(
            new CdnUploadRequest(
                Bucket: "public-assets",
                TenantId: tenantId,
                File: image,
                FileName: "avatar.png",
                ContentType: "image/png",
                FolderPath: "avatars"),
            ct);

    // Office document, e.g. an exported Word/Excel report
    public Task<CdnUploadResult> UploadWordDocumentAsync(Stream docx, long tenantId, long ownerId, CancellationToken ct)
        => cdn.UploadAsync(
            new CdnUploadRequest(
                Bucket: "documents",
                TenantId: tenantId,
                File: docx,
                FileName: "report.docx",
                ContentType: "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                OwnerId: ownerId,
                FolderPath: "reports/2026"),
            ct);

    // Video (only upload this if the deployment's content-type allow-list permits it)
    public Task<CdnUploadResult> UploadVideoAsync(Stream video, long tenantId, CancellationToken ct)
        => cdn.UploadAsync(
            new CdnUploadRequest(
                Bucket: "documents",
                TenantId: tenantId,
                File: video,
                FileName: "walkthrough.mp4",
                ContentType: "video/mp4"),
            ct);

    // Unknown/generic binary - let the server infer/validate the content type from the extension
    public Task<CdnUploadResult> UploadGenericFileAsync(Stream file, string fileName, long tenantId, CancellationToken ct)
        => cdn.UploadAsync(
            new CdnUploadRequest(
                Bucket: "documents",
                TenantId: tenantId,
                File: file,
                FileName: fileName),
            ct);
}
```

`UploadAsync` returns `CdnUploadResult` - `Id`, `Bucket`, `ObjectKey`, `OriginalFileName`,
`ContentType`, `SizeBytes`, `Visibility`, `Url`. This is the API's actual upload-response shape;
it is smaller than `CdnFileObject` (no tenant, checksum, category, or thumbnail fields - those are
only returned by list/metadata). Persist `Bucket` and `ObjectKey` in your own database: the
**object key**, not the original file name, is what you need to fetch the file again later. Call
`GetMetadataAsync` afterward if you need the full detail (checksum, category, thumbnail status).

> **FolderPath** is optional, max 8 `/`-separated segments, each matching `^[A-Za-z0-9][A-Za-z0-9_-]*$`.
> Not validated client-side - a bad value surfaces as a 400 `ValidationFailed` only once the
> request reaches the server.
>
> **Size/content-type limits** are configured per deployment. The ASP.NET route ceiling is 100 MB,
> but the actual enforced business limit is commonly smaller (e.g. 25 MB) with a small allow-list
> of content types (PDF, JPEG, PNG, WEBP, DOC, DOCX are typical defaults). Neither limit is
> discoverable through the API - confirm them with whoever manages your CDN deployment, and expect
> a 400 `ValidationFailed` for anything outside them.

## 7. List / search files

```csharp
CdnPagedResult<CdnFileObject> page = await cdn.ListFilesAsync(
    new CdnFileListQuery
    {
        Bucket = "documents",
        TenantId = 42,
        OwnerId = 1001,
        ObjectKeyPrefix = "owner-token/invoices",
        Category = CdnFileCategory.Document,
        Visibility = CdnBucketVisibility.Private,
        FileNameContains = "invoice",
        SortBy = CdnFileSortBy.CreatedAtUtc,
        SortDescending = true,
        Page = 1,
        PageSize = 50
    },
    cancellationToken);

foreach (CdnFileObject file in page.Items)
{
    Console.WriteLine($"{file.OriginalFileName} ({file.Category}, {file.SizeBytes} bytes)");
}
```

## 8. Read metadata for one file (no bytes transferred)

```csharp
CdnFileObject file = await cdn.GetMetadataAsync("documents", objectKey, cancellationToken);
```

## 9. Usage summary (dashboards / file-manager cards)

```csharp
CdnFileSummary summary = await cdn.GetSummaryAsync(bucket: "documents", cancellationToken: cancellationToken);

Console.WriteLine($"{summary.TotalFileCount} files, {summary.TotalSizeBytes} bytes");
foreach (CdnFileCategorySummary category in summary.Categories)
{
    Console.WriteLine($"{category.Category}: {category.FileCount} files, {category.TotalSizeBytes} bytes");
}
```

## 10. Download a file (backend-to-backend only)

```csharp
await using CdnFileDownload download = await cdn.DownloadAsync("documents", objectKey, cancellationToken);

// download.Content is a Stream; download.ContentType / download.FileName / download.SizeBytes are also available
await using FileStream target = File.Create(download.FileName ?? "download.bin");
await download.Content.CopyToAsync(target, cancellationToken);
```

Use this only from backend code. Never forward the API key to a browser/mobile client to hit this
route directly - use a presigned URL (below) instead.

## 11. Delete a file (soft delete)

```csharp
await cdn.DeleteAsync("documents", objectKey, cancellationToken);
```

## 12. Public URLs (Public-visibility buckets)

```csharp
string url = cdn.BuildPublicUrl("public-assets", objectKey);
```

This is pure local string construction - no HTTP call is made, and it does not verify the bucket
is actually `Public` or that the object exists. Use it only for files you uploaded into a bucket
you know is `Public`.

## 13. Presigned URLs (private files, browser/mobile clients)

Single file:

```csharp
CdnPresignedUrl presigned = await cdn.CreatePresignedUrlAsync(
    new CdnPresignRequest("documents", objectKey, ExpirySeconds: 300),
    cancellationToken);

return presigned.Url; // hand this to the browser/mobile client - never the API key itself
```

Leave `ExpirySeconds` `null` to use the server's configured default (300s at time of writing;
the server clamps to its own max regardless of what's requested). Keep expiry short - 5 to 15
minutes is typical.

Batch (many files in one call):

```csharp
CdnPresignBatchResult result = await cdn.CreatePresignedUrlsAsync(
    new CdnPresignBatchRequest(
        Targets:
        [
            new CdnPresignTarget("documents", "owner-token/invoices/2026/a.pdf"),
            new CdnPresignTarget("documents", "owner-token/invoices/2026/b.pdf")
        ],
        ExpirySeconds: 300),
    cancellationToken);

// Batch presign returns HTTP 200 even when some targets fail - always check Failed.
foreach (CdnPresignedTarget ok in result.Succeeded)
{
    Console.WriteLine($"{ok.ObjectKey} -> {ok.Url}");
}

foreach (CdnPresignFailure failed in result.Failed)
{
    Console.WriteLine($"{failed.ObjectKey} failed: {failed.Reason}"); // "NotFound" or "Forbidden"
}
```

## 14. Error handling

Every failed call throws `CdnApiException` (derives from `HttpRequestException`):

```csharp
try
{
    await cdn.UploadAsync(request, cancellationToken);
}
catch (CdnApiException ex)
{
    logger.LogWarning(
        "CDN call failed: {StatusCode} {ErrorCode} {Message} (trace {CorrelationId})",
        ex.StatusCode, ex.ErrorCode, ex.Message, ex.CorrelationId);

    if (ex.ErrorCode == 42901 && ex.RetryAfter is { } retryAfter) // TooManyRequests
    {
        await Task.Delay(retryAfter, cancellationToken);
        // ...retry
    }

    if (ex.Details is not null)
    {
        foreach ((string field, string[] errors) in ex.Details)
        {
            logger.LogWarning("{Field}: {Errors}", field, string.Join(", ", errors));
        }
    }
}
```

| `ErrorCode` | HTTP | Meaning |
|---|---|---|
| `40001` | 400 | ValidationFailed - bad request body/query/form data, disallowed content type, oversized file, invalid `FolderPath`. |
| `40101` | 401 | Unauthorized - missing/invalid/revoked API key. |
| `40301` | 403 | Forbidden - key valid but not allowed for this tenant/bucket/file. |
| `40401` | 404 | NotFound - bucket/file/object key not found. |
| `40901` | 409 | Conflict - quota exceeded or conflicting state. |
| `42901` | 429 | TooManyRequests - rate limit/quota exceeded. `CdnApiException.RetryAfter` is populated when the server sends a `Retry-After` header. |
| `50001` | 500 | InternalServerError. |

`CorrelationId` is the server's trace id for the failed request - log it so support/backend teams
can cross-reference server logs.

## What the client covers

Access validation, bucket discovery, multipart upload, file listing/search, metadata lookup,
usage summary, authenticated download, soft delete, public URL building, and single/batch
presigned URLs. Anonymous redemption routes (`/public/...`, `/files/presigned/...`,
`/files/thumbnails/...`) are intentionally **not** wrapped here - those are meant to be hit
directly by the browser/mobile client using the URL your backend already produced, without an
API key.
