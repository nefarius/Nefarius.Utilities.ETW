# etwutils — Nefarius.Utilities.ETW.CLI

[![.NET](https://github.com/nefarius/Nefarius.Utilities.ETW/actions/workflows/build.yml/badge.svg)](https://github.com/nefarius/Nefarius.Utilities.ETW/actions/workflows/build.yml)
![Requirements](https://img.shields.io/badge/Requires-.NET%208%2F9%2F10-blue.svg)
![Windows only](https://img.shields.io/badge/Windows-8.0+-red)
[![NuGet Version](https://img.shields.io/nuget/v/Nefarius.Utilities.ETW.CLI)](https://www.nuget.org/packages/Nefarius.Utilities.ETW.CLI/)
[![NuGet](https://img.shields.io/nuget/dt/Nefarius.Utilities.ETW.CLI)](https://www.nuget.org/packages/Nefarius.Utilities.ETW.CLI/)

A .NET global tool (`etwutils`) that wraps the ETW API of [Nefarius.Utilities.ETW](https://www.nuget.org/packages/Nefarius.Utilities.ETW/) and writes decoded events as **NDJSON** or **plain tab-separated** text, making it trivial to pipe into `jq`, `grep`, log aggregators, or any line-oriented consumer. Supports both **realtime** capture and **offline** `.etl` file decoding.

> [!IMPORTANT]  
> While this tool is designed to support universal ETW trace sources, the primary personal goal and highest development priority has been making it work reliably with [WPP Software Tracing](https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/wpp-software-tracing) in particular. Other trace source types may work but receive less focused attention and testing.

> [!WARNING]  
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
    [--keep-original-provider]         # keep raw WPP GuidName instead of rewriting to TMC: friendly name
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

Unresolved PDBs log a `[!]` warning and are non-fatal; affected WPP events fall back to a `GUID=...` placeholder. In addition, each distinct provider GUID that triggers the placeholder also emits a one-time `[!]` warning to `stderr` at decode time, so symbol mismatches are surfaced immediately even when piping output through `--filter`.

#### WPP provider name rewrite

By default, when PDB files containing `WPP_DEFINE_CONTROL_GUID` declarations are loaded, the `Provider`/`GuidName` field in every WPP event is rewritten from the raw folder-derived token (e.g. `obj\amd64`) to the friendly name declared in the source (`BthPS3TraceGuid`). The rewrite is best-effort:

- Events whose control GUID is not found in any loaded PDB are left unchanged.
- TMF-only sources do not carry control-GUID names, so no rewrite occurs, but a one-time informational notice is emitted to stderr to make the situation visible.

Pass `--keep-original-provider` to suppress the rewrite and use the original decoder value as-is.

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

> [!TIP]  
> [WinDbgSymbolsCachingProxy](https://github.com/nefarius/WinDbgSymbolsCachingProxy) is a ready-made caching proxy that speaks the same SymSrv protocol. Point `--symbol-server` (or `_NT_SYMBOL_PATH`) at `https://symbols.nefarius.at/download/symbols` to use the public instance, or self-host your own.

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
    [--keep-original-provider]         # keep raw WPP GuidName instead of rewriting to TMC: friendly name
    [--driver <service-name>] ...       # repeatable; auto-resolve binary, download PDB, derive provider GUIDs
    [--driver-type <kernel|umdf|auto>] # applied to every --driver; default auto
    [--driver-binary <path>]           # explicit binary override for a single --driver; skips ImagePath lookup
    [--symbol-server <url>]            # symbol store root URL shared by all --driver resolutions
    [--symbol-cache  <path>]           # local symstore-layout cache; default %LOCALAPPDATA%\Nefarius\etwutils\symcache
```

`provider-guid` is **optional**. When omitted, the tool reads the `WPP_DEFINE_CONTROL_GUID` declarations embedded in the PDB files passed to `--symbols` (or resolved via `--driver`) and uses those as the provider list. Explicit GUIDs always take precedence; PDB-derived GUIDs are not added on top of explicit ones.

> [!NOTE]  
> Auto-derivation requires **PDB files**. TMF files do not contain the WPP control GUID (they only hold per-call-site message format data). If `--symbols` points to a directory or glob that contains only TMF files, you must also supply `provider-guid` explicitly.

The provider name rewrite described under `parse` applies here too. Pass `--keep-original-provider` to disable it.

#### `--driver` — zero-configuration capture from a service name

When `--driver <name>` is supplied, `etwutils` performs the following steps automatically before starting the capture:

1. **Locate the binary** — reads `HKLM\SYSTEM\CurrentControlSet\Services\<name>\ImagePath`, normalises the path (strips `\??\`, expands `\SystemRoot\` and `%SystemRoot%`), and verifies the file exists on disk.  Supply `--driver-binary <path>` to skip this step and provide the binary directly (required for UMDF drivers that do not store `ImagePath` in the standard location).
2. **Extract PDB identity** — opens the binary as a PE file and reads the CodeView RSDS debug directory entry to obtain the embedded PDB filename, GUID, and age.
3. **Resolve the PDB** — checks the local cache (`--symbol-cache`), then downloads from `--symbol-server` (or `_NT_SYMBOL_PATH`) using the standard SymSrv URL path `<server>/<pdbname>/<GUID><AGE>/<pdbname>`.
4. **Feed into capture** — appends the resolved PDB to the `--symbols` list and auto-derives the WPP provider GUIDs from its `WPP_DEFINE_CONTROL_GUID` annotations, exactly as if `--symbols <pdb>` had been passed explicitly.

`--driver` is repeatable: pass it multiple times to capture events from several drivers in one session. Each driver is resolved independently — a failure for one driver logs a warning and skips that driver without aborting the others. The command exits with code 2 only if every driver failed *and* no other provider source (`provider-guid` args or `--symbols`) was supplied.

`--driver-type`, `--symbol-server`, and `--symbol-cache` are single-valued and apply to every named driver. `--driver-binary` is only valid when exactly one `--driver` is given (combining it with multiple drivers exits with code 2).

> [!TIP]  
> Point `--symbol-server` at `https://symbols.nefarius.at/download/symbols` to use the public Nefarius symbol server, which hosts PDBs for BthPS3, HidHide, ViGEm, and other projects. For WinDbg-compatible servers set `_NT_SYMBOL_PATH` and omit the flag entirely.

### `verbose`

Enable or disable WPP verbose tracing for a kernel-mode or UMDF driver service by writing the `VerboseOn` `REG_DWORD` under the driver's registry parameters key.

> [!IMPORTANT]  
> **Admin required.** The `enable` and `disable` actions write to `HKEY_LOCAL_MACHINE` and require an elevated process.

```text
etwutils verbose <service-name> <enable|disable|status> [--type kernel|umdf] [--dry-run]
```

| Argument / Option | Description |
|---|---|
| `enable` | Write `VerboseOn = 1` (REG_DWORD) to the service's registry key |
| `disable` | Delete the `VerboseOn` value (silent no-op when already absent) |
| `status` | Print the current state for both kernel and UMDF candidates; no registry writes |
| `--type kernel\|umdf` | Target a specific driver kind explicitly (see detection rules below) |
| `--dry-run` | Print what would be done without touching the registry; exits 0 |

#### Registry target paths

| Driver kind | `VerboseOn` location |
|---|---|
| Kernel-mode | `HKLM\SYSTEM\CurrentControlSet\Services\<name>\Parameters\VerboseOn` |
| UMDF | `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\WUDF\Services\<name>\VerboseOn` |

#### Service detection rules

The command probes both registry locations independently for the given service name:

- **Kernel candidate** — `HKLM\SYSTEM\CurrentControlSet\Services\<name>\Type` must exist and equal `SERVICE_KERNEL_DRIVER` (1) or `SERVICE_FILE_SYSTEM_DRIVER` (2). Any other `Type` value is not treated as a kernel candidate.
- **UMDF candidate** — the key `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\WUDF\Services\<name>` must exist.

Because a kernel-mode and a UMDF driver can share the same service name on one system, `--type` selects the intended target explicitly. When `--type` is omitted the command prefers the kernel candidate; if only a UMDF candidate is found it falls back and emits a `[*]` warning on stderr. If neither candidate exists the command exits with code 2.

#### Examples

    ```text
    # Enable verbose tracing for the BthPS3 kernel driver
    etwutils verbose BthPS3 enable

    # Check the current state of both kernel and UMDF candidates
    etwutils verbose BthPS3 status

    # Disable verbose tracing
    etwutils verbose BthPS3 disable

    # When both a kernel and a UMDF driver share the same name, target explicitly
    etwutils verbose Foo enable --type umdf
    etwutils verbose Foo enable --type kernel

    # Preview what enable would do without writing anything
    etwutils verbose BthPS3 enable --dry-run
    ```

> [!NOTE]  
> After toggling `VerboseOn`, the change only takes effect once the driver restarts or the device is re-enumerated. A future `etwutils` release will add `--restart-devices` to automate this step.

### `sessions`

List and clean up ETW trace sessions created by `etwutils`. Useful after a run is killed before it can stop its session — orphaned `NefariusEtwCli-<pid>` sessions hold their provider enabled, and once the system or provider limit is reached every new run fails with an error.

> [!IMPORTANT]  
> **Admin required.** The `clean` action calls `ControlTrace(STOP)` and requires an elevated process.

```text
etwutils sessions <list|clean> [--all] [--prefix <name>] [--dry-run]
```

| Argument / Option | Description |
|---|---|
| `list` | Print all running ETW sessions; annotate sessions matching `--prefix` as `alive` or `dead` based on whether the owning process still exists |
| `clean` | Stop sessions matching `--prefix` whose owning process is no longer running |
| `--all` | For `clean`: also stop sessions whose process is still running. **Warning:** may interrupt another live `etwutils realtime` instance |
| `--prefix <name>` | Session name prefix to target (default: `NefariusEtwCli-`) |
| `--dry-run` | Print what would be stopped without stopping anything; exits 0 |

#### Why sessions pile up

`etwutils realtime` creates an ETW session named `NefariusEtwCli-<pid>`. When the process is killed (e.g. closing the terminal) the session is left behind because `ControlTrace(STOP)` never runs. Modern ETW providers can be enabled in up to 8 sessions simultaneously; legacy WPP/MOF providers allow only 1. Once the limit is reached, `EnableTraceEx2` returns `ERROR_NO_SYSTEM_RESOURCES` (0x5AA) and the next run fails immediately.

#### Examples

```text
# List all running ETW sessions (annotates etwutils sessions as alive/dead)
etwutils sessions list

# Remove all dead NefariusEtwCli-<pid> sessions (safe for concurrent runs)
etwutils sessions clean

# Preview what would be removed without touching anything
etwutils sessions clean --dry-run

# Force-stop all NefariusEtwCli- sessions regardless of process state
etwutils sessions clean --all

# Target a custom session name prefix
etwutils sessions list --prefix MyApp-
etwutils sessions clean --prefix MyApp-
```

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

#### Capture by driver service name (zero-config)

Resolve everything automatically from a driver service name — binary path, PDB identity, download, and provider GUIDs:

```bash
# Locate HidHide.sys via its ImagePath registry value, download HidHide.pdb from
# the Nefarius symbol server, derive the WPP provider GUID, and start capturing.
etwutils realtime --driver HidHide \
    --symbol-server https://symbols.nefarius.at/download/symbols | jq .
# stderr:
# [*] _NT_SYMBOL_PATH: (or resolved from --symbol-server)
# [*] --driver: resolved binary: C:\Windows\System32\drivers\HidHide.sys
# [*] --driver: PDB reference: HidHide.pdb (guid=…, age=1)
#     [server] HidHide.pdb <- https://symbols.nefarius.at/download/symbols/hidhide.pdb/…/hidhide.pdb
# [*] --driver: PDB resolved: C:\Users\…\symcache\hidhide.pdb\…\hidhide.pdb
# [*] Session 'NefariusEtwCli-1234' started. Providers (auto-derived from symbols): {…} | level=Verbose | …
# [*] Streaming events... (Ctrl+C to stop)
```

Capture events from multiple drivers in a single session:

```bash
etwutils realtime \
    --driver HidHide \
    --driver BthPS3 \
    --symbol-server https://symbols.nefarius.at/download/symbols | jq .
# Each driver is resolved independently; if one PDB is missing the other still loads.
```

Re-run without network access — the PDB is served from the local cache:

```bash
etwutils realtime --driver HidHide \
    --symbol-server https://symbols.nefarius.at/download/symbols | jq .
# stderr:
#     [cache ] HidHide.pdb
```

Use `_NT_SYMBOL_PATH` so `--symbol-server` can be omitted:

```bash
# _NT_SYMBOL_PATH=srv*C:\symbols*https://symbols.nefarius.at/download/symbols
etwutils realtime --driver HidHide | jq .
```

Target a UMDF driver and supply the binary path explicitly when `ImagePath` is not in the standard registry location:

```bash
etwutils realtime --driver MyUmdfDriver --driver-type umdf \
    --driver-binary "C:\Windows\System32\drivers\UMDF\MyUmdfDriver.dll" \
    --symbol-server https://symbols.nefarius.at/download/symbols | jq .
```

Combine `--driver` with `--format plain` and a filter for human-readable live output:

```bash
etwutils realtime --driver HidHide \
    --symbol-server https://symbols.nefarius.at/download/symbols \
    --format plain --filter "LevelNumber <= 3"
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
    --filter "LevelNumber == 2 || LevelNumber == 3"
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

When stdout is a TTY (and `NO_COLOR` is not set), the `Level` column (when present) is automatically colorized: Critical → bright red, Error → red, Warning → yellow, Information → cyan, Verbose → gray. Level strings containing "Fatal" are also treated as Critical severity. Color is suppressed automatically when piping. Use `--color always|never` to override.

```text
2026-05-26T14:51:23.1234567+02:00	BthPS3TraceGuid	TRACE_LEVEL_INFORMATION	Device arrived: USB\VID_054C&PID_09CC
2026-05-26T14:51:23.5678901+02:00	BthPS3TraceGuid	TRACE_LEVEL_VERBOSE	  ConnectRequest: handle=0x0003
```

#### Filter expression language

`--filter` accepts a C#-like boolean expression evaluated per event by [DynamicExpresso](https://github.com/dynamicexpresso/DynamicExpresso). Events for which the expression returns `false` are silently dropped. Parse errors abort startup with exit code 2; per-event evaluation errors are fatal.

All column tokens listed above are available as identifiers directly in the expression (no prefix needed). In addition, the raw WPP JSON property names are accepted as aliases so you can use either form interchangeably:

| Raw WPP name | Alias for |
|---|---|
| `GuidName` | `Provider` |
| `LevelName` | `Level` |
| `FormattedString` | `Message` |
| `FunctionName` | `Function` |
| `ComponentName` | `Component` |
| `SubComponentName` | `SubComponent` |
| `FlagsName` | `Flags` |

```text
# Skip all events from a specific provider (column token or WPP alias both work)
--filter "Provider != \"BthPS3PSM\""
--filter "GuidName != \"BthPS3PSM\""

# Only show errors and warnings (Error=2, Warning=3)
--filter "LevelNumber == 2 || LevelNumber == 3"

# Keep only events whose message starts with a specific string
--filter "Message.StartsWith(\"[BthPS3\")"
--filter "FormattedString.StartsWith(\"[BthPS3\")"

# Filter by WPP function name
--filter "FunctionName.StartsWith(\"EvtUdecx\")"

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

## Credits

| Package | Author / Maintainer | License | Role |
|---|---|---|---|
| [DynamicExpresso.Core](https://github.com/dynamicexpresso/DynamicExpresso) | Davide Icardi | MIT | Powers the `--filter` predicate engine |
| [System.CommandLine](https://github.com/dotnet/command-line-api) | .NET Foundation | MIT | CLI argument parsing, help generation, and tab-completion plumbing |
| [Smx.PDBSharp](https://github.com/smx-smx/PDBSharp) | smx-smx | MPL-2.0 | PDB file parsing used by `inspect-pdb` and WPP symbol resolution |
| [MinVer](https://github.com/adamralph/minver) | Adam Ralph | Apache-2.0 | Build-time Git-tag-based version stamping (not shipped at runtime) |
