<p align="center">
  <img src="https://raw.githubusercontent.com/alisaivi786/NinjaVault/main/assets/icon-512.png" width="112" alt="NinjaVault" />
</p>

<h1 align="center">NinjaVault.Http</h1>

<p align="center">
  Typed <code>HttpClient</code> registration for third-party APIs with <b>automatic request/response logging</b>,
  a pluggable log sink, and <b>correlation-id propagation</b>.
</p>

<p align="center">
  <a href="https://www.nuget.org/packages/NinjaVault.Http"><img src="https://img.shields.io/nuget/v/NinjaVault.Http.svg?label=NinjaVault.Http" alt="NuGet version" /></a>
  <a href="https://www.nuget.org/packages/NinjaVault.Http"><img src="https://img.shields.io/nuget/dt/NinjaVault.Http.svg" alt="NuGet downloads" /></a>
  <a href="https://github.com/alisaivi786/NinjaVault/actions/workflows/ci.yml"><img src="https://github.com/alisaivi786/NinjaVault/actions/workflows/ci.yml/badge.svg" alt="CI" /></a>
  <img src="https://img.shields.io/badge/.NET-8%20%7C%209%20%7C%2010-512BD4" alt=".NET 8, 9, 10" />
  <a href="https://github.com/alisaivi786/NinjaVault/blob/main/LICENSE"><img src="https://img.shields.io/badge/license-MIT-green.svg" alt="MIT" /></a>
</p>

---

## Why NinjaVault.Http

- **See every outbound call.** Method, URL, status, elapsed time, headers and bodies for every request your app makes to an external API.
- **Safe by default.** Secret headers are redacted. Binary, multipart and oversized bodies are recorded as a small placeholder and never decoded or buffered.
- **Store logs anywhere.** By default each call is one `ILogger` entry, so it flows into Serilog, the console, Seq, Application Insights or OpenSearch with no extra setup. Implement one interface (`IExternalApiCallLogSink`) to keep full records in SQL or a queue.
- **One correlation id end to end.** An `AsyncLocal` accessor stamps the same `X-Correlation-Id` on every outgoing call made while handling one incoming request.
- **Never breaks your call.** A failing sink is caught and logged as a warning; the real HTTP call always completes.
- **Standard building blocks.** A plain `DelegatingHandler` on `IHttpClientFactory`, so it composes with resilience handlers and anything else.

> Works with any typed or named `HttpClient`, including [NinjaVault.Cdn](https://www.nuget.org/packages/NinjaVault.Cdn):
> `services.AddNinjaVaultCdn(configuration).AddNinjaVaultHttpLogging("NinjaVaultCdn");`

---

## Quick start

**1. Install**

```bash
dotnet add package NinjaVault.Http
```

**2. Register shared infrastructure, then each API client**

```csharp
builder.Services.AddNinjaVaultHttp(builder.Configuration);

builder.Services.AddNinjaVaultHttpClient<IPaymentsApi, PaymentsApi>("PaymentsApi", (_, client) =>
{
    client.BaseAddress = new Uri("https://payments.example.com/");
});
```

**3. Write the typed client as usual**

```csharp
public sealed class PaymentsApi(HttpClient http) : IPaymentsApi
{
    public Task<HttpResponseMessage> ChargeAsync(ChargeRequest request, CancellationToken ct)
        => http.PostAsJsonAsync("charges", request, ct);
}
```

Every call made by `PaymentsApi` is now logged and carries an `X-Correlation-Id` header.

### Add logging to a client registered elsewhere

Already have an `IHttpClientBuilder` (from `AddHttpClient`, an SDK, or `AddNinjaVaultCdn`)? Attach logging with one line:

```csharp
builder.Services.AddNinjaVaultHttp(builder.Configuration);   // binds options (optional; defaults otherwise)

builder.Services.AddHttpClient<IWeatherApi, WeatherApi>()
    .AddNinjaVaultHttpLogging("WeatherApi");

builder.Services.AddNinjaVaultCdn(builder.Configuration)
    .AddNinjaVaultHttpLogging("NinjaVaultCdn");
```

---

## Configuration reference

```json
{
  "NinjaVault": {
    "ExternalApiLogging": {
      "Enabled": true,
      "CaptureRequestBody": true,
      "CaptureResponseBody": true,
      "MaxBodyLength": 8192,
      "SensitiveHeaders": [ "X-Tenant-Secret" ]
    }
  }
}
```

| Key | Default | Description |
|---|---|---|
| `Enabled` | `true` | Master switch. `false` means no capture and no correlation header. |
| `CaptureRequestBody` | `true` | Record text request bodies (JSON, XML, form). |
| `CaptureResponseBody` | `true` | Record text response bodies. |
| `MaxBodyLength` | `8192` | Characters kept per body; longer bodies are truncated. |
| `SensitiveHeaders` | built-in list | **Adds** header names to redact. Always redacted: `Authorization`, `Cookie`, `Set-Cookie`, `X-Api-Key`, `X-Access-Token`. |

Every key is optional. With no section at all you get the defaults above. Options are read through `IOptionsMonitor`, so changes made while the app is running apply to the next call.

Configure in code instead:

```csharp
builder.Services.AddNinjaVaultHttp();
builder.Services.Configure<ExternalApiLoggingOptions>(o =>
{
    o.CaptureResponseBody = false;
    o.SensitiveHeaders.Add("X-Tenant-Secret");
});
```

### What is never captured as text

| Body | Recorded as |
|---|---|
| `multipart/*`, `image/*`, `audio/*`, `video/*`, `application/octet-stream`, `application/pdf`, `application/zip` | `{"omitted":true,"reason":"binary or multipart body - not logged","contentType":"...","sizeBytes":...}` |
| Response with `Content-Length` over 1 MB | `{"omitted":true,"reason":"body larger than 1 MB - not buffered or logged",...}` |

Neither is read or buffered by the handler, so large downloads still stream straight to your code.

---

## Where the logs go

| Your setup | Result |
|---|---|
| Nothing extra (default) | One `Information` entry per call from `LoggingExternalApiCallLogSink` (category `NinjaVault.Http.Logging.LoggingExternalApiCallLogSink`, event id 3001) with service, URL, status, elapsed ms, correlation id and error. It goes wherever your `ILogger` goes: console, Serilog sinks, Seq, Application Insights, OpenSearch. |
| Your own `IExternalApiCallLogSink` | Your sink receives the **full** record (headers and bodies) and decides where it is stored. The default `ILogger` entry is replaced. |

If you filter logs by category or level (for example with Serilog `MinimumLevel.Override`), keep `NinjaVault.Http` at `Information` or lower, or the default entries are dropped.

### Persist logs with a custom sink

```csharp
public sealed class DbApiLogSink(AppDbContext db) : IExternalApiCallLogSink
{
    public async Task WriteAsync(ExternalApiCallLog log, CancellationToken cancellationToken)
    {
        db.ApiLogs.Add(new ApiLogRow
        {
            ServiceName = log.ServiceName,         // "PaymentsApi", "NinjaVaultCdn", ...
            EndpointId = log.EndpointId,
            CorrelationId = log.CorrelationId,
            TraceId = log.TraceId,
            Method = log.Method,
            RequestUrl = log.RequestUrl,
            RequestHeaders = log.RequestHeaders,   // JSON, secrets already [REDACTED]
            RequestBody = log.RequestBody,
            StatusCode = log.StatusCode,           // 0 = failed before a response (DNS, timeout...)
            ResponseHeaders = log.ResponseHeaders,
            ResponseBody = log.ResponseBody,
            ElapsedMs = log.ElapsedMs,
            Error = log.Error,
            StartedOnUtc = log.StartedOnUtc,
            CompletedOnUtc = log.CompletedOnUtc
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}

builder.Services.AddNinjaVaultHttp(builder.Configuration);
builder.Services.AddScoped<IExternalApiCallLogSink, DbApiLogSink>(); // register after AddNinjaVaultHttp
```

Each write runs in its own DI scope, so scoped services such as a `DbContext` are safe to use.

| `ExternalApiCallLog` field | Content |
|---|---|
| `ServiceName` | Name passed to `AddNinjaVaultHttpClient` |
| `EndpointId` | Optional id set with `request.WithEndpointId(...)` |
| `CorrelationId` / `TraceId` | From the ambient `CorrelationContext` |
| `Method`, `RequestUrl`, `StatusCode`, `ElapsedMs` | The call itself |
| `RequestHeaders` / `ResponseHeaders` | JSON object, sensitive values redacted |
| `RequestBody` / `ResponseBody` | Truncated text, or the omitted placeholder |
| `Error` | Exception message when no response was received |
| `StartedOnUtc` / `CompletedOnUtc` | Timestamps |

---

## Correlation across services

Set the context once per incoming request, and every outgoing call reuses it:

```csharp
app.Use(async (context, next) =>
{
    ICorrelationContextAccessor accessor = context.RequestServices.GetRequiredService<ICorrelationContextAccessor>();
    string correlationId = context.Request.Headers[CorrelationConstants.HeaderName].FirstOrDefault()
        ?? Guid.NewGuid().ToString("N");

    accessor.Current = new CorrelationContext(correlationId, context.TraceIdentifier);
    context.Response.Headers[CorrelationConstants.HeaderName] = correlationId;

    await next();
});
```

If no context is set (in background jobs, for example), a new id is generated per logical operation.

## Tag calls per endpoint (optional)

```csharp
using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "charges")
    .WithEndpointId((int)PaymentsEndpoint.CreateCharge);

// the log's EndpointId is set, so you can group, count and alert per endpoint
```

---

## Troubleshooting

| Symptom | Fix |
|---|---|
| No log entries at all | `Enabled` is `false`, the client has no `AddNinjaVaultHttpLogging(...)` / `AddNinjaVaultHttpClient` registration, or a log filter drops `NinjaVault.Http` at `Information`. |
| Only one line per call, no bodies | That's the default `ILogger` sink. Register an `IExternalApiCallLogSink` to keep the full record. |
| My sink isn't used | Register it **after** `AddNinjaVaultHttp` (the default is added with `TryAdd`). |
| `"omitted": true` in the body | Binary, multipart or >1 MB body, by design. |
| A secret header shows in logs | Add it to `SensitiveHeaders`. |

## Compatibility

| | |
|---|---|
| Target frameworks | `net8.0`, `net9.0`, `net10.0` |
| Dependencies | `Microsoft.Extensions.Http`, `Options`, `Logging.Abstractions` 8.0+ |
| Source Link / symbols | ✅ |

## NinjaVault SDKs

The same CDN client in every language, with the same features and the **same version number** (for example `100.42.1` everywhere):

| Language | Package | Install | Source |
|---|---|---|---|
| .NET 8+ | [`NinjaVault.Cdn`](https://www.nuget.org/packages/NinjaVault.Cdn) | `dotnet add package NinjaVault.Cdn` | [NinjaVault](https://github.com/alisaivi786/NinjaVault) |
| Python 3.10+ | [`ninjavault-cdn`](https://pypi.org/project/ninjavault-cdn/) | `pip install ninjavault-cdn` | [NinjaVault-Python](https://github.com/alisaivi786/NinjaVault-Python) |
| Node.js 18+ | [`@ninjavault/cdn`](https://www.npmjs.com/package/@ninjavault/cdn) | `npm install @ninjavault/cdn` | [NinjaVault-Node](https://github.com/alisaivi786/NinjaVault-Node) |

---

## Links

- Source, issues and release notes: <https://github.com/alisaivi786/NinjaVault>
- Change-sets: <https://github.com/alisaivi786/NinjaVault/tree/main/changesets/NinjaVault.Http>
- License: MIT
