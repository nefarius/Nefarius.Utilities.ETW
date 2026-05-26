# etwutils — Nefarius.Utilities.ETW.CLI

[![.NET](https://github.com/nefarius/Nefarius.Utilities.ETW/actions/workflows/build.yml/badge.svg)](https://github.com/nefarius/Nefarius.Utilities.ETW/actions/workflows/build.yml)
![Requirements](https://img.shields.io/badge/Requires-.NET%208%2F9%2F10-blue.svg)
![Windows only](https://img.shields.io/badge/Windows-8.0+-red)
[![NuGet Version](https://img.shields.io/nuget/v/Nefarius.Utilities.ETW.CLI)](https://www.nuget.org/packages/Nefarius.Utilities.ETW.CLI/)
[![NuGet](https://img.shields.io/nuget/dt/Nefarius.Utilities.ETW.CLI)](https://www.nuget.org/packages/Nefarius.Utilities.ETW.CLI/)

A .NET global tool (`etwutils`) that wraps the realtime ETW API of [Nefarius.Utilities.ETW](https://www.nuget.org/packages/Nefarius.Utilities.ETW/) and writes decoded events as **NDJSON** or **plain tab-separated** text on `stdout`, making it trivial to pipe into `jq`, `grep`, log aggregators, or any line-oriented consumer.

> **Admin required.** ETW session creation requires an elevated process.

## Installation

```text
dotnet tool install -g Nefarius.Utilities.ETW.CLI
```

This makes the `etwutils` command available on `PATH`. Requires the .NET 8, 9, or 10 **SDK** on Windows (`dotnet tool` is an SDK feature, not available with the runtime-only install).

## Commands

### `realtime`

Start a live ETW capture session and stream decoded events to `stdout`.

```text
etwutils realtime [<provider-guid> ...]
    [--keywords           <hex|dec>]   # match-any mask, default 0xFFFFFFFFFFFFFFFF
    [--match-all-keywords <hex|dec>]   # match-all mask, default 0
    [--level <Critical|Error|Warning|Information|Verbose>]  # default Verbose
    [--session-name <name>]            # default NefariusEtwCli-<pid>
    [--symbols <path>] ...             # repeatable; PDB file, directory, or glob
    [--buffer-size-kb <n>]             # ETW buffer size, default 64
    [--flush-seconds  <n>]             # flush interval, default 1
    [--format <ndjson|plain>]          # output format, default ndjson
    [--color  <auto|always|never>]     # colorize Level column (plain only), default auto
```

`provider-guid` is **optional**. When omitted, the tool reads the `WPP_DEFINE_CONTROL_GUID` declarations embedded in the PDB files passed to `--symbols` and uses those as the provider list. Explicit GUIDs always take precedence; PDB-derived GUIDs are not added on top of explicit ones.

> **Note:** Auto-derivation requires **PDB files**. TMF files do not contain the WPP control GUID (they only hold per-call-site message format data). If `--symbols` points to a directory or glob that contains only TMF files, you must also supply `provider-guid` explicitly.

### `inspect-pdb`

Parse PDB files and report every embedded WPP provider GUID without starting an ETW session.

```text
etwutils inspect-pdb <path> [<path> ...]
    [--format <plain|ndjson>]          # output format, default plain
```

Lists every `WPP_DEFINE_CONTROL_GUID` found, along with the control name and declared `WPP_DEFINE_BIT` flag names. Accepts the same path forms as `--symbols` (PDB files, directories, glob patterns). TMF files are silently ignored.

## Examples

Capture all events from a provider and pretty-print them with `jq`:

```bash
# Replace the GUID below with the actual provider GUID you want to trace.
etwutils realtime {12345678-1234-1234-1234-1234567890AB} | jq .
```

Auto-derive the provider GUID from a PDB and stream all events:

```bash
# The control GUID is extracted from WPP_DEFINE_CONTROL_GUID in the PDB.
etwutils realtime --symbols C:\Symbols\MyDriver.pdb | jq .
```

Capture only errors and warnings, with WPP symbol files loaded from a directory:

```bash
etwutils realtime {12345678-1234-1234-1234-1234567890AB} \
    --level Warning \
    --symbols C:\Symbols\MyDriver.pdb \
    --symbols C:\Symbols\TMFs\
```

Glob expansion — load every PDB from an entire symbol tree, auto-derive all providers:

```bash
etwutils realtime \
    --keywords 0xFFFFFFFF --level Verbose \
    --symbols "C:\Symbols\**\*.pdb"
```

Stream events in human-friendly plain format (colorized Level when stdout is a TTY):

```bash
etwutils realtime --symbols C:\Symbols\MyDriver.pdb --format plain
# 2026-05-26T14:51:23.1234567+02:00	BthPS3TraceGuid	TRACE_LEVEL_INFORMATION	Device arrived: USB\VID_054C&PID_09CC
```

Pipe plain output into `column` for aligned columns (disables color automatically):

```bash
etwutils realtime --symbols C:\Symbols\MyDriver.pdb --format plain | column -t -s $'\t'
```

Force color off even on a TTY:

```bash
etwutils realtime --symbols C:\Symbols\MyDriver.pdb --format plain --color never
```

List provider GUIDs embedded in a PDB (plain, human-readable — includes control name and bit flags):

```text
etwutils inspect-pdb C:\Symbols\MyDriver.pdb

{37DCD579-E844-4C80-9C8B-A10850B6FAC6}  BthPS3TraceGuid                 (BthPS3.pdb, 9 bit flags)
    MYDRIVER_ALL_INFO
    TRACE_DRIVER
    TRACE_DEVICE
    TRACE_QUEUE
    ...
```

List provider GUIDs as NDJSON for use in a script:

```bash
etwutils inspect-pdb C:\Symbols\MyDriver.pdb --format ndjson | jq .
# { "guid": "{37DCD579-...}", "name": "BthPS3TraceGuid", "bitFlags": ["MYDRIVER_ALL_INFO", ...], "source": "BthPS3.pdb" }
```

## Output formats

### NDJSON (default)

Each line written to `stdout` is a self-contained JSON object. Status and error messages are written to `stderr` so the `stdout` pipe stays clean. Ctrl+C stops the session gracefully; the tool exits with code `0` on clean shutdown and `1` on fatal error.

```jsonc
// stdout — one JSON object per line (NDJSON)
{"EventName":"ProcessStart","ProcessId":1234,"ImageName":"notepad.exe", ...}
{"EventName":"ProcessStop","ProcessId":1234, ...}

// stderr — human-readable status (never mixed into stdout)
[*] Session 'NefariusEtwCli-9876' started. Providers (auto-derived from symbols): {37DCD579-...} | level=Verbose | keywords=0xFFFFFFFF
[*] Streaming events... (Ctrl+C to stop)
[*] Done.

# Example pipeline
etwutils realtime --symbols C:\Symbols\MyDriver.pdb | jq .
```

### Plain (tab-separated)

`--format plain` writes one event per line to `stdout` as four tab-separated columns:

| Column | Content |
|--------|---------|
| Timestamp | Local time in ISO-8601 with UTC offset, e.g. `2026-05-26T14:51:23.1234567+02:00` |
| Provider | WPP provider friendly name (`GuidName`), or the first segment of the TDH event name for non-WPP events |
| Level | WPP level string (`TRACE_LEVEL_INFORMATION`, etc.) or `-` for non-WPP events |
| Message | WPP formatted message, or a compact JSON representation of the raw properties for non-WPP events |

Embedded tabs and newlines in the message are escaped to `\t` and `\n` so each event always occupies exactly one output line.

When stdout is a TTY (and `NO_COLOR` is not set), the Level column is automatically colorized: Critical/Fatal → bright red, Error → red, Warning → yellow, Information → cyan, Verbose → gray. Color is suppressed automatically when piping. Use `--color always|never` to override.

```text
2026-05-26T14:51:23.1234567+02:00	BthPS3TraceGuid	TRACE_LEVEL_INFORMATION	Device arrived: USB\VID_054C&PID_09CC
2026-05-26T14:51:23.5678901+02:00	BthPS3TraceGuid	TRACE_LEVEL_VERBOSE	  ConnectRequest: handle=0x0003
```

## Known limitations

- Currently relies on **Windows-only** APIs so no support for other platforms.
- `%!ItemEnum!`/`%!ItemFlagsEnum!` types display raw numeric values; PDB-based enum name resolution is not yet implemented.
- Kernel-mode ETW providers and the NT Kernel Logger session are not yet supported.

## Links

- [GitHub repository](https://github.com/nefarius/Nefarius.Utilities.ETW)
- [Nefarius.Utilities.ETW library on NuGet](https://www.nuget.org/packages/Nefarius.Utilities.ETW/)
