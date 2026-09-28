namespace NinjaVault.Cdn
{
    public sealed class NinjaVaultCdnClient(HttpClient httpClient, IOptions<NinjaVaultCdnOptions> options) : INinjaVaultCdnClient
    {
        private const string ApiKeyHeaderName = "X-Api-Key";

        private static readonly string UserAgent =
            "NinjaVault.Cdn/" + (typeof(NinjaVaultCdnClient).Assembly.GetName().Version?.ToString() ?? "0.0.0.0");

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            Converters =
            {
                new JsonStringEnumConverter()
            }
        };

        public async Task<CdnAccessContext> GetAccessContextAsync(CancellationToken cancellationToken = default)
        {
            using HttpRequestMessage request = CreateRequest(HttpMethod.Get, "/api/v1/me");
            return await SendJsonAsync<CdnAccessContext>(request, cancellationToken);
        }

        public async Task<IReadOnlyList<CdnBucket>> ListBucketsAsync(CancellationToken cancellationToken = default)
        {
            using HttpRequestMessage request = CreateRequest(HttpMethod.Get, "/api/v1/buckets");
            return await SendJsonAsync<IReadOnlyList<CdnBucket>>(request, cancellationToken);
        }

        public async Task<CdnUploadResult> UploadAsync(CdnUploadRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(request.File);
            ThrowIfBlank(request.Bucket, nameof(request.Bucket));
            ThrowIfBlank(request.FileName, nameof(request.FileName));

            using MultipartFormDataContent content = new()
            {
                { new StringContent(request.Bucket), "Bucket" },
                { new StringContent(request.TenantId.ToString(CultureInfo.InvariantCulture)), "TenantId" }
            };
            if (request.OwnerId.HasValue)
            {
                content.Add(new StringContent(request.OwnerId.Value.ToString(CultureInfo.InvariantCulture)), "OwnerId");
            }

            AddStringContent(content, "FolderPath", request.FolderPath);

            StreamContent fileContent = new(request.File);
            if (!string.IsNullOrWhiteSpace(request.ContentType))
            {
                fileContent.Headers.ContentType = new(request.ContentType);
            }

            content.Add(fileContent, "File", request.FileName);

            using HttpRequestMessage httpRequest = CreateRequest(HttpMethod.Post, "/api/v1/files");
            httpRequest.Content = content;

            return await SendJsonAsync<CdnUploadResult>(httpRequest, cancellationToken);
        }

        public async Task<CdnPagedResult<CdnFileObject>> ListFilesAsync(CdnFileListQuery? query = null, CancellationToken cancellationToken = default)
        {
            string path = "/api/v1/files" + BuildFileListQuery(query);
            using HttpRequestMessage request = CreateRequest(HttpMethod.Get, path);
            return await SendJsonAsync<CdnPagedResult<CdnFileObject>>(request, cancellationToken);
        }

        public async Task<CdnFileObject> GetMetadataAsync(string bucket, string objectKey, CancellationToken cancellationToken = default)
        {
            ThrowIfBlank(bucket, nameof(bucket));
            ThrowIfBlank(objectKey, nameof(objectKey));

            using HttpRequestMessage request = CreateRequest(HttpMethod.Get, $"/api/v1/file-metadata/{EncodePathSegment(bucket)}/{EncodeObjectKey(objectKey)}");
            return await SendJsonAsync<CdnFileObject>(request, cancellationToken);
        }

        public async Task<CdnFileSummary> GetSummaryAsync(string? bucket = null, CancellationToken cancellationToken = default)
        {
            string path = string.IsNullOrWhiteSpace(bucket)
                ? "/api/v1/files/summary"
                : "/api/v1/files/summary?bucket=" + Uri.EscapeDataString(bucket);

            using HttpRequestMessage request = CreateRequest(HttpMethod.Get, path);
            return await SendJsonAsync<CdnFileSummary>(request, cancellationToken);
        }

        public async Task<CdnFileDownload> DownloadAsync(string bucket, string objectKey, CancellationToken cancellationToken = default)
        {
            ThrowIfBlank(bucket, nameof(bucket));
            ThrowIfBlank(objectKey, nameof(objectKey));

            using HttpRequestMessage request = CreateRequest(HttpMethod.Get, $"/api/v1/files/{EncodePathSegment(bucket)}/{EncodeObjectKey(objectKey)}");
            HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                await ThrowApiExceptionAsync(response, cancellationToken);
            }

            Stream content = await response.Content.ReadAsStreamAsync(cancellationToken);
            return new CdnFileDownload(
                new ResponseOwnedStream(content, response),
                response.Content.Headers.ContentType?.MediaType,
                response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName,
                response.Content.Headers.ContentLength);
        }

        public async Task DeleteAsync(string bucket, string objectKey, CancellationToken cancellationToken = default)
        {
            ThrowIfBlank(bucket, nameof(bucket));
            ThrowIfBlank(objectKey, nameof(objectKey));

            using HttpRequestMessage request = CreateRequest(HttpMethod.Delete, $"/api/v1/files/{EncodePathSegment(bucket)}/{EncodeObjectKey(objectKey)}");
            await SendWithoutResultAsync(request, cancellationToken);
        }

        public async Task<CdnPresignedUrl> CreatePresignedUrlAsync(CdnPresignRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            using HttpRequestMessage httpRequest = CreateRequest(HttpMethod.Post, "/api/v1/files/presign");
            httpRequest.Content = JsonContent.Create(request, options: JsonOptions);

            return await SendJsonAsync<CdnPresignedUrl>(httpRequest, cancellationToken);
        }

        public async Task<CdnPresignBatchResult> CreatePresignedUrlsAsync(CdnPresignBatchRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            using HttpRequestMessage httpRequest = CreateRequest(HttpMethod.Post, "/api/v1/files/presign/batch");
            httpRequest.Content = JsonContent.Create(request, options: JsonOptions);

            return await SendJsonAsync<CdnPresignBatchResult>(httpRequest, cancellationToken);
        }

        public string BuildPublicUrl(string bucket, string objectKey)
        {
            ThrowIfBlank(bucket, nameof(bucket));
            ThrowIfBlank(objectKey, nameof(objectKey));

            string baseUrl = options.Value.PublicBaseUrl ?? options.Value.BaseUrl;
            ThrowIfBlank(baseUrl, nameof(NinjaVaultCdnOptions.PublicBaseUrl));

            return baseUrl.TrimEnd('/') + "/public/" + EncodePathSegment(bucket) + "/" + EncodeObjectKey(objectKey);
        }

        private HttpRequestMessage CreateRequest(HttpMethod method, string path)
        {
            ThrowIfBlank(options.Value.BaseUrl, nameof(NinjaVaultCdnOptions.BaseUrl));
            ThrowIfBlank(options.Value.ApiKey, nameof(NinjaVaultCdnOptions.ApiKey));

            // Always absolute: keeps any path prefix on BaseUrl (e.g. https://host/cdn) and never mutates the
            // shared HttpClient.BaseAddress, which throws once the client has started sending requests.
            Uri requestUri = new(options.Value.BaseUrl.TrimEnd('/') + path, UriKind.Absolute);

            HttpRequestMessage request = new(method, requestUri);
            request.Headers.TryAddWithoutValidation(ApiKeyHeaderName, options.Value.ApiKey);
            request.Headers.UserAgent.ParseAdd(UserAgent);
            return request;
        }

        // Delete may answer 204 No Content or an envelope with no data; only a failure envelope is an error.
        private async Task SendWithoutResultAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                await ThrowApiExceptionAsync(response, cancellationToken);
            }

            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(body))
            {
                return;
            }

            ApiEnvelope<JsonElement>? envelope = JsonSerializer.Deserialize<ApiEnvelope<JsonElement>>(body, JsonOptions);
            if (envelope is { Success: false })
            {
                CdnApiError? error = envelope.Error;
                throw new CdnApiException(
                    error?.Message ?? "CDN request failed.",
                    response.StatusCode,
                    error?.ErrorCode,
                    error?.Description,
                    error?.CorrelationId,
                    error?.Details,
                    retryAfter: GetRetryAfter(response));
            }
        }

        private async Task<T> SendJsonAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                await ThrowApiExceptionAsync(response, cancellationToken);
            }

            ApiEnvelope<T>? envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<T>>(JsonOptions, cancellationToken);
            if (envelope is null)
            {
                throw new CdnApiException("CDN response body was empty.", response.StatusCode);
            }

            if (!envelope.Success)
            {
                CdnApiError? error = envelope.Error;
                throw new CdnApiException(
                    error?.Message ?? "CDN request failed.",
                    response.StatusCode,
                    error?.ErrorCode,
                    error?.Description,
                    error?.CorrelationId,
                    error?.Details,
                    retryAfter: GetRetryAfter(response));
            }

            return envelope.Data!;
        }

        private static async Task ThrowApiExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            CdnApiError? error = null;
            string? responseBody = null;

            if (response.Content is not null)
            {
                responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(responseBody))
                {
                    try
                    {
                        ApiEnvelope<JsonElement>? envelope = JsonSerializer.Deserialize<ApiEnvelope<JsonElement>>(responseBody, JsonOptions);
                        error = envelope?.Error;
                    }
                    catch (JsonException)
                    {
                        // Anonymous/download routes may return raw error bodies instead of the JSON envelope.
                    }
                }
            }

            throw new CdnApiException(
                error?.Message ?? $"CDN request failed with HTTP {(int)response.StatusCode}.",
                response.StatusCode,
                error?.ErrorCode,
                error?.Description ?? responseBody,
                error?.CorrelationId,
                error?.Details,
                retryAfter: GetRetryAfter(response));
        }

        private static TimeSpan? GetRetryAfter(HttpResponseMessage response)
        {
            RetryConditionHeaderValue? retryAfter = response.Headers.RetryAfter;
            if (retryAfter is null)
            {
                return null;
            }

            if (retryAfter.Delta.HasValue)
            {
                return retryAfter.Delta;
            }

            if (retryAfter.Date.HasValue)
            {
                TimeSpan delay = retryAfter.Date.Value - DateTimeOffset.UtcNow;
                return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
            }

            return null;
        }

        private static string BuildFileListQuery(CdnFileListQuery? query)
        {
            if (query is null)
            {
                return string.Empty;
            }

            QueryBuilder builder = new();
            builder.Add("bucket", query.Bucket);
            builder.Add("tenantId", query.TenantId);
            builder.Add("ownerId", query.OwnerId);
            builder.Add("objectKeyPrefix", query.ObjectKeyPrefix);
            builder.Add("fileNameContains", query.FileNameContains);
            builder.Add("contentType", query.ContentType);
            builder.Add("category", query.Category);
            builder.Add("visibility", query.Visibility);
            builder.Add("createdFromUTC", query.CreatedFromUtc);
            builder.Add("createdToUTC", query.CreatedToUtc);
            builder.Add("includeDeleted", query.IncludeDeleted);
            builder.Add("sortBy", query.SortBy);
            builder.Add("sortDescending", query.SortDescending);
            builder.Add("page", query.Page);
            builder.Add("pageSize", query.PageSize);
            return builder.ToString();
        }

        private static void AddStringContent(MultipartFormDataContent content, string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                content.Add(new StringContent(value), name);
            }
        }

        private static string EncodeObjectKey(string objectKey)
        {
            ThrowIfBlank(objectKey, nameof(objectKey));
            return string.Join('/', objectKey.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(EncodePathSegment));
        }

        private static string EncodePathSegment(string value)
        {
            ThrowIfBlank(value, nameof(value));
            return Uri.EscapeDataString(value);
        }

        private static void ThrowIfBlank(string? value, string paramName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Value cannot be null or whitespace.", paramName);
            }
        }

        private sealed record ApiEnvelope<T>(bool Success, T? Data, CdnApiError? Error);

        // The server's error envelope names this field "traceId",
        // not "correlationId" - map it explicitly so CdnApiException.CorrelationId actually populates.
        private sealed record CdnApiError(
            int ErrorCode,
            string Message,
            string? Description,
            [property: JsonPropertyName("traceId")] string? CorrelationId,
            IReadOnlyDictionary<string, string[]>? Details);

        private sealed class QueryBuilder
        {
            private readonly List<string> _parts = [];

            public void Add<T>(string name, T? value)
            {
                if (value is null)
                {
                    return;
                }

                string text = value switch
                {
                    DateTimeOffset dateTime => dateTime.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
                    bool boolean => boolean.ToString(CultureInfo.InvariantCulture).ToLowerInvariant(),
                    IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                    _ => value.ToString() ?? string.Empty
                };

                if (string.IsNullOrWhiteSpace(text))
                {
                    return;
                }

                _parts.Add(Uri.EscapeDataString(name) + "=" + Uri.EscapeDataString(text));
            }

            public override string ToString()
                => _parts.Count == 0 ? string.Empty : "?" + string.Join('&', _parts);
        }
    }
}