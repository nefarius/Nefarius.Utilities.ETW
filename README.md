# <img src="assets/NSS-128x128.png" align="left" />Nefarius.Utilities.ETW

[![.NET](https://github.com/nefarius/Nefarius.Utilities.ETW/actions/workflows/build.yml/badge.svg)](https://github.com/nefarius/Nefarius.Utilities.ETW/actions/workflows/build.yml)
![Requirements](https://img.shields.io/badge/Requires-.NET%208%2F9%2F10-blue.svg)
![Windows only](https://img.shields.io/badge/Windows-8.0+-red)
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
  - Full support for all [WPP extended format specification strings](https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/what-are-the-wpp-extended-format-specification-strings-)
    (`%!FUNC!`, `%!LEVEL!`, `%!FLAGS!`, `%!IPADDR!`, `%!TIMESTAMP!`, `%!delta!`, `%!due!`, `%!GUID!`,
    `%!CLSID!`/`%!LIBID!`/`%!IID!`, `%!PORT!`, `%!STATUS!`, `%!WINERROR!`, `%!HRESULT!`,
    `%!NDIS_STATUS!`, `%!NDIS_OID!`, `%!sid!`, bitset/list enumerations, and more)
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
- **Kernel-mode providers / NT Kernel Logger** are not supported in this release.

## CLI (`etwutils`)

The `tools/Nefarius.Utilities.ETW.CLI` project builds a .NET global tool (`etwutils`) that wraps the realtime API and writes decoded events as NDJSON or plain tab-separated text to `stdout`.

```text
dotnet tool install -g Nefarius.Utilities.ETW.CLI
```

See [tools/Nefarius.Utilities.ETW.CLI/README.md](tools/Nefarius.Utilities.ETW.CLI/README.md) for full installation instructions, command reference, and examples.

## Known limitations

- Currently relies on **Windows-only** APIs so no support for other platforms
- `%!ItemEnum!`/`%!ItemFlagsEnum!` types display raw numeric values; PDB-based enum name resolution is not yet implemented
- Kernel-mode ETW providers and the NT Kernel Logger session are not yet supported for realtime mode

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
