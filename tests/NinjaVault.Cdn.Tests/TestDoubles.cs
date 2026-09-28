using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Microsoft.Extensions.Http;

namespace NinjaVault.Cdn.Tests;

internal sealed class StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
{
    private readonly ConcurrentQueue<CapturedRequest> _requests = new();

    public IReadOnlyList<CapturedRequest> Requests => _requests.ToArray();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        _requests.Enqueue(new CapturedRequest(request.Method, request.RequestUri, request.Headers.ToDictionary(x => x.Key, x => x.Value.ToArray()), body));
        return await responder(request, cancellationToken);
    }
}

internal sealed record CapturedRequest(
    HttpMethod Method,
    Uri? Uri,
    IReadOnlyDictionary<string, string[]> Headers,
    string? Body);

internal static class StubResponses
{
    public static HttpResponseMessage Json(HttpStatusCode statusCode, string body)
        => new(statusCode) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Ok(string body)
        => Json(HttpStatusCode.OK, body);
}

internal sealed class RecordingHandler : DelegatingHandler
{
    private readonly ConcurrentQueue<string> _requests = new();

    public IReadOnlyList<string> Requests => _requests.ToArray();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _requests.Enqueue($"{request.Method} {request.RequestUri}");
        return base.SendAsync(request, cancellationToken);
    }
}

internal sealed class StubCdnPrimaryHandler : HttpMessageHandler
{
    private readonly ConcurrentQueue<HttpRequestMessage> _requests = new();

    public IReadOnlyList<HttpRequestMessage> Requests => _requests.ToArray();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _requests.Enqueue(request);
        return Task.FromResult(StubResponses.Ok("""
        {
          "success": true,
          "data": [
            { "name": "documents", "visibility": "Private", "fileCount": 1, "totalSizeBytes": 100 }
          ]
        }
        """));
    }
}

internal sealed class StubPrimaryHandlerFilter(StubCdnPrimaryHandler handler) : IHttpMessageHandlerBuilderFilter
{
    public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next)
        => builder =>
        {
            next(builder);
            builder.PrimaryHandler = handler;
        };
}