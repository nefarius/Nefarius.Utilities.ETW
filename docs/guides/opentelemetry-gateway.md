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
| `Export:TracesEndpoint` | OTLP/HTTP base URL of a trace backend such as Jaeger; each decoded event is also exported as a span (one trace per tenant and relay session). The log and trace receivers get the same headers |
| `Export:Headers`, `QueueCapacity`, `MaxAttempts`, `InitialRetryDelay`, `MaxRetryDelay`, `RequestTimeout` | delivery, bounded queue and retry |
| `Wpp:PdbFiles`, `Wpp:PdbDirectories`, `Wpp:TmfDirectories`, `Wpp:RewriteProviderName` | server-only WPP assets |
| `Wpp:SymbolServer`, `Wpp:SymbolCache`, `Wpp:SymbolRequestTimeout`, `Wpp:SymbolWaitTimeout`, `Wpp:SymbolMaxQueuedRequests`, `Wpp:SymbolMaxAttempts` | optional symbol server (absolute http(s) URL), cache directory and per-request timeout for [symbol discovery](#symbol-server-discovery) |
| `Limits:*` | body size, frame size, session counts (total and per tenant), records per request, idle timeout |
| `RequireAuthorization` | require an authenticated user; you register the authentication scheme |

It listens for OTLP/HTTP (`POST /v1/logs`, protobuf, optional gzip) on port 4318 and OTLP/gRPC on 4317, serves `/healthz`, and publishes counters under the meter `Nefarius.Utilities.ETW.OtlpGateway`.

If the downstream receiver is slow or down, the gateway buffers decoded batches in a bounded queue and retries. When the queue is full it answers **before consuming any frame** with HTTP 503 and `Retry-After` (gRPC `UNAVAILABLE`), so the sender can resend the identical request; frames that were accepted are never lost to a refusal. A batch that still fails after `MaxAttempts` is dropped and counted. A rejected frame is reported through the standard OTLP `partial_success` response; a continuation of a session the gateway does not know (expired or restarted) is rejected so the client opens a new session with a hello frame.

A session with no request for `Limits:SessionIdleTimeout` (default 15 minutes) is discarded on the same path. `etwrelay` keeps an open capture from reaching that timeout by replaying its last acknowledged frames after `--heartbeat-seconds` (default 5 minutes). `--heartbeat-seconds 0` disables heartbeats. Set the interval below the configured `Limits:SessionIdleTimeout`: an interval equal to or greater than that timeout can let the session expire before the first heartbeat. Replay starts only after an event frame has been acknowledged. A capture that has sent only its hello receives no heartbeat and expires after `Limits:SessionIdleTimeout`. The replay leaves out a leading hello, so a restarted gateway cannot treat the heartbeat as a new session and export those events again; a live session still sees the remaining frames as duplicates and does not decode or export them a second time. The heartbeat does not recreate a session that has already expired or was lost when the gateway restarted.

## Symbol server discovery

Set `Wpp:SymbolServer` (for example `https://msdl.microsoft.com/download/symbols` or your own SymSrv) and the gateway downloads the PDBs of the traced binaries on demand, caching them in `Wpp:SymbolCache` (default: a folder below the temp directory; use a persistent volume to keep it across restarts).

How it works:

* The client relays raw events only. The gateway looks for the image debug-ID events in the relayed stream (`KernelTraceControl/ImageID/DbgID_RSDS`, and the legacy `MSNT_SystemTrace/EventTrace/DbgIdRSDS`), takes the PDB file name, GUID and age from them, resolves the identity through the cache and then the symbol server, validates the downloaded PDB's GUID and age, and adds it to the shared WPP decoding context.
* Identities in a request are resolved **before that request is decoded**, so a capture that sends its image events first (as ETW rundowns do) gets its WPP messages formatted. Downloads are deduplicated, including concurrent ones.
* **Requests wait in memory for symbols that are still resolving.** A request is held (up to `Wpp:SymbolWaitTimeout`, default 10 s, and for at most `Wpp:SymbolMaxQueuedRequests`, default 32, requests at once) until every download in flight has finished, including downloads started by other sessions. Nothing is dropped: after the timeout, or when the queue is full, the request is decoded right away with placeholders while the download continues in the background.
* **Failures:** not found, access denied and a mismatching PDB are permanent and never retried. Timeouts, network and server errors are retried `Wpp:SymbolMaxAttempts` times (default 3) with a short backoff, then at most once a minute.
* **Discovery is best effort.** Events that are decoded before their symbols were loaded, for instance because the image events come later in the stream, the wait timed out or the download failed, keep the `GUID=...` placeholder; nothing is buffered or replayed. Later requests use whatever has loaded by then.
* A missing, unreachable, mismatching or malformed PDB only produces a warning (event payloads are never logged); it never fails the request.

Security boundaries: the server URL is operator configuration only. Relay data contributes just a PDB leaf file name, GUID and age, never a URL, a host or a path, and names with path, URL or control characters are ignored. Only the configured server is contacted, redirects are not followed, requests time out, the number of identities per request and in total is bounded, and a download is only accepted when its embedded GUID and age match. A symbol server URL that is not an absolute http(s) URL, or a cache directory that is not writable, stops the gateway at startup. Authenticated tenants share the cache and the loaded symbols; run separate gateways if tenants must not see each other's formats.

## Testing the gateway from the command line

Two small tools make a capture-to-gateway round trip possible without writing a client. `etwutils` captures and writes raw frames; the optional `etwrelay` tool wraps them in OTLP Logs and posts them to the gateway's `/v1/logs`:

```text
etwutils realtime --format relay <provider-guid> | etwrelay --endpoint http://localhost:4318 --resource service.name=my-host
```

`etwrelay` batches at most 64 frames per request (the gateway's duplicate-retry window), keeps sessions apart, and resends a failed request with identical bytes, so a retry after a `503` and `Retry-After` is recognised as a duplicate. The same duplicate recognition is what makes its idle heartbeat safe. Connection failures, timeouts, `408`, `429` and `5xx` are retried; other `4xx` responses and rejected records (for example a session the gateway no longer knows) are final. Credential-like headers are only accepted with `https://`.

The binary pipe needs PowerShell 7.4+, `cmd.exe` or a Unix shell. In PowerShell 7.0–7.3 and Windows PowerShell 5.1, use `etwutils realtime --format relay-ndjson ... | etwrelay --input-format base64-ndjson`. Details: [etwrelay](../../tools/Nefarius.Utilities.ETW.Relay.CLI/README.md).

## Embedding in your own host

Authentication stays yours. Register your scheme, then:

```csharp
builder.Services.AddEtwOtlpGateway(builder.Configuration);
// ...
app.MapEtwOtlpGateway().RequireAuthorization("etw-ingest");   // applies to HTTP and gRPC
```

Sessions are isolated per tenant. The default `IGatewayTenantResolver` uses the authenticated user's name or `sub` claim; replace it to derive the tenant from your own identity model. Replace `IDecodedLogSink` to deliver decoded records somewhere other than OTLP/HTTP.


See also: [Raw ETW relay protocol](raw-etw-relay.md).

See [examples/jaeger](../../examples/jaeger) for a Docker Compose stack with a sample sender and the gateway. Decoded events go to the Aspire Dashboard as native OTLP Logs, and the same events are projected as spans for Jaeger.
