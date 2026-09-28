using NinjaVault.Http.Correlation;
using NinjaVault.Http.Logging;

namespace NinjaVault.Http.Tests;

public sealed class CorrelationTests
{
    [Fact]
    public async Task AmbientCorrelationId_IsStampedOnRequest_AndLogged()
    {
        StubHttpMessageHandler stub = new(_ => StubResponses.Ok("ok"));
        LogStore store = new();
        using HttpTestHostContext host = HttpTestHost.Build(stub, store);
        host.Correlation.Current = new CorrelationContext("corr-123", "trace-9");

        await host.Client.PostAsync("a", "{}", TestContext.Current.CancellationToken);

        Assert.Equal("corr-123", Assert.Single(stub.Requests).CorrelationId);
        ExternalApiCallLog log = Assert.Single(store.Logs);
        Assert.Equal("corr-123", log.CorrelationId);
        Assert.Equal("trace-9", log.TraceId);
    }

    [Fact]
    public async Task NoAmbientContext_GeneratesCorrelationId_OnRequestAndLog()
    {
        StubHttpMessageHandler stub = new(_ => StubResponses.Ok("ok"));
        LogStore store = new();
        using HttpTestHostContext host = HttpTestHost.Build(stub, store);
        host.Correlation.Current = null;

        await host.Client.PostAsync("a", "{}", TestContext.Current.CancellationToken);

        string? headerId = Assert.Single(stub.Requests).CorrelationId;
        Assert.False(string.IsNullOrWhiteSpace(headerId));
        Assert.Equal(headerId, Assert.Single(store.Logs).CorrelationId);
    }

    [Fact]
    public async Task ConcurrentCalls_ShareTheSameCorrelationId()
    {
        StubHttpMessageHandler stub = new(_ => StubResponses.Ok("ok"));
        LogStore store = new();
        using HttpTestHostContext host = HttpTestHost.Build(stub, store);
        host.Correlation.Current = new CorrelationContext("shared-corr", "trace-x");

        CancellationToken token = TestContext.Current.CancellationToken;
        await Task.WhenAll(Enumerable.Range(0, 8).Select(i => host.Client.PostAsync($"path/{i}", "{}", token)));

        Assert.Equal(8, stub.Requests.Count);
        Assert.All(stub.Requests, request => Assert.Equal("shared-corr", request.CorrelationId));
        Assert.Equal(8, store.Logs.Count);
        Assert.All(store.Logs, log => Assert.Equal("shared-corr", log.CorrelationId));
    }

    [Fact]
    public void GetOrCreateCorrelationId_ReturnsExistingId()
    {
        CorrelationContextAccessor accessor = new()
        {
            Current = new CorrelationContext("existing", "trace")
        };

        Assert.Equal("existing", accessor.GetOrCreateCorrelationId());
    }

    [Fact]
    public void GetOrCreateCorrelationId_GeneratesAndStoresWhenEmpty()
    {
        CorrelationContextAccessor accessor = new();

        string generated = accessor.GetOrCreateCorrelationId();

        Assert.False(string.IsNullOrWhiteSpace(generated));
        Assert.Equal(generated, accessor.Current?.CorrelationId);
    }

    [Fact]
    public async Task CallerSuppliedCorrelationHeader_IsNotOverwritten()
    {
        StubHttpMessageHandler stub = new(_ => StubResponses.Ok("ok"));
        LogStore store = new();
        using HttpTestHostContext host = HttpTestHost.Build(stub, store);
        host.Correlation.Current = new CorrelationContext("ambient", "trace");

        await host.Client.PostWithHeaderAsync("preset", "{}", CorrelationConstants.HeaderName, "preset-id", TestContext.Current.CancellationToken);

        Assert.Equal("preset-id", Assert.Single(stub.Requests).CorrelationId);
    }
}