# OpenTelemetry integration and decoding gateway

OpenTelemetry is not required by the core package. [`integrations/Nefarius.Utilities.ETW.OpenTelemetry`](../../integrations/Nefarius.Utilities.ETW.OpenTelemetry) adds an optional adapter, and [`tools/Nefarius.Utilities.ETW.OtlpGateway`](../../tools/Nefarius.Utilities.ETW.OtlpGateway) is a ready-to-run decoding gateway built on it.

**Transport.** A client wraps each relay frame in an OTLP `LogRecord` whose body is the frame as `bytes_value`, grouped under a resource carrying the attribute `etw.relay.session_id` (the session GUID), and sends it with any OTLP/HTTP or OTLP/gRPC exporter. `RelayLogRecords.CreateRequest` does this on .NET.

**Decoded output.** The gateway answers with standard OTLP Logs for any storage backend: the event name becomes `event_name`, TraceLogging and manifest properties become a structured `kvlist` body, WPP events carry the formatted message as the string body (`etw.wpp.*` attributes hold the rest), ETW level maps to severity, and the header fields become `etw.*` attributes (`etw.provider.id`, `etw.event.id`, `etw.process.id`, …).

```text
capture client -- OTLP (frames) --> [ Collector / proxy ] --> decoding gateway --> OTLP Logs --> Collector --> backend
   no symbols                         auth, TLS, routing      PDB/TMF live here
```

**Collector constraints.** Any Collector between the client and the gateway must forward the bodies unchanged and keep the order of records within a session. Do not substitute the Collector's Windows Event Log receiver or any receiver that decodes events on the collecting host: those need the provider's metadata on that host, which is exactly what this design avoids.

## Running the gateway

```text
dotnet run --project tools/Nefarius.Utilities.ETW.OtlpGateway
```

Configure it through the `Gateway` section of `appsettings.json` or environment variables (`Gateway__Export__Endpoint=http://collector:4318`):

| Key | Purpose |
|-----|---------|
| `Export:Endpoint` | downstream OTLP/HTTP base URL for decoded logs |
| `Export:TracesEndpoint` | OTLP/HTTP base URL of a trace backend such as Jaeger; each decoded event is also exported as a span (one trace per relay session) |
| `Export:Headers`, `QueueCapacity`, `MaxAttempts`, `InitialRetryDelay`, `MaxRetryDelay`, `RequestTimeout` | delivery, bounded queue and retry |
| `Wpp:PdbFiles`, `Wpp:PdbDirectories`, `Wpp:TmfDirectories`, `Wpp:RewriteProviderName` | server-only WPP assets |
| `Limits:*` | body size, frame size, session counts (total and per tenant), records per request, idle timeout |
| `RequireAuthorization` | require an authenticated user; you register the authentication scheme |

It listens for OTLP/HTTP (`POST /v1/logs`, protobuf, optional gzip) on port 4318 and OTLP/gRPC on 4317, serves `/healthz`, and publishes counters under the meter `Nefarius.Utilities.ETW.OtlpGateway`.

If the downstream receiver is slow or down, the gateway buffers decoded batches in a bounded queue and retries. When the queue is full it answers **before consuming any frame** with HTTP 503 and `Retry-After` (gRPC `UNAVAILABLE`), so the sender can resend the identical request; frames that were accepted are never lost to a refusal. A batch that still fails after `MaxAttempts` is dropped and counted. A rejected frame is reported through the standard OTLP `partial_success` response; a continuation of a session the gateway does not know (expired or restarted) is rejected so the client opens a new session with a hello frame.

## Embedding in your own host

Authentication stays yours. Register your scheme, then:

```csharp
builder.Services.AddEtwOtlpGateway(builder.Configuration);
// ...
app.MapEtwOtlpGateway().RequireAuthorization("etw-ingest");   // applies to HTTP and gRPC
```

Sessions are isolated per tenant. The default `IGatewayTenantResolver` uses the authenticated user's name or `sub` claim; replace it to derive the tenant from your own identity model. Replace `IDecodedLogSink` to deliver decoded records somewhere other than OTLP/HTTP.


See also: [Raw ETW relay protocol](raw-etw-relay.md).

See [examples/jaeger](../../examples/jaeger) for a Docker Compose stack with a sample sender, the gateway and Jaeger.
