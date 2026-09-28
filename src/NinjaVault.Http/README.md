# NinjaVault.Http

```bash
dotnet add package NinjaVault.Http
```

A reusable HTTP client factory for calling third-party APIs, with automatic
request/response logging, a pluggable persistence sink, and correlation-id propagation.

## What it gives you

- **Reusable typed-client factory** — register any third-party client with
  `AddNinjaVaultHttpClient<TClient, TImpl>(serviceName, configure)` and it is wired for logging
  and correlation automatically.
- **External request/response logging** — an `ExternalApiLoggingHandler` (a `DelegatingHandler`)
  captures method, URL, headers (sensitive values redacted), bodies (truncated), status code and
  elapsed time for every call.
- **Pluggable persistence** — logs are handed to an `IExternalApiCallLogSink`. A structured-logging
  sink is registered by default; register your own to persist to a database/audit table.
- **Correlation** — an `AsyncLocal`-backed `ICorrelationContextAccessor` shares one correlation id
  across all concurrent outbound calls of a single inbound request. The id is stamped on every
  outgoing request via the `X-Correlation-Id` header.

## Usage

```csharp
// startup
builder.Services.AddNinjaVaultHttp(builder.Configuration);
builder.Services.AddScoped<IExternalApiCallLogSink, MyDbSink>(); // optional: persist to a table

builder.Services.AddNinjaVaultHttpClient<IMyApiClient, MyApiClient>("MyApi", (sp, client) =>
{
    client.BaseAddress = new Uri("https://api.example.com/");
});
```

Set the correlation id per inbound request (e.g. in ASP.NET middleware):

```csharp
ICorrelationContextAccessor accessor = context.RequestServices.GetRequiredService<ICorrelationContextAccessor>();
string correlationId = context.Request.Headers[CorrelationConstants.HeaderName].FirstOrDefault()
    ?? Guid.NewGuid().ToString("N");
accessor.Current = new CorrelationContext(correlationId, context.TraceIdentifier);
```

## Configuration

```json
{
  "NinjaVault": {
    "ExternalApiLogging": {
      "Enabled": true,
      "CaptureRequestBody": true,
      "CaptureResponseBody": true,
      "MaxBodyLength": 8192
    }
  }
}
```

`Authorization`, `Cookie`, `Set-Cookie`, `X-Api-Key` and `X-Access-Token` header values are always
redacted. Binary and multipart bodies are recorded as a small JSON placeholder (content type + size),
never as raw bytes.
