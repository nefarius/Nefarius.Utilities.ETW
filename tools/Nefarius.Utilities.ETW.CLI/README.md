# etwutils — Nefarius.Utilities.ETW.CLI

[![.NET](https://github.com/nefarius/Nefarius.Utilities.ETW/actions/workflows/build.yml/badge.svg)](https://github.com/nefarius/Nefarius.Utilities.ETW/actions/workflows/build.yml)
![Requirements](https://img.shields.io/badge/Requires-.NET%208%2F9%2F10-blue.svg)
![Windows only](https://img.shields.io/badge/Windows-8.0+-red)
[![NuGet Version](https://img.shields.io/nuget/v/Nefarius.Utilities.ETW.CLI)](https://www.nuget.org/packages/Nefarius.Utilities.ETW.CLI/)
[![NuGet](https://img.shields.io/nuget/dt/Nefarius.Utilities.ETW.CLI)](https://www.nuget.org/packages/Nefarius.Utilities.ETW.CLI/)

A .NET global tool (`etwutils`) that wraps the ETW API of [Nefarius.Utilities.ETW](https://www.nuget.org/packages/Nefarius.Utilities.ETW/) and writes decoded events as **NDJSON** or **plain tab-separated** text, making it trivial to pipe into `jq`, `grep`, log aggregators, or any line-oriented consumer. Supports both **realtime** capture and **offline** `.etl` file decoding.

> **Admin required.** ETW session creation requires an elevated process.

## Installation

```text
dotnet tool install -g Nefarius.Utilities.ETW.CLI
```

This makes the `etwutils` command available on `PATH`. Requires the .NET 8, 9, or 10 **SDK** on Windows (`dotnet tool` is an SDK feature, not available with the runtime-only install).

## Commands

### `parse`

Decode one or more offline `.etl` files and emit events as NDJSON or plain TSV to **stdout** (all inputs time-merged by ETW) or to **per-file output** in a target directory.

```text
etwutils parse <etl-path> [<etl-path> ...]
    [--out-dir          <path>]        # per-file output dir; default: stream to stdout
    [--symbols          <path>] ...    # PDB / TMF / dir / glob, loaded unconditionally
    [--symbols-search   <path>] ...    # dirs/globs searched only for PDBs the trace references
    [--symbol-server    <url>]         # symbol store root URL (SymSrv-compatible)
    [--symbol-cache     <path>]        # local symstore-layout cache; default below
    [--format           <ndjson|plain>]# default ndjson
    [--color            <auto|always|never>]  # plain stdout only; default auto
    [--preserve-raw-timestamps]        # apply PROCESS_TRACE_MODE_RAW_TIMESTAMP
    [--columns          <list>]        # plain only: comma-separated column tokens; default Timestamp,Provider,Level,Message
    [--header]                         # plain only: emit a TSV header line first
    [--filter           <expression>]  # plain only: DynamicExpresso predicate to drop events
```

`<etl-path>` accepts individual `.etl` files, directories (top-level `*.etl` enumeration), and glob patterns (e.g. `C:\Traces\*.etl`). Multiple paths are accepted and deduplicated.

#### Output modes

| Mode | How to activate | Output naming |
|------|-----------------|---------------|
| Stdout (default) | omit `--out-dir` | all inputs merged, written to stdout |
| Per-file | `--out-dir <path>` | `trace.etl` → `<path>/trace.ndjson` (or `.tsv`) |

#### WPP symbol auto-discovery

When `--symbols-search`, `--symbol-server`, or `--symbol-cache` is supplied (or `_NT_SYMBOL_PATH` provides a server or cache), the tool runs a **pre-scan** of the input files with `EnumeratePdbReferences` and resolves each referenced PDB in order:

1. **Local cache** — `<cache>/<pdbname>/<GUID><AGE>/<pdbname>` (SymSrv-compatible layout).
2. **Search paths** — first case-insensitive filename match in `--symbols-search` directories.
3. **Symbol server** — `GET <url>/<pdbname>/<GUID><AGE>/<pdbname>`; downloaded atomically into the cache.

Unresolved PDBs log a `[!]` warning and are non-fatal; affected WPP events fall back to a `GUID=...` placeholder.

##### Default cache directory

`%LOCALAPPDATA%\Nefarius\etwutils\symcache` (created on demand). Override with `--symbol-cache`.

##### `_NT_SYMBOL_PATH` fallback

When neither `--symbol-server` nor `--symbol-cache` is passed, the tool reads the first parseable segment of the `_NT_SYMBOL_PATH` environment variable (the same variable consumed by WinDbg/DbgHelp):

| Segment form | Effect |
|---|---|
| `srv*<cache>*<url>` | sets both cache and server |
| `srv*<url>` | sets server; uses default cache |
| `cache*<dir>` | sets cache only |
| anything else | prints a notice and is ignored |

> **Tip:** [WinDbgSymbolsCachingProxy](https://github.com/nefarius/WinDbgSymbolsCachingProxy) is a ready-made caching proxy that speaks the same SymSrv protocol. Point `--symbol-server` (or `_NT_SYMBOL_PATH`) at `https://symbols.nefarius.at/download/symbols` to use the public instance, or self-host your own.

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
    [--columns <list>]                 # plain only: comma-separated column tokens; default Timestamp,Provider,Level,Message
    [--header]                         # plain only: emit a TSV header line first
    [--filter <expression>]            # plain only: DynamicExpresso predicate to drop events
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

### Offline `.etl` parsing

Decode a single trace file and stream events as NDJSON to stdout:

```bash
etwutils parse BthPS3_0.etl | jq .
```

Decode multiple files, time-merging their events, with WPP symbols loaded from a PDB:

```bash
etwutils parse trace1.etl trace2.etl --symbols C:\Symbols\MyDriver.pdb | jq .
```

Decode every `.etl` in a directory and write one output file each to a target directory:

```bash
etwutils parse C:\Traces\ --out-dir C:\Decoded --symbols C:\Symbols\MyDriver.pdb
# stderr:
# [*] Resolved 3 .etl file(s).
# [*] trace1.etl -> C:\Decoded\trace1.ndjson
#     1,234 event(s) written.
# ...
```

Plain TSV per-file output:

```bash
etwutils parse C:\Traces\ --out-dir C:\Decoded --format plain
```

Parse with custom column order and a header row:

```bash
etwutils parse BthPS3_0.etl --symbols C:\Symbols\BthPS3.pdb \
    --format plain --columns Timestamp,Level,Function,Message --header
```

Filter to only errors and above, drop a noisy provider:

```bash
etwutils parse BthPS3_0.etl --symbols C:\Symbols\BthPS3.pdb \
    --format plain \
    --filter "LevelNumber <= 3 && LevelNumber > 0 && Provider != \"BthPS3PSM\""
```

Select specific columns and filter by message prefix, with a header, written to per-file TSV:

```bash
etwutils parse C:\Traces\ --out-dir C:\Decoded \
    --format plain --columns Timestamp,Pid,Level,Message --header \
    --filter "Message.StartsWith(\"[BthPS3\")"
```

Auto-discover and download PDB symbols from a symbol server, then decode:

```bash
etwutils parse BthPS3_0.etl \
    --symbol-server https://symbols.nefarius.at/download/symbols \
    --symbols-search C:\Symbols | jq .
# stderr:
# [*] Resolved 1 .etl file(s).
# [*] Auto-discovery: resolving 2 PDB reference(s)...
#     [cache ] BthPS3.pdb
#     [server] BthPS3PSM.pdb <- https://symbols.nefarius.at/download/symbols/bthps3psm.pdb/...
# [*] Auto-discovery complete: 2/2 resolved (1 cache, 0 local, 1 downloaded, 0 unresolved).
# [*] Streaming 1 .etl file(s) to stdout...
```

Using `_NT_SYMBOL_PATH` (already set for WinDbg):

```bash
# _NT_SYMBOL_PATH=srv*C:\symbols*https://msdl.microsoft.com/download/symbols
etwutils parse BthPS3_0.etl --symbols-search C:\Symbols
# The cache and server are picked up automatically from _NT_SYMBOL_PATH.
```

### Realtime capture

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

Select a custom column set (Timestamp, Pid, Function, Message) and add a header line:

```bash
etwutils realtime --symbols C:\Symbols\MyDriver.pdb --format plain \
    --columns Timestamp,Pid,Function,Message --header
```

Show only errors and warnings in realtime, and output just timestamp and message:

```bash
etwutils realtime --symbols C:\Symbols\MyDriver.pdb --format plain \
    --columns Timestamp,Message \
    --filter "LevelNumber <= 3 && LevelNumber > 0"
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

`--format plain` writes one event per line to `stdout` as tab-separated columns. By default four columns are emitted; use `--columns` to change the set and order.

#### Default columns

| Column token | Content |
|---|---|
| `Timestamp` | Local time in ISO-8601 with UTC offset, e.g. `2026-05-26T14:51:23.1234567+02:00` |
| `Provider` | WPP provider friendly name (`GuidName`), or the first segment of the TDH event name for non-WPP events |
| `Level` | WPP level string (`TRACE_LEVEL_INFORMATION`, etc.) or `-` for non-WPP events |
| `Message` | WPP formatted message, or a compact JSON representation of the raw properties for non-WPP events |

#### Available column tokens

All tokens below are available to both `--columns` and `--filter`.

| Token | Type | Content | WPP-only |
|---|---|---|---|
| `Timestamp` | string | Local ISO-8601 timestamp with UTC offset | no |
| `Provider` | string | WPP `GuidName` or first TDH event name segment | no |
| `ProviderGuid` | string | Provider GUID in `D` format | no |
| `Level` | string | WPP `LevelName` or `-` for non-WPP events | no |
| `LevelNumber` | int | Numeric level 1 (Critical) … 5 (Verbose); 0 when unknown | no |
| `Message` | string | WPP `FormattedString` or compact JSON of raw properties | no |
| `EventName` | string | Full TDH event name (`Provider/Task/Opcode`) or `WPP` | no |
| `EventId` | int | Event Id from the event header | no |
| `Pid` | int | Process identifier | no |
| `Tid` | int | Thread identifier | no |
| `Cpu` | int | Processor number | no |
| `ActivityId` | string | Activity GUID or empty | no |
| `RelatedActivityId` | string | Related-activity GUID or empty | no |
| `Function` | string | WPP `FunctionName` | yes |
| `Component` | string | WPP `ComponentName` | yes |
| `SubComponent` | string | WPP `SubComponentName` | yes |
| `Flags` | string | WPP `FlagsName` | yes |

Embedded tabs and newlines in every cell value are escaped to `\t` and `\n` so each event always occupies exactly one output line.

When stdout is a TTY (and `NO_COLOR` is not set), the `Level` column (when present) is automatically colorized: Critical/Fatal → bright red, Error → red, Warning → yellow, Information → cyan, Verbose → gray. Color is suppressed automatically when piping. Use `--color always|never` to override.

```text
2026-05-26T14:51:23.1234567+02:00	BthPS3TraceGuid	TRACE_LEVEL_INFORMATION	Device arrived: USB\VID_054C&PID_09CC
2026-05-26T14:51:23.5678901+02:00	BthPS3TraceGuid	TRACE_LEVEL_VERBOSE	  ConnectRequest: handle=0x0003
```

#### Filter expression language

`--filter` accepts a C#-like boolean expression evaluated per event by [DynamicExpresso](https://github.com/dynamicexpresso/DynamicExpresso). Events for which the expression returns `false` are silently dropped. Parse errors abort startup with exit code 2; per-event evaluation errors are logged to stderr and drop the event (non-fatal).

All column tokens listed above are available as identifiers directly in the expression (no prefix needed):

```text
# Skip all events from a specific provider
--filter "Provider != \"BthPS3PSM\""

# Only show errors and above (LevelNumber <= 3 covers Critical, Error, Warning)
--filter "LevelNumber <= 3 && LevelNumber > 0"

# Keep only events whose message starts with a specific string
--filter "Message.StartsWith(\"[BthPS3\")"

# Combine conditions
--filter "Provider == \"BthPS3\" && !Message.Contains(\"Verbose\")"
```

## Known limitations

- Currently relies on **Windows-only** APIs so no support for other platforms.
- `%!ItemEnum!`/`%!ItemFlagsEnum!` types display raw numeric values; PDB-based enum name resolution is not yet implemented.
- Kernel-mode ETW providers and the NT Kernel Logger session are not yet supported.

## Links

- [GitHub repository](https://github.com/nefarius/Nefarius.Utilities.ETW)
- [Nefarius.Utilities.ETW library on NuGet](https://www.nuget.org/packages/Nefarius.Utilities.ETW/)
