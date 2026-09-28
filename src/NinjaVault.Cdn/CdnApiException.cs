namespace NinjaVault.Cdn
{
    public sealed class CdnApiException : HttpRequestException
    {
        public CdnApiException()
            : this("CDN request failed.", HttpStatusCode.InternalServerError)
        {
        }

        public CdnApiException(string message)
            : this(message, HttpStatusCode.InternalServerError)
        {
        }

        public CdnApiException(string message, Exception innerException)
            : this(message, HttpStatusCode.InternalServerError, innerException: innerException)
        {
        }

        public CdnApiException(
            string message,
            HttpStatusCode statusCode,
            int? errorCode = null,
            string? description = null,
            string? correlationId = null,
            IReadOnlyDictionary<string, string[]>? details = null,
            Exception? innerException = null,
            TimeSpan? retryAfter = null)
            : base(message, innerException, statusCode)
        {
            ErrorCode = errorCode;
            Description = description;
            CorrelationId = correlationId;
            Details = details;
            RetryAfter = retryAfter;
        }

        public int? ErrorCode { get; }
        public string? Description { get; }

        /// <summary>Server-provided trace id for the failed request (from the error envelope's "traceId" field). Log this for support/debugging.</summary>
        public string? CorrelationId { get; }
        public IReadOnlyDictionary<string, string[]>? Details { get; }

        /// <summary>
        /// Populated from the response's Retry-After header when the server returns HTTP 429 (error code 42901,
        /// TooManyRequests). Null for all other errors. Use this to implement backoff instead of guessing a delay.
        /// </summary>
        public TimeSpan? RetryAfter { get; }
    }
}