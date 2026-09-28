using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NinjaVault.Http.Logging;

namespace NinjaVault.Http.Tests;

public sealed class ExternalApiLoggingHandlerTests
{
    [Fact]
    public async Task LogsSuccessfulCall_CapturesMethodUrlStatusAndBodies()
    {
        StubHttpMessageHandler stub = new(_ => StubResponses.Ok("{\"pong\":true}"));
        LogStore store = new();
        using HttpTestHostContext host = HttpTestHost.Build(stub, store);

        string result = await host.Client.PostAsync("api/v1/ping", "{\"ping\":1}", TestContext.Current.CancellationToken);

        Assert.Equal("{\"pong\":true}", result);
        ExternalApiCallLog log = Assert.Single(store.Logs);
        Assert.Equal("TestApi", log.ServiceName);
        Assert.Equal("POST", log.Method);
        Assert.Equal("https://unit.test/api/v1/ping", log.RequestUrl);
        Assert.Equal(200, log.StatusCode);
        Assert.Equal("{\"ping\":1}", log.RequestBody);
        Assert.Equal("{\"pong\":true}", log.ResponseBody);
        Assert.True(log.CompletedOnUtc >= log.StartedOnUtc);
    }

    [Fact]
    public async Task RedactsSensitiveHeaders()
    {
        StubHttpMessageHandler stub = new(_ => StubResponses.Ok("ok"));
        LogStore store = new();
        using HttpTestHostContext host = HttpTestHost.Build(stub, store);

        await host.Client.PostWithAuthAsync("secure", "{}", "super-secret-token", TestContext.Current.CancellationToken);

        ExternalApiCallLog log = Assert.Single(store.Logs);
        Assert.NotNull(log.RequestHeaders);
        Assert.Contains("[REDACTED]", log.RequestHeaders, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret-token", log.RequestHeaders, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TruncatesBodies_ToMaxBodyLength()
    {
        StubHttpMessageHandler stub = new(_ => StubResponses.Ok(new string('r', 500)));
        LogStore store = new();
        using HttpTestHostContext host = HttpTestHost.Build(stub, store, options => options.MaxBodyLength = 10);

        await host.Client.PostAsync("big", new string('q', 500), TestContext.Current.CancellationToken);

        ExternalApiCallLog log = Assert.Single(store.Logs);
        Assert.Equal(10, log.RequestBody!.Length);
        Assert.Equal(10, log.ResponseBody!.Length);
    }

    [Fact]
    public async Task ResponseRemainsReadableByCaller_AfterHandlerCapturesIt()
    {
        StubHttpMessageHandler stub = new(_ => StubResponses.Ok("caller-can-read-this"));
        LogStore store = new();
        using HttpTestHostContext host = HttpTestHost.Build(stub, store);

        string result = await host.Client.PostAsync("read", "{}", TestContext.Current.CancellationToken);

        Assert.Equal("caller-can-read-this", result);
        Assert.Equal("caller-can-read-this", Assert.Single(store.Logs).ResponseBody);
    }

    [Fact]
    public async Task DisabledLogging_WritesNothing_AndDoesNotAddCorrelationHeader()
    {
        StubHttpMessageHandler stub = new(_ => StubResponses.Ok("ok"));
        LogStore store = new();
        using HttpTestHostContext host = HttpTestHost.Build(stub, store, options => options.Enabled = false);

        await host.Client.PostAsync("noop", "{}", TestContext.Current.CancellationToken);

        Assert.Empty(store.Logs);
        Assert.Null(Assert.Single(stub.Requests).CorrelationId);
    }

    [Fact]
    public async Task CaptureResponseBodyFalse_OmitsResponseBody_ButStillLogs()
    {
        StubHttpMessageHandler stub = new(_ => StubResponses.Ok("hidden"));
        LogStore store = new();
        using HttpTestHostContext host = HttpTestHost.Build(stub, store, options => options.CaptureResponseBody = false);

        await host.Client.PostAsync("x", "{}", TestContext.Current.CancellationToken);

        ExternalApiCallLog log = Assert.Single(store.Logs);
        Assert.Equal(string.Empty, log.ResponseBody);
        Assert.Equal(200, log.StatusCode);
    }

    [Fact]
    public async Task SinkFailure_DoesNotBreakTheCall()
    {
        StubHttpMessageHandler stub = new(_ => StubResponses.Ok("still-works"));
        LogStore store = new();
        using HttpTestHostContext host = HttpTestHost.Build(stub, store, useThrowingSink: true);

        string result = await host.Client.PostAsync("resilient", "{}", TestContext.Current.CancellationToken);

        Assert.Equal("still-works", result);
    }

    [Fact]
    public async Task FailedCall_LogsStatusZeroAndError_AndRethrows()
    {
        StubHttpMessageHandler stub = new(_ => throw new HttpRequestException("connection refused"));
        LogStore store = new();
        using HttpTestHostContext host = HttpTestHost.Build(stub, store);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => host.Client.PostAsync("dead", "{}", TestContext.Current.CancellationToken));

        ExternalApiCallLog log = Assert.Single(store.Logs);
        Assert.Equal(0, log.StatusCode);
        Assert.Equal("connection refused", log.Error);
    }

    [Fact]
    public async Task ScopedSink_IsResolvedPerCall()
    {
        StubHttpMessageHandler stub = new(_ => StubResponses.Ok("ok"));
        LogStore store = new();
        using HttpTestHostContext host = HttpTestHost.Build(stub, store, sinkLifetime: ServiceLifetime.Scoped);

        await host.Client.PostAsync("a", "{}", TestContext.Current.CancellationToken);
        await host.Client.PostAsync("b", "{}", TestContext.Current.CancellationToken);

        Assert.Equal(2, store.Logs.Count);
    }

    [Fact]
    public async Task EndpointIdTag_IsRecordedOnTheLog()
    {
        StubHttpMessageHandler stub = new(_ => StubResponses.Ok("ok"));
        LogStore store = new();
        using HttpTestHostContext host = HttpTestHost.Build(stub, store);

        await host.Client.PostWithEndpointIdAsync("op", "{}", 7, TestContext.Current.CancellationToken);

        Assert.Equal(7, Assert.Single(store.Logs).EndpointId);
    }

    [Fact]
    public async Task WithoutEndpointIdTag_EndpointIdIsNull()
    {
        StubHttpMessageHandler stub = new(_ => StubResponses.Ok("ok"));
        LogStore store = new();
        using HttpTestHostContext host = HttpTestHost.Build(stub, store);

        await host.Client.PostAsync("op", "{}", TestContext.Current.CancellationToken);

        Assert.Null(Assert.Single(store.Logs).EndpointId);
    }

    [Fact]
    public async Task MultipartRequestBody_IsNotCapturedAsText()
    {
        StubHttpMessageHandler stub = new(_ => StubResponses.Ok("{\"ok\":true}"));
        LogStore store = new();
        using HttpTestHostContext host = HttpTestHost.Build(stub, store);

        // Bytes chosen so a naive UTF-8 decode of the raw multipart body would be ill-formed.
        byte[] fileBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46];
        await host.Client.PostMultipartFileAsync("upload", fileBytes, "photo.jpg", TestContext.Current.CancellationToken);

        ExternalApiCallLog log = Assert.Single(store.Logs);
        using JsonDocument requestBody = JsonDocument.Parse(log.RequestBody!);
        Assert.True(requestBody.RootElement.GetProperty("omitted").GetBoolean());
        Assert.StartsWith("multipart/form-data", requestBody.RootElement.GetProperty("contentType").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BinaryResponseBody_IsNotCapturedAsText()
    {
        byte[] fileBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46];
        StubHttpMessageHandler stub = new(_ => StubResponses.OkBinary(fileBytes, "image/jpeg"));
        LogStore store = new();
        using HttpTestHostContext host = HttpTestHost.Build(stub, store);

        byte[] result = await host.Client.PostMultipartFileAsync("download", fileBytes, "photo.jpg", TestContext.Current.CancellationToken);

        Assert.Equal(fileBytes, result);
        ExternalApiCallLog log = Assert.Single(store.Logs);
        using JsonDocument responseBody = JsonDocument.Parse(log.ResponseBody!);
        Assert.True(responseBody.RootElement.GetProperty("omitted").GetBoolean());
        Assert.Equal("image/jpeg", responseBody.RootElement.GetProperty("contentType").GetString());
        Assert.Equal(fileBytes.Length, responseBody.RootElement.GetProperty("sizeBytes").GetInt64());
    }

    [Fact]
    public async Task NonSuccessStatus_IsLoggedWithStatusCode()
    {
        StubHttpMessageHandler stub = new(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("{\"error\":\"bad\"}")
        });
        LogStore store = new();
        using HttpTestHostContext host = HttpTestHost.Build(stub, store);

        await host.Client.PostAsync("bad", "{}", TestContext.Current.CancellationToken);

        ExternalApiCallLog log = Assert.Single(store.Logs);
        Assert.Equal(400, log.StatusCode);
        Assert.Equal("{\"error\":\"bad\"}", log.ResponseBody);
    }

