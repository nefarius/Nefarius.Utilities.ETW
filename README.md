# <img src="assets/NSS-128x128.png" align="left" />Nefarius.Utilities.ETW

[![.NET](https://github.com/nefarius/Nefarius.Utilities.ETW/actions/workflows/build.yml/badge.svg)](https://github.com/nefarius/Nefarius.Utilities.ETW/actions/workflows/build.yml)
![Requirements](https://img.shields.io/badge/Requires-.NET%208%2F9%2F10-blue.svg)
[![NuGet Version](https://img.shields.io/nuget/v/Nefarius.Utilities.ETW)](https://www.nuget.org/packages/Nefarius.Utilities.ETW/)
[![NuGet](https://img.shields.io/nuget/dt/Nefarius.Utilities.ETW)](https://www.nuget.org/packages/Nefarius.Utilities.ETW/)
[![Assisted by Cursor AI](https://img.shields.io/badge/Assisted%20by-Cursor%20AI-8B5CF6?style=flat)](https://cursor.com/)

[ETW Log Files (.ETL)](https://learn.microsoft.com/en-us/windows-hardware/test/weg/instrumenting-your-code-with-etw) to
JSON parser/converter library.

> [!NOTE]  
> *This is a fork of the fantastic [`ETW2JSON`](https://github.com/microsoft/ETW2JSON) project by Microsoft and
contributors.*

> [!IMPORTANT]  
> While this library is designed to support universal ETW trace sources, the primary personal goal and highest development priority has been making it work reliably with [WPP Software Tracing](https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/wpp-software-tracing) in particular. Other trace source types may work but receive less focused attention and testing.

## Changes of this fork

- Converted console tool into a reusable class library
- Replaced P/Invoke code with [source generators](https://github.com/microsoft/CsWin32)
- Changed namespace to `Nefarius.Utilities.ETW` to avoid conflicts with the origin library
- Added support for [WPP Software Tracing](https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/wpp-software-tracing) decoding
  - Supports `.PDB` files as a decoding source — WPP provider GUIDs (ETW trace control GUIDs) are automatically extracted from `TMC:` annotations in the PDB symbol stream so no manual GUID lookup is required
  - Supports `.TMF` files as a decoding source
  - Support for the [WPP extended format specification strings](https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/what-are-the-wpp-extended-format-specification-strings-)
    (`%!FUNC!`, `%!LEVEL!`, `%!FLAGS!`, `%!IPADDR!`, `%!TIMESTAMP!`, `%!delta!`, `%!due!`, `%!GUID!`,
    `%!CLSID!`/`%!LIBID!`/`%!IID!`, `%!PORT!`, `%!STATUS!`, `%!WINERROR!`, `%!HRESULT!`,
    `%!NDIS_STATUS!`, `%!NDIS_OID!`, `%!sid!`, bitset/list enumerations, and more); known exceptions are
    `%!CLSID!` (no registry name lookup) and `%!LINE!` (rendered empty) (see [Known limitations](#known-limitations))
  - `USEPREFIX`/`USESUFFIX` trace message prefixes are automatically expanded — the `%0` standard-prefix
    sentinel and `%!FUNC!`/`%!LEVEL!` context markers are resolved from the TMF metadata at decode time
- Added `EtwUtil.EnumeratePdbReferences` for lightweight pre-scanning of ETL files to collect all PDB metadata
  (symbol GUIDs, ages and file names) referenced in the trace before performing a full decode — enabling
  proper multi-PDB symbol resolution via symbol servers or local paths
- Added `EtwUtil.EnumerateEventsAsync` — a streaming `IAsyncEnumerable<ReadOnlyMemory<byte>>` API that
  yields each decoded ETW event as a self-contained UTF-8 JSON buffer as it is produced, rather than
  waiting for the full trace to finish; a dedicated background thread runs the blocking `ProcessTrace` call
  and feeds a bounded channel so the consumer is naturally backpressured and can process events concurrently
  with parsing; works well as a data source for real-time delivery scenarios such as
  [FastEndpoints Server Sent Events](https://fast-endpoints.com/docs/server-sent-events):

```csharp
public override async Task HandleAsync(CancellationToken ct)
{
    await Send.EventStreamAsync("etw-event", GetEtwStream(ct), ct);
}

private async IAsyncEnumerable<object> GetEtwStream([EnumeratorCancellation] CancellationToken ct)
{
    await foreach (ReadOnlyMemory<byte> eventJson in EtwUtil.EnumerateEventsAsync(
        [@"C:\traces\capture.etl"],
        opts => opts.WppDecodingContext = myDecodingContext,
        ct))
    {
        yield return JsonSerializer.Deserialize<object>(eventJson.Span)!;
    }
}
```

- Added a managed, cross-platform `.etl` reader (`EtwBackend`): `EtwUtil.ConvertToJson`, `EnumerateEventsAsync` and `EnumeratePdbReferences` now also run on Linux/macOS, including WPP decoding and TraceLogging events (custom-serialized fields use their base in-type unless a TraceLoggingCustomDecoders decoder is registered; both backends decode TraceLogging from the embedded schema)
- Added realtime ETW session support via `EtwRealtimeSession` and `EtwUtil.EnumerateRealtimeEventsAsync`:
  - `EtwRealtimeSession.Create(name)` starts a user-mode ETW session (`StartTraceW`)
  - `EnableProvider` / `DisableProvider` toggle providers at runtime via `EnableTraceEx2`
  - `EtwUtil.EnumerateRealtimeEventsAsync` attaches to any externally-managed session and streams decoded events as UTF-8 JSON using the same bounded-channel / background-thread architecture as the file-based `EnumerateEventsAsync`
  - `EtwUtil.ConvertRealtimeToJson` blocks and writes events to a `Utf8JsonWriter` until cancelled
  - `EtwUtil.StopOrphanSession` cleanly stops a session left behind by a previous crash

## Realtime decoding

The library ships two independent layers for realtime ETW consumption.

### Layer 1 — attach to any existing session (consumer-only)

If the session is already running (started by `logman`, `xperf`, another process, or your own code), attach directly with a session name:

```csharp
using CancellationTokenSource cts = new();

// Cancel after Ctrl+C or when your app shuts down.
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

await foreach (ReadOnlyMemory<byte> eventJson in EtwUtil.EnumerateRealtimeEventsAsync(
    "MySession",
    cancellationToken: cts.Token))
{
    Console.WriteLine(System.Text.Encoding.UTF8.GetString(eventJson.Span));
}
```

### Layer 2 — full session management (start + enable + stop)

Use `EtwRealtimeSession` when your application owns the session lifetime.
**Requires administrator / `SeSystemProfilePrivilege`.**

```csharp
// Clean up any orphan from a previous crash.
EtwUtil.StopOrphanSession("MyApp-Live");

using EtwRealtimeSession session = EtwRealtimeSession.Create("MyApp-Live",
    opts =>
    {
        opts.BufferSizeKb = 64;
        opts.FlushTimerSeconds = 1;
        opts.ClockResolution = EtwClockResolution.QueryPerformanceCounter;
    });

// Enable one or more providers at the desired verbosity level.
Guid microsoftWindowsKernelProcess = new("22FB2CD6-0E7B-422B-A0C7-2FAD1FD0E716");
session.EnableProvider(microsoftWindowsKernelProcess, TraceEventLevel.Information);

using CancellationTokenSource cts = new();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

await foreach (ReadOnlyMemory<byte> eventJson in EtwUtil.EnumerateRealtimeEventsAsync(
    session.SessionName,
    cancellationToken: cts.Token))
{
    Console.WriteLine(System.Text.Encoding.UTF8.GetString(eventJson.Span));
}
// Disposing session calls ControlTraceW(STOP) — no orphan left behind.
```

### Realtime caveats

- **Admin required** to start a session. `EtwRealtimeSession.Create` throws `EtwStartTraceException` with `ERROR_ACCESS_DENIED` when the process is not elevated.
- **Sessions persist after process exit.** Always dispose `EtwRealtimeSession`, and call `EtwUtil.StopOrphanSession("name")` at startup as a safety net.
- **WPP decoding** in realtime mode requires a pre-built `DecodingContext`. The file-based `EnumeratePdbReferences` pre-scan only works on `.etl` files, not on live sessions. Build the `DecodingContext` from known `.pdb` or `.tmf` paths before starting the session.
- **Kernel providers**: `EnumerateRealtimeEventsAsync` attaches by name to an existing `NT Kernel Logger` session (verified against an externally created one). `EtwRealtimeSession.Create` can also create kernel sessions: set `EtwRealtimeSessionOptions.SystemLogger` and enable `EtwSystemProviders` GUIDs (system logger mode is available from Windows 8, enabling the system provider GUIDs requires Windows 11+), or create a session named `EtwRealtimeSession.NtKernelLoggerName` with `EnableFlags` (the single system-wide classic logger).

## Symbol resolution

`Nefarius.Utilities.ETW.Symbols` loads `.pdb` and `.tmf` inputs and resolves PDB identities without shelling out to the CLI:

```csharp
SymbolStoreConfiguration store = SymbolStoreConfiguration.Resolve(
    configuredServer: null,
    configuredCache: null,
    ntSymbolPath: Environment.GetEnvironmentVariable("_NT_SYMBOL_PATH"),
    defaultCacheDirectory: Path.Combine(Path.GetTempPath(), "symbols"));

DecodingSourceSet explicitSources = DecodingSources.Load([@"D:\symbols\driver.pdb", @"D:\tmf"]);
TraceSymbolResolution resolved = await DecodingSources.ResolveTraceSymbolsAsync(
    [@"C:\traces\capture.etl"],
    store);

PortableExecutableSymbols.TryReadCodeViewPdb(@"C:\Windows\System32\drivers\example.sys");
```

Search order is the local symbol cache, then configured directories or filename globs whose PDB GUID and age match, then the HTTP symbol server. `srv*cache*url`, `srv*url`, and `cache*dir` segments of `_NT_SYMBOL_PATH` are recognized.

## CLI (`etwutils`)

The `tools/Nefarius.Utilities.ETW.CLI` project builds a .NET global tool (`etwutils`) that wraps the realtime API and writes decoded events as NDJSON or plain tab-separated text to `stdout`.

```text
dotnet tool install -g Nefarius.Utilities.ETW.CLI
```

See [tools/Nefarius.Utilities.ETW.CLI/README.md](tools/Nefarius.Utilities.ETW.CLI/README.md) for full installation instructions, command reference, and examples.

## Raw ETW relay (decode on a trusted server)

Some capture clients must not hold `.pdb`/`.tmf` files. The relay protocol lets such a client forward **raw `EVENT_RECORD`s** and lets a server you control decode them, WPP included, with this library. The client needs no .NET: the format is a small, versioned, pointer-free binary framing that is easy to produce from C or C++.

```csharp
// server side: `body` is an already authenticated stream of back-to-back frames, e.g. an HTTP request body
await foreach (ReadOnlyMemory<byte> json in EtwUtil.EnumerateRelayEventsAsync(body, o =>
{
    o.WppDecodingContext = new DecodingContext(PdbFileDecodingContextType.CreateFrom("server-only.pdb"));
}))
{
    // json is valid until the next iteration step; copy it first if you keep it
}
```

`RawEtwRelayDecoder` offers the same as a push API that yields `EtwEvent` objects (`EtwValue` tree, no JSON), for integrations that need structured data. Decoding is stateful per session, so one decoder instance must be driven by one thread at a time.

### Wire format, version 1

All integers are little-endian. GUIDs are written in RFC 4122 byte order (`Data1` and the next two fields big-endian), **not** as the in-memory Windows `GUID`. Every frame is a multiple of 8 bytes; padding is zero.

| Offset | Size | Prefix (32 bytes) |
|-------:|-----:|-------------------|
| 0 | 4 | magic `NER1` |
| 4 | 1 | major version (1) |
| 5 | 1 | minor version (0); receivers accept any minor |
| 6 | 2 | frame type: 0 hello, 1 event, 3 loss, 4 end |
| 8 | 4 | total frame length including the prefix |
| 12 | 4 | frame flags (0) |
| 16 | 8 | producer sequence number |
| 24 | 2 | length of the type-specific header |
| 26 | 2 | number of extended data items (event frames) |
| 28 | 4 | CRC32C of the whole frame computed with this field zeroed; 0 when absent |

A session is `hello` (sequence 0), then `event`/`loss` frames with sequence 1, 2, 3, …, optionally closed by `end`.

* **hello** header (32 bytes): session GUID (16), capabilities `u32` (0), maximum frame length `u32` the producer may send, 8 reserved bytes. The body is the UTF-8 producer name, padded.
* **loss** header (16 bytes): lost event count `u64`, reason `u32`, 4 reserved bytes.
* **end**: no header.
* **event** header (96 bytes), followed by the payload padded to 8 bytes and then the extended data items:

| Offset | Size | Field |
|-------:|-----:|-------|
| 0 | 2 | `EVENT_HEADER.Size` |
| 2 | 2 | `HeaderType` |
| 4 | 2 | `Flags` |
| 6 | 2 | `EventProperty` |
| 8 | 4 | `ThreadId` |
| 12 | 4 | `ProcessId` |
| 16 | 8 | `TimeStamp` (FILETIME) |
| 24 | 16 | `ProviderId` |
| 40 | 2 | `EventDescriptor.Id` |
| 42 | 1 | `Version` |
| 43 | 1 | `Channel` |
| 44 | 1 | `Level` |
| 45 | 1 | `Opcode` |
| 46 | 2 | `Task` |
| 48 | 8 | `Keyword` |
| 56 | 8 | `ProcessorTime` (the `KernelTime`/`UserTime` pair) |
| 64 | 16 | `ActivityId` |
| 80 | 4 | `BufferContext` (`ProcessorNumber` and `LoggerId`) |
| 84 | 1 | timestamp kind: 1 = FILETIME (the only supported value) |
| 85 | 1 | pointer size of the producing system, 4 or 8 |
| 86 | 1 | 1 when the WPP sequence number below is valid |
| 87 | 1 | reserved |
| 88 | 4 | WPP sequence number |
| 92 | 4 | payload length (`UserDataLength`) |

Each extended data item has an 8-byte header (`Reserved1` `u16`, `ExtType` `u16`, `Linkage` `u16`, data length `u16`) followed by its data padded to 8 bytes. Items are copied by value; pointer-based items such as stack traces are carried as their data.

Golden vectors live in `tests/UnitTestRawEtwRelay.cs` (`GoldenEventFrameHex`, `GoldenHelloFrameHex`) and are stable for version 1.

### Producing frames from C or C++

```cpp
// ProcessTrace callback of an OpenTrace session (EVENT_TRACE_LOGFILE.EventRecordCallback)
void WINAPI OnEvent(PEVENT_RECORD r)
{
    // 1. Copy everything NOW: r and its buffers are invalid after the callback returns.
    // 2. Never send pointers: UserData and ExtendedData are copied by value.
    // 3. Write GUIDs in RFC 4122 order and timestamps as FILETIME (do not use
    //    PROCESS_TRACE_MODE_RAW_TIMESTAMP).
    std::vector<uint8_t> frame = BuildEventFrame(nextSequence++, r->EventHeader, r->BufferContext,
                                                 r->UserData, r->UserDataLength,
                                                 r->ExtendedData, r->ExtendedDataCount);
    queue.Push(std::move(frame));   // sender thread: add a CRC32C, batch, authenticate, transmit
}
```

Report events you had to drop with a `loss` frame so the server can account for them. A retried frame with the same sequence number and content is acknowledged without being decoded twice; a different frame for an already used sequence number or a gap is refused.

### Limits and trust model

The sender is untrusted. All lengths are validated against `RawEtwRelayLimits` (frame length, extended item count and size) **before** anything is allocated, records are rebuilt in private memory with no sender pointers, and relayed records are never handed to TDH (a manifest event therefore needs a custom manifest registered with the decoder). Malformed input throws `RawEtwProtocolException` and leaves an established session intact.

### Hosting contract

The library decodes; **you** own everything around it. The stream passed in must already be authenticated and authorized, and the host is responsible for TLS, identity, authorization, replay protection across sessions, quotas and rate limits, and any SSE framing of the output. PDB/TMF files stay on the server. Copy pooled output buffers before advancing the enumerator.

## OpenTelemetry integration (optional)

OpenTelemetry is not required by the core package. `integrations/Nefarius.Utilities.ETW.OpenTelemetry` adds an optional adapter, and `tools/Nefarius.Utilities.ETW.OtlpGateway` is a ready-to-run decoding gateway built on it.

**Transport.** A client wraps each relay frame in an OTLP `LogRecord` whose body is the frame as `bytes_value`, grouped under a resource carrying the attribute `etw.relay.session_id` (the session GUID), and sends it with any OTLP/HTTP or OTLP/gRPC exporter. `RelayLogRecords.CreateRequest` does this on .NET.

**Decoded output.** The gateway answers with standard OTLP Logs for any storage backend: the event name becomes `event_name`, TraceLogging and manifest properties become a structured `kvlist` body, WPP events carry the formatted message as the string body (`etw.wpp.*` attributes hold the rest), ETW level maps to severity, and the header fields become `etw.*` attributes (`etw.provider.id`, `etw.event.id`, `etw.process.id`, …).

```text
capture client -- OTLP (frames) --> [ Collector / proxy ] --> decoding gateway --> OTLP Logs --> Collector --> backend
   no symbols                         auth, TLS, routing      PDB/TMF live here
```

**Collector constraints.** Any Collector between the client and the gateway must forward the bodies unchanged and keep the order of records within a session. Do not substitute the Collector's Windows Event Log receiver or any receiver that decodes events on the collecting host: those need the provider's metadata on that host, which is exactly what this design avoids.

### Running the gateway

```text
dotnet run --project tools/Nefarius.Utilities.ETW.OtlpGateway
```

Configure it through the `Gateway` section of `appsettings.json` or environment variables (`Gateway__Export__Endpoint=http://collector:4318`):

| Key | Purpose |
|-----|---------|
| `Export:Endpoint` | downstream OTLP/HTTP base URL; empty discards decoded records |
| `Export:Headers`, `QueueCapacity`, `MaxAttempts`, `InitialRetryDelay`, `MaxRetryDelay`, `RequestTimeout` | delivery, bounded queue and retry |
| `Wpp:PdbFiles`, `Wpp:PdbDirectories`, `Wpp:TmfDirectories`, `Wpp:RewriteProviderName` | server-only WPP assets |
| `Limits:*` | body size, frame size, session counts (total and per tenant), records per request, idle timeout |
| `RequireAuthorization` | require an authenticated user; you register the authentication scheme |

It listens for OTLP/HTTP (`POST /v1/logs`, protobuf, optional gzip) on port 4318 and OTLP/gRPC on 4317, serves `/healthz`, and publishes counters under the meter `Nefarius.Utilities.ETW.OtlpGateway`.

If the downstream receiver is slow or down, the gateway buffers decoded batches in a bounded queue and retries. When the queue is full it answers **before consuming any frame** with HTTP 503 and `Retry-After` (gRPC `UNAVAILABLE`), so the sender can resend the identical request; frames that were accepted are never lost to a refusal. A batch that still fails after `MaxAttempts` is dropped and counted. A rejected frame is reported through the standard OTLP `partial_success` response; a continuation of a session the gateway does not know (expired or restarted) is rejected so the client opens a new session with a hello frame.

### Embedding in your own host

Authentication stays yours. Register your scheme, then:

```csharp
builder.Services.AddEtwOtlpGateway(builder.Configuration);
// ...
app.MapEtwOtlpGateway().RequireAuthorization("etw-ingest");   // applies to HTTP and gRPC
```

Sessions are isolated per tenant. The default `IGatewayTenantResolver` uses the authenticated user's name or `sub` claim; replace it to derive the tenant from your own identity model. Replace `IDecodedLogSink` to deliver decoded records somewhere other than OTLP/HTTP.

## Known limitations

- Reading `.etl` files works on Windows, Linux and macOS through a managed reader (`EtwBackend.Managed`, the default off Windows; set `EtwJsonConverterOptions.Backend` / `EtwMetadataScanOptions.Backend` to choose). Real-time sessions and session management remain **Windows-only**. Manifest-described events decode on every platform, including structs, fixed and variable array counts, fixed and variable lengths, declared out-types and value/bit maps (maps are kept in the property metadata; JSON output keeps the raw values) and throw `PlatformNotSupportedException` elsewhere
- The managed reader reads compressed (`EVENT_TRACE_COMPRESSED_MODE`, Xpress LZ77) `.etl` files by decompressing every buffer into memory first, and covers the record types found in WPP, kernel and TraceLogging/manifest traces; unsupported record types are skipped with a warning via `ReportError`
- `%!ItemEnum!`/`%!ItemFlagsEnum!` types are shown with enumerator names when the enum type (named in the TMF as `ItemEnum(Name)`) is found in a PDB type stream; from TMF files, or when the type is missing, the raw value is shown. Without a `WppDecodingContext`, WPP events are still emitted with the "No format information found." message and `OnWppFormatMissing` fires
- `%!CLSID!` prints the GUID without a registry name lookup and `%!LINE!` is rendered as an empty string
- Relayed records are never decoded with TDH, so manifest-described events need a custom manifest on the server; raw (QPC) timestamps are not supported by the relay, and one relay decoder or session must be driven by a single thread at a time
- The CLI does not create kernel sessions; use the library API (`SystemLogger` / `NtKernelLoggerName`) or attach to an existing session by name

## Documentation

[Link to API docs](docs/index.md).

## Sources & 3rd party credits

- [Microsoft/ETW2JSON](https://github.com/microsoft/ETW2JSON)
- [Microsoft/ETW](https://github.com/microsoft/ETW)
- [WPP Software Tracing](https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/wpp-software-tracing)
- [microsoftarchive/bcl/Tools/ETW/traceEvent/SymbolEventParser.cs](https://github.com/microsoftarchive/bcl/blob/d646329371acaf696529a85e2aeb7c54639f9e70/Tools/ETW/traceEvent/SymbolEventParser.cs)
- [`enum _TDH_CONTEXT_TYPE`](https://github.com/cheolw00myung/cross-compile_for_Windows/blob/08935f0864f497ee7fc6f13aba1b598701a04be1/SDK10/include/um/tdh.h#L798-L816)
- [Learn / Windows / Windows Drivers / How do I add a prefix and suffix to a trace message?](https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/how-do-i-add-a-prefix-and-suffix-to-a-trace-message-#configuration-block-syntax)
- [Learn / Windows / Windows Drivers / What are the WPP extended format specification strings](https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/what-are-the-wpp-extended-format-specification-strings-#software-tracing)
- [kaitai-pdb](https://github.com/smx-smx/kaitai-pdb)
- [MinVer](https://github.com/adamralph/minver)
- [Fast access to .net fields/properties](https://github.com/mgravell/fast-member)
- [Nefarius.Shared.PdbUtils](https://github.com/nefarius/PdbUtils)
- [Information from Microsoft about the PDB format](https://github.com/microsoft/microsoft-pdb)
