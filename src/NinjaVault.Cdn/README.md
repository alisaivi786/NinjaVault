<p align="center">
  <img src="https://raw.githubusercontent.com/alisaivi786/NinjaVault/main/assets/icon-512.png" width="112" alt="NinjaVault" />
</p>

<h1 align="center">NinjaVault.Cdn</h1>

<p align="center">
  Typed .NET client for the <b>NinjaVault CDN Server</b>: upload, search, download and share files with one injected interface.
</p>

<p align="center">
  <a href="https://www.nuget.org/packages/NinjaVault.Cdn"><img src="https://img.shields.io/nuget/v/NinjaVault.Cdn.svg?label=NinjaVault.Cdn" alt="NuGet version" /></a>
  <a href="https://www.nuget.org/packages/NinjaVault.Cdn"><img src="https://img.shields.io/nuget/dt/NinjaVault.Cdn.svg" alt="NuGet downloads" /></a>
  <a href="https://github.com/alisaivi786/NinjaVault/actions/workflows/ci.yml"><img src="https://github.com/alisaivi786/NinjaVault/actions/workflows/ci.yml/badge.svg" alt="CI" /></a>
  <img src="https://img.shields.io/badge/.NET-8%20%7C%209%20%7C%2010-512BD4" alt=".NET 8, 9, 10" />
  <a href="https://github.com/alisaivi786/NinjaVault/blob/main/LICENSE"><img src="https://img.shields.io/badge/license-MIT-green.svg" alt="MIT" /></a>
</p>

---

## Why NinjaVault.Cdn

- **One interface, every operation.** Upload, list/search, metadata, usage summary, download, soft delete, public URLs, and single or batch presigned URLs.
- **No HTTP plumbing.** Handles the `X-Api-Key` header, multipart fields, object-key encoding, and the JSON success/error envelope for you.
- **Typed errors.** Every failure is a `CdnApiException` with the HTTP status, CDN error code, trace id, validation details, and `Retry-After`.
- **Standalone, no opinions.** Depends only on `Microsoft.Extensions.*`. Logging, retries and correlation stay under your app's control: plug in whatever you already use.
- **Plays well with your stack.** Built on `IHttpClientFactory`, so you can chain logging handlers, Polly or `Microsoft.Extensions.Http.Resilience`, and the interface is easy to mock in tests.
- **Streams large files.** Downloads are handed to you as a stream and never buffered in memory.

---

## Quick start

**1. Install**

```bash
dotnet add package NinjaVault.Cdn
```

**2. Configure** (`appsettings.json`)

```json
{
  "NinjaVault": {
    "Cdn": {
      "BaseUrl": "https://cdn.example.com",
      "ApiKey": ""
    }
  }
}
```

Keep the API key out of the file:

```bash
dotnet user-secrets set "NinjaVault:Cdn:ApiKey" "cdn_xxxxx"     # local development
export NinjaVault__Cdn__ApiKey="cdn_xxxxx"                        # servers / containers
```

**3. Register and use**

```csharp
builder.Services.AddNinjaVaultCdn(builder.Configuration);
```

```csharp
public sealed class InvoiceService(INinjaVaultCdnClient cdn)
{
    public async Task<string> SaveAsync(Stream pdf, long tenantId, CancellationToken ct)
    {
        CdnUploadResult file = await cdn.UploadAsync(
            new CdnUploadRequest("documents", tenantId, pdf, "invoice.pdf", "application/pdf"), ct);

        return file.ObjectKey; // store Bucket + ObjectKey in your database
    }
}
```

That's it. Everything below is optional.

---

## Configuration reference

```json
{
  "NinjaVault": {
    "Cdn": {
      "BaseUrl": "https://cdn.example.com",
      "PublicBaseUrl": "https://cdn.example.com",
      "ApiKey": ""
    }
  }
}
```

| Key | Required | Default | Description |
|---|:---:|---|---|
| `NinjaVault:Cdn:BaseUrl` | ✅ | none | Base URL of the authenticated `/api/v1/...` routes. A path prefix works (`https://gateway.example.com/cdn`). |
| `NinjaVault:Cdn:ApiKey` | ✅ | none | The `X-Api-Key` issued for your integration. **Backend-only secret.** |
| `NinjaVault:Cdn:PublicBaseUrl` | | `BaseUrl` | Host for anonymous public files. **Host only**; the client appends `/public/`. |

Environment variables use `__` in place of `:` (for example `NinjaVault__Cdn__BaseUrl`).

### Configure in code instead

```csharp
builder.Services.AddNinjaVaultCdn(options =>
{
    options.BaseUrl = "https://cdn.example.com";
    options.ApiKey = builder.Configuration["Secrets:CdnApiKey"]!;
});
```

### Add resilience (retries, timeouts, circuit breaker)

Both `AddNinjaVaultCdn` overloads return `IHttpClientBuilder`:

```csharp
// dotnet add package Microsoft.Extensions.Http.Resilience
builder.Services.AddNinjaVaultCdn(builder.Configuration)
    .AddStandardResilienceHandler();
```

### Verify the key at startup (recommended)

```csharp
using (IServiceScope scope = app.Services.CreateScope())
{
    INinjaVaultCdnClient cdn = scope.ServiceProvider.GetRequiredService<INinjaVaultCdnClient>();
    CdnAccessContext access = await cdn.GetAccessContextAsync();
    // access.AllowedBuckets / AllowedTenantIds empty = unrestricted, not "no access"
}
```

---

## Usage

All examples assume `INinjaVaultCdnClient cdn` is injected and `ct` is a `CancellationToken`.

### Upload

```csharp
CdnUploadResult uploaded = await cdn.UploadAsync(new CdnUploadRequest(
    Bucket: "documents",
    TenantId: 42,
    File: stream,
    FileName: "report.docx",
    ContentType: "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    OwnerId: 1001,                 // optional
    FolderPath: "reports/2026"),   // optional: up to 8 segments of [A-Za-z0-9_-]
    ct);

// uploaded.Bucket + uploaded.ObjectKey identify the file from now on (not the original file name).
```

Allowed content types and the maximum size are set per CDN deployment (commonly PDF, JPEG, PNG, WEBP, DOC and DOCX, up to about 25 MB). Anything outside them fails with `40001`.

### Give a browser or mobile app access

| The file is in... | Use | Makes an HTTP call? | Link lifetime |
|---|---|:---:|---|
| a **Public** bucket | `cdn.BuildPublicUrl(bucket, objectKey)` | no | permanent |
| a **Private** bucket | `cdn.CreatePresignedUrlAsync(...)` | yes | short (you choose) |
| many private files | `cdn.CreatePresignedUrlsAsync(...)` | yes, once | short |

```csharp
string logoUrl = cdn.BuildPublicUrl("public-assets", objectKey);

CdnPresignedUrl link = await cdn.CreatePresignedUrlAsync(
    new CdnPresignRequest("documents", objectKey, ExpirySeconds: 600), ct);

CdnPresignBatchResult batch = await cdn.CreatePresignedUrlsAsync(new CdnPresignBatchRequest(
    [new CdnPresignTarget("documents", keyA), new CdnPresignTarget("documents", keyB)],
    ExpirySeconds: 600), ct);

foreach (CdnPresignFailure failed in batch.Failed)   // batch returns 200 even if some fail
{
    logger.LogWarning("{Key}: {Reason}", failed.ObjectKey, failed.Reason);
}
```

> Never send the API key to a browser or mobile app. Give them a public or presigned URL instead.

### Download (backend to backend)

```csharp
await using CdnFileDownload download = await cdn.DownloadAsync("documents", objectKey, ct);

// Stream straight through without loading the file into memory
return Results.Stream(download.Content, download.ContentType, download.FileName);
```

### Search and list

```csharp
CdnPagedResult<CdnFileObject> page = await cdn.ListFilesAsync(new CdnFileListQuery
{
    Bucket = "documents",
    TenantId = 42,
    OwnerId = 1001,
    ObjectKeyPrefix = "reports/2026",
    FileNameContains = "invoice",
    Category = CdnFileCategory.Document,
    CreatedFromUtc = DateTimeOffset.UtcNow.AddDays(-30),
    SortBy = CdnFileSortBy.CreatedAtUtc,
    SortDescending = true,
    Page = 1,
    PageSize = 50
}, ct);
```

### Metadata, usage, buckets, delete

```csharp
CdnFileObject info       = await cdn.GetMetadataAsync("documents", objectKey, ct); // checksum, category, thumbnail...
CdnFileSummary usage     = await cdn.GetSummaryAsync("documents", ct);             // totals per category
IReadOnlyList<CdnBucket> buckets = await cdn.ListBucketsAsync(ct);                 // visibility + counters
await cdn.DeleteAsync("documents", objectKey, ct);                                 // soft delete
```

---

## Error handling

```csharp
try
{
    await cdn.UploadAsync(request, ct);
}
catch (CdnApiException ex) when (ex.ErrorCode == 42901 && ex.RetryAfter is { } wait)
{
    await Task.Delay(wait, ct); // rate limited: back off exactly as long as the server asks
}
catch (CdnApiException ex)
{
    logger.LogWarning("CDN {Status} {Code}: {Message} (trace {TraceId})",
        (int?)ex.StatusCode, ex.ErrorCode, ex.Message, ex.CorrelationId);

    foreach ((string field, string[] errors) in ex.Details ?? new Dictionary<string, string[]>())
    {
        logger.LogWarning("{Field}: {Errors}", field, string.Join(", ", errors));
    }
}
```

| `ErrorCode` | HTTP | Meaning | What to check |
|---|:---:|---|---|
| `40001` | 400 | Validation failed | `FolderPath` format, content type, file size, `ex.Details` |
| `40101` | 401 | Unauthorized | `ApiKey` missing, mistyped, or revoked |
| `40301` | 403 | Forbidden | Key not allowed for this bucket or tenant (`GetAccessContextAsync`) |
| `40401` | 404 | Not found | Bucket name or object key (use `ObjectKey`, not the file name) |
| `40901` | 409 | Conflict | Quota exceeded or conflicting state |
| `42901` | 429 | Too many requests | Wait for `ex.RetryAfter` |
| `50001` | 500 | Server error | Retry later; send `ex.CorrelationId` to the CDN team |

---

## Logging, correlation and retries: your choice

`NinjaVault.Cdn` adds no logging of its own. `AddNinjaVaultCdn(...)` returns the `IHttpClientBuilder` for the
named client **`NinjaVaultCdn`** (`DependencyInjection.ServiceName`), so you attach exactly what your app uses.

| You want... | Add |
|---|---|
| Nothing extra | Nothing. `IHttpClientFactory` already writes request start/end entries to `ILogger` under the category `System.Net.Http.HttpClient.NinjaVaultCdn.*`, so they reach Serilog, Seq or the console. |
| Ready-made request/response logging, a DB sink and `X-Correlation-Id` | [NinjaVault.Http](https://www.nuget.org/packages/NinjaVault.Http) (optional package): `.AddNinjaVaultHttpLogging(DependencyInjection.ServiceName)` |
| Microsoft's structured HTTP logging | `Microsoft.Extensions.Http.Diagnostics`: `.AddExtendedHttpClientLogging()` |
| Your own logging or correlation | Your own `DelegatingHandler`: `.AddHttpMessageHandler<MyHandler>()` |
| Retries, timeouts, circuit breaker | `Microsoft.Extensions.Http.Resilience`: `.AddStandardResilienceHandler()` |

```csharp
// dotnet add package NinjaVault.Http
builder.Services.AddNinjaVaultHttp(builder.Configuration);            // optional: binds NinjaVault:ExternalApiLogging
builder.Services.AddNinjaVaultCdn(builder.Configuration)
    .AddNinjaVaultHttpLogging(DependencyInjection.ServiceName)        // logging + correlation id
    .AddStandardResilienceHandler();                                  // retries
```

> If you write your own handler, remember that every CDN request carries the `X-Api-Key` header. Redact it
> before logging headers. NinjaVault.Http does this for you.

---

## Unit testing your code

Depend on `INinjaVaultCdnClient` and mock it with any library:

```csharp
var cdn = Substitute.For<INinjaVaultCdnClient>();
cdn.BuildPublicUrl("public-assets", "logo.png").Returns("https://cdn.test/public/public-assets/logo.png");

var service = new BrandingService(cdn);
```

---

## Troubleshooting

| Symptom | Fix |
|---|---|
| Public URLs contain `/public/public/` | Set `PublicBaseUrl` to the host only (`https://cdn.example.com`), or remove it. |
| `ArgumentException: BaseUrl` / `ApiKey` | The `NinjaVault:Cdn` section wasn't found. Check the section name and your environment's appsettings file. |
| `40101` on every call | Wrong or rotated key. Check the environment variable `NinjaVault__Cdn__ApiKey`. |
| `40001` on upload | Invalid `FolderPath` (max 8 segments of `[A-Za-z0-9_-]`, each starting with a letter or digit), or a file type/size the deployment doesn't allow. |
| File not found after upload | Fetch with the returned `ObjectKey`, not the original file name. |

---

## Compatibility

| | |
|---|---|
| Target frameworks | `net8.0`, `net9.0`, `net10.0` |
| Dependencies | `Microsoft.Extensions.Http`, `Options.ConfigurationExtensions` 8.0+ (nothing else) |
| Source Link / symbols | ✅ Step into the package source while debugging |

## Links

- Source, issues and release notes: <https://github.com/alisaivi786/NinjaVault>
- Change-sets: <https://github.com/alisaivi786/NinjaVault/tree/main/changesets/NinjaVault.Cdn>
- License: MIT