    [Fact]
    public async Task BinaryResponseBody_IsNotBufferedByTheLoggingHandler()
    {
        // The content refuses to be read until the log has been written, so any buffering inside the
        // handler (which happens before the sink runs) would fail the call.
        byte[] fileBytes = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37];
        LogStore store = new();
        StubHttpMessageHandler stub = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ReadAfterLogContent(fileBytes, "application/pdf", store)
        });
        using HttpTestHostContext host = HttpTestHost.Build(stub, store);

        byte[] result = await host.Client.PostMultipartFileAsync("download", fileBytes, "doc.pdf", TestContext.Current.CancellationToken);

        Assert.Equal(fileBytes, result);
        ExternalApiCallLog log = Assert.Single(store.Logs);
        using JsonDocument responseBody = JsonDocument.Parse(log.ResponseBody!);
        Assert.True(responseBody.RootElement.GetProperty("omitted").GetBoolean());
        Assert.Equal("application/pdf", responseBody.RootElement.GetProperty("contentType").GetString());
    }

    [Fact]
    public async Task LargeTextResponse_IsNotBufferedAndIsRecordedAsPlaceholder()
    {
        string largeJson = "\"" + new string('a', (1024 * 1024) + 16) + "\"";
        StubHttpMessageHandler stub = new(_ => StubResponses.Ok(largeJson));
        LogStore store = new();
        using HttpTestHostContext host = HttpTestHost.Build(stub, store);

        string result = await host.Client.PostAsync("big", "{}", TestContext.Current.CancellationToken);

        Assert.Equal(largeJson, result);
        ExternalApiCallLog log = Assert.Single(store.Logs);
        using JsonDocument responseBody = JsonDocument.Parse(log.ResponseBody!);
        Assert.True(responseBody.RootElement.GetProperty("omitted").GetBoolean());
        Assert.Contains("larger than 1 MB", responseBody.RootElement.GetProperty("reason").GetString(), StringComparison.Ordinal);
    }
}