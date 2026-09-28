using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace NinjaVault.Cdn.Tests;

public sealed class NinjaVaultCdnClientTests
{
    [Fact]
    public async Task ListBucketsAsync_SendsApiKeyAndReadsEnvelope()
    {
        StubHttpMessageHandler handler = new((_, _) => Task.FromResult(StubResponses.Ok("""
        {
          "success": true,
          "data": [
            { "name": "documents", "visibility": "Private", "fileCount": 12, "totalSizeBytes": 2048000 }
          ]
        }
        """)));

        INinjaVaultCdnClient client = CreateClient(handler);

        IReadOnlyList<CdnBucket> buckets = await client.ListBucketsAsync(TestContext.Current.CancellationToken);

        Assert.Single(buckets);
        Assert.Equal("documents", buckets[0].Name);
        CapturedRequest request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://cdn.test/api/v1/buckets", request.Uri?.ToString());
        Assert.Equal("cdn_secret", Assert.Single(request.Headers["X-Api-Key"]));
    }

    [Fact]
    public async Task UploadAsync_SendsExpectedMultipartFields()
    {
        StubHttpMessageHandler handler = new(async (request, cancellationToken) =>
        {
            _ = await request.Content!.ReadAsStringAsync(cancellationToken);
            return StubResponses.Ok("""
            {
              "success": true,
              "data": {
                "id": "1cf8e5e6-21f0-49a5-90b1-5c0f985c9df7",
                "bucket": "documents",
                "objectKey": "owner-token/invoices/2026/file.pdf",
                "originalFileName": "invoice.pdf",
                "contentType": "application/pdf",
                "sizeBytes": 7,
                "visibility": "Private",
                "url": "https://cdn.test/api/v1/files/documents/owner-token/invoices/2026/file.pdf"
              }
            }
            """);
        });

        INinjaVaultCdnClient client = CreateClient(handler);
        using MemoryStream stream = new(Encoding.UTF8.GetBytes("payload"));

        CdnUploadResult uploaded = await client.UploadAsync(
            new CdnUploadRequest("documents", 42, stream, "invoice.pdf", "application/pdf", 1001, "invoices/2026"),
            TestContext.Current.CancellationToken);

        Assert.Equal("owner-token/invoices/2026/file.pdf", uploaded.ObjectKey);
        Assert.Equal(CdnBucketVisibility.Private, uploaded.Visibility);
        CapturedRequest captured = Assert.Single(handler.Requests);
        Assert.Contains("name=Bucket", captured.Body, StringComparison.Ordinal);
        Assert.Contains("documents", captured.Body, StringComparison.Ordinal);
        Assert.Contains("name=TenantId", captured.Body, StringComparison.Ordinal);
        Assert.Contains("42", captured.Body, StringComparison.Ordinal);
        Assert.Contains("name=OwnerId", captured.Body, StringComparison.Ordinal);
        Assert.Contains("1001", captured.Body, StringComparison.Ordinal);
        Assert.Contains("name=FolderPath", captured.Body, StringComparison.Ordinal);
        Assert.Contains("invoices/2026", captured.Body, StringComparison.Ordinal);
        Assert.Contains("name=File", captured.Body, StringComparison.Ordinal);
        Assert.Contains("filename=invoice.pdf", captured.Body, StringComparison.Ordinal);
        Assert.Contains("Content-Type: application/pdf", captured.Body, StringComparison.Ordinal);
        Assert.Contains("payload", captured.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListFilesAsync_BuildsGuideQueryNames()
    {
        StubHttpMessageHandler handler = new((_, _) => Task.FromResult(StubResponses.Ok("""
        {
          "success": true,
          "data": { "items": [], "totalCount": 0, "page": 2, "pageSize": 25 }
        }
        """)));

        INinjaVaultCdnClient client = CreateClient(handler);

        _ = await client.ListFilesAsync(
            new CdnFileListQuery
            {
                Bucket = "documents",
                TenantId = 42,
                ObjectKeyPrefix = "owner-token/invoices",
                Category = CdnFileCategory.Document,
                Visibility = CdnBucketVisibility.Private,
                SortBy = CdnFileSortBy.CreatedAtUtc,
                SortDescending = false,
                Page = 2,
                PageSize = 25
            },
            TestContext.Current.CancellationToken);

        string url = Assert.Single(handler.Requests).Uri!.ToString();
        Assert.Contains("bucket=documents", url, StringComparison.Ordinal);
        Assert.Contains("tenantId=42", url, StringComparison.Ordinal);
        Assert.Contains("objectKeyPrefix=owner-token%2Finvoices", url, StringComparison.Ordinal);
        Assert.Contains("category=Document", url, StringComparison.Ordinal);
        Assert.Contains("visibility=Private", url, StringComparison.Ordinal);
        Assert.Contains("sortBy=CreatedAtUtc", url, StringComparison.Ordinal);
        Assert.Contains("sortDescending=false", url, StringComparison.Ordinal);
        Assert.Contains("page=2", url, StringComparison.Ordinal);
        Assert.Contains("pageSize=25", url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetMetadataAsync_EncodesBucketAndObjectKeySegments()
    {
        StubHttpMessageHandler handler = new((_, _) => Task.FromResult(StubResponses.Ok("""
        {
          "success": true,
          "data": {
            "id": "1cf8e5e6-21f0-49a5-90b1-5c0f985c9df7",
            "tenantId": 42,
            "ownerId": null,
            "bucket": "documents",
            "objectKey": "owner token/a file.pdf",
            "originalFileName": "a file.pdf",
            "contentType": "application/pdf",
            "sizeBytes": 1,
            "checksum": null,
            "visibility": "Private",
            "extension": "pdf",
            "category": "Document",
            "thumbnailStatus": "NotApplicable",
            "thumbnailUrl": null,
            "createdAtUTC": "2026-07-26T15:00:00Z",
            "url": "https://cdn.test"
          }
        }
        """)));

        INinjaVaultCdnClient client = CreateClient(handler);

        _ = await client.GetMetadataAsync("my docs", "owner token/a file.pdf", TestContext.Current.CancellationToken);

        Assert.Equal("https://cdn.test/api/v1/file-metadata/my%20docs/owner%20token/a%20file.pdf", Assert.Single(handler.Requests).Uri?.AbsoluteUri);
    }

    [Fact]
    public async Task CreatePresignedUrlAsync_SendsJsonBody()
    {
        StubHttpMessageHandler handler = new((_, _) => Task.FromResult(StubResponses.Ok("""
        {
          "success": true,
          "data": {
            "url": "https://cdn.test/files/presigned/documents/a.pdf?expires=1&sig=abc",
            "expiresAtUtc": "2026-07-26T15:20:00+00:00"
          }
        }
        """)));

        INinjaVaultCdnClient client = CreateClient(handler);

        CdnPresignedUrl result = await client.CreatePresignedUrlAsync(
            new CdnPresignRequest("documents", "a.pdf", 300),
            TestContext.Current.CancellationToken);

        Assert.Equal("https://cdn.test/files/presigned/documents/a.pdf?expires=1&sig=abc", result.Url);
        CapturedRequest request = Assert.Single(handler.Requests);
        Assert.Equal("https://cdn.test/api/v1/files/presign", request.Uri?.ToString());
        Assert.Contains("\"bucket\":\"documents\"", request.Body, StringComparison.Ordinal);
        Assert.Contains("\"objectKey\":\"a.pdf\"", request.Body, StringComparison.Ordinal);
        Assert.Contains("\"expirySeconds\":300", request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ErrorEnvelope_ThrowsCdnApiExceptionWithCorrelationId()
    {
        // Server's ApiError envelope names this field "traceId", not "correlationId".
        StubHttpMessageHandler handler = new((_, _) => Task.FromResult(StubResponses.Json(HttpStatusCode.Forbidden, """
        {
          "success": false,
          "error": {
            "errorCode": 40301,
            "message": "Forbidden",
            "description": "Bucket is not allowed.",
            "traceId": "corr-1",
            "details": { "Bucket": ["Denied"] }
          }
        }
        """)));

        INinjaVaultCdnClient client = CreateClient(handler);

        CdnApiException exception = await Assert.ThrowsAsync<CdnApiException>(
            () => client.ListBucketsAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.Equal(40301, exception.ErrorCode);
        Assert.Equal("corr-1", exception.CorrelationId);
        Assert.Equal("Denied", Assert.Single(exception.Details!["Bucket"]));
    }

    [Fact]
    public async Task RateLimitError_ExposesRetryAfter()
    {
        StubHttpMessageHandler handler = new((_, _) =>
        {
            HttpResponseMessage response = StubResponses.Json(HttpStatusCode.TooManyRequests, """
            {
              "success": false,
              "error": {
                "errorCode": 42901,
                "message": "Too many requests",
                "description": "Rate limit exceeded.",
                "traceId": "corr-2",
                "details": null
              }
            }
            """);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            return Task.FromResult(response);
        });

        INinjaVaultCdnClient client = CreateClient(handler);

        CdnApiException exception = await Assert.ThrowsAsync<CdnApiException>(
            () => client.ListBucketsAsync(TestContext.Current.CancellationToken));

        Assert.Equal(42901, exception.ErrorCode);
        Assert.Equal(TimeSpan.FromSeconds(30), exception.RetryAfter);
    }

    [Fact]
    public async Task GetMetadataAsync_DeserializesNumericOwnerId()
    {
        StubHttpMessageHandler handler = new((_, _) => Task.FromResult(StubResponses.Ok("""
        {
          "success": true,
          "data": {
            "id": "1cf8e5e6-21f0-49a5-90b1-5c0f985c9df7",
            "tenantId": 42,
            "ownerId": 1001,
            "bucket": "documents",
            "objectKey": "owner-token/invoices/2026/file.pdf",
            "originalFileName": "invoice.pdf",
            "contentType": "application/pdf",
            "sizeBytes": 7,
            "checksum": null,
            "visibility": "Private",
            "extension": "pdf",
            "category": "Document",
            "thumbnailStatus": "NotApplicable",
            "thumbnailUrl": null,
            "createdAtUTC": "2026-07-26T15:00:00Z",
            "url": "https://cdn.test"
          }
        }
        """)));

        INinjaVaultCdnClient client = CreateClient(handler);

        CdnFileObject file = await client.GetMetadataAsync("documents", "owner-token/invoices/2026/file.pdf", TestContext.Current.CancellationToken);

        Assert.Equal(1001, file.OwnerId);
    }

    [Fact]
    public void BuildPublicUrl_UsesPublicBaseUrlWhenConfigured()
    {
        INinjaVaultCdnClient client = CreateClient(new StubHttpMessageHandler((_, _) => Task.FromResult(StubResponses.Ok("{}"))));

        string url = client.BuildPublicUrl("public assets", "owner token/logo one.png");

        Assert.Equal("https://public.test/public/public%20assets/owner%20token/logo%20one.png", url);
    }

    [Fact]
    public void AddNinjaVaultCdn_RegistersClientWithSharedHttpInfrastructure()
    {
        Dictionary<string, string?> values = new()
        {
            ["NinjaVault:Cdn:BaseUrl"] = "https://cdn.test",
            ["NinjaVault:Cdn:ApiKey"] = "cdn_secret"
        };
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        ServiceCollection services = new();

        services.AddNinjaVaultCdn(configuration);

        using ServiceProvider provider = services.BuildServiceProvider();
        INinjaVaultCdnClient client = provider.GetRequiredService<INinjaVaultCdnClient>();

        Assert.IsType<NinjaVaultCdnClient>(client);
    }

    [Fact]
    public async Task AddNinjaVaultCdn_RunsConsumerHandlersOnEveryCall()
    {
        ServiceCollection services = new();
        StubCdnPrimaryHandler primaryHandler = new();
        RecordingHandler consumerHandler = new();

        services.AddSingleton(primaryHandler);
        services.AddSingleton<IHttpMessageHandlerBuilderFilter, StubPrimaryHandlerFilter>();
        services.AddNinjaVaultCdn(options =>
        {
            options.BaseUrl = "https://cdn.test";
            options.ApiKey = "cdn_secret";
        }).AddHttpMessageHandler(() => consumerHandler);

        using ServiceProvider provider = services.BuildServiceProvider();
        INinjaVaultCdnClient client = provider.GetRequiredService<INinjaVaultCdnClient>();

        _ = await client.ListBucketsAsync(TestContext.Current.CancellationToken);

        string seen = Assert.Single(consumerHandler.Requests);
        Assert.Equal("GET https://cdn.test/api/v1/buckets", seen);
        Assert.Single(primaryHandler.Requests);
    }

    [Fact]
    public void AddNinjaVaultCdn_DoesNotRegisterLoggingInfrastructure()
    {
        ServiceCollection services = new();

        services.AddNinjaVaultCdn(options =>
        {
            options.BaseUrl = "https://cdn.test";
            options.ApiKey = "cdn_secret";
        });

        Assert.DoesNotContain(services, d => d.ServiceType.Namespace?.StartsWith("NinjaVault.Http", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task DeleteAsync_AcceptsNoContentResponse()
    {
        StubHttpMessageHandler handler = new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)));
        INinjaVaultCdnClient client = CreateClient(handler);

        await client.DeleteAsync("documents", "a.pdf", TestContext.Current.CancellationToken);

        CapturedRequest request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal("https://cdn.test/api/v1/files/documents/a.pdf", request.Uri?.ToString());
    }

    [Fact]
    public async Task DeleteAsync_ThrowsWhenEnvelopeReportsFailure()
    {
        StubHttpMessageHandler handler = new((_, _) => Task.FromResult(StubResponses.Ok("""
        { "success": false, "error": { "errorCode": 40401, "message": "Not found", "traceId": "corr-3" } }
        """)));
        INinjaVaultCdnClient client = CreateClient(handler);

        CdnApiException exception = await Assert.ThrowsAsync<CdnApiException>(
            () => client.DeleteAsync("documents", "a.pdf", TestContext.Current.CancellationToken));

        Assert.Equal(40401, exception.ErrorCode);
        Assert.Equal("corr-3", exception.CorrelationId);
    }

    [Fact]
    public async Task Requests_KeepBaseUrlPathPrefix()
    {
        StubHttpMessageHandler handler = new((_, _) => Task.FromResult(StubResponses.Ok("""{ "success": true, "data": [] }""")));
        INinjaVaultCdnClient client = CreateClient(handler, baseUrl: "https://gateway.test/cdn/");

        _ = await client.ListBucketsAsync(TestContext.Current.CancellationToken);

        Assert.Equal("https://gateway.test/cdn/api/v1/buckets", Assert.Single(handler.Requests).Uri?.ToString());
    }

    [Fact]
    public async Task Requests_SendVersionedUserAgent()
    {
        StubHttpMessageHandler handler = new((_, _) => Task.FromResult(StubResponses.Ok("""{ "success": true, "data": [] }""")));
        INinjaVaultCdnClient client = CreateClient(handler);

        _ = await client.ListBucketsAsync(TestContext.Current.CancellationToken);

        string userAgent = Assert.Single(Assert.Single(handler.Requests).Headers["User-Agent"]);
        Assert.StartsWith("NinjaVault.Cdn/", userAgent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DownloadAsync_StreamsContentAndMetadata()
    {
        StubHttpMessageHandler handler = new((_, _) =>
        {
            HttpResponseMessage response = new(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes("file-bytes")) };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
            response.Content.Headers.ContentDisposition = new System.Net.Http.Headers.ContentDispositionHeaderValue("attachment") { FileName = "a.pdf" };
            return Task.FromResult(response);
        });
        INinjaVaultCdnClient client = CreateClient(handler);

        await using CdnFileDownload download = await client.DownloadAsync("documents", "a.pdf", TestContext.Current.CancellationToken);
        using MemoryStream copy = new();
        await download.Content.CopyToAsync(copy, TestContext.Current.CancellationToken);

        Assert.Equal("file-bytes", Encoding.UTF8.GetString(copy.ToArray()));
        Assert.Equal("application/pdf", download.ContentType);
        Assert.Equal("a.pdf", download.FileName);
    }

    [Fact]
    public void AddNinjaVaultCdn_WithCodeOptions_RegistersClient()
    {
        ServiceCollection services = new();

        services.AddNinjaVaultCdn(options =>
        {
            options.BaseUrl = "https://cdn.test";
            options.ApiKey = "cdn_secret";
        });

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.IsType<NinjaVaultCdnClient>(provider.GetRequiredService<INinjaVaultCdnClient>());
        Assert.Equal("https://cdn.test", provider.GetRequiredService<IOptions<NinjaVaultCdnOptions>>().Value.BaseUrl);
    }

    private static INinjaVaultCdnClient CreateClient(StubHttpMessageHandler handler, string baseUrl = "https://cdn.test")
    {
        HttpClient httpClient = new(handler);
        return new NinjaVaultCdnClient(
            httpClient,
            Options.Create(new NinjaVaultCdnOptions
            {
                BaseUrl = baseUrl,
                PublicBaseUrl = "https://public.test",
                ApiKey = "cdn_secret"
            }));
    }

}