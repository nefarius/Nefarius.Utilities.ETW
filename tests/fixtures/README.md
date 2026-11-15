# ETL fixtures

`New-EtlFixtures.ps1` regenerates the ETL files in `tests/traces/fixtures` (stored in Git LFS). It needs an elevated
Windows session, Windows PowerShell 5.1 and PowerShell 7. The `producers` folder holds the deterministic event
producers; all ETW sessions are created with `StartTrace`/`EnableTraceEx2` so the logger mode is fully controlled.

| File | Purpose |
| --- | --- |
| `tracelogging-x64.etl` / `tracelogging-x86.etl` | Self-describing TraceLogging events from a 64-bit and a 32-bit producer: all scalar types, arrays, nested structures, a sentinel field after each complex field |
| `manifest-x64.etl` | Manifest-based EventSource events including an enum value map and a `[Flags]` bit map, with the manifest delivered in-band |
| `bulk-uncompressed.etl` / `bulk-compressed.etl` | The same 400-event workload spanning several logger buffers, without and with `EVENT_TRACE_COMPRESSED_MODE` |
| `kernel-classic.etl` | Classic kernel process events (SYSTEM_TRACE headers incl. compact type 4), first 1 MiB of the capture |

`fixtures.manifest.txt` records size and SHA-256 of the committed files. Regenerated files differ bit-wise (timestamps,
process ids), so after regenerating update the expected counts in `UnitTestEtlFixtures.cs`.

## Known differences found with these fixtures

* Managed reader: compressed files are rejected; compact record header type 4 is skipped (3 kernel events).
* TraceLogging/manifest decoding differs between the native and managed backends (event names, binary array counts,
  nested structures, manifest binary fields); the parity cases for the new fixtures are ignored until fixed.
* `BackendParityTests` compares SYSTEMTIME values in local time on Windows and therefore only passes in a UTC time zone.
