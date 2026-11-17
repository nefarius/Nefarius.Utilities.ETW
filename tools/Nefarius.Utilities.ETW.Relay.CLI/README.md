# etwrelay — Nefarius.Utilities.ETW.Relay.CLI

[![.NET](https://github.com/nefarius/Nefarius.Utilities.ETW/actions/workflows/build.yml/badge.svg)](https://github.com/nefarius/Nefarius.Utilities.ETW/actions/workflows/build.yml)
![Requirements](https://img.shields.io/badge/Requires-.NET%208%2F9%2F10-blue.svg)
[![NuGet Version](https://img.shields.io/nuget/v/Nefarius.Utilities.ETW.Relay.CLI)](https://www.nuget.org/packages/Nefarius.Utilities.ETW.Relay.CLI/)

An **optional** companion to [`etwutils`](../Nefarius.Utilities.ETW.CLI/README.md). It reads raw ETW relay frames and delivers them to an OpenTelemetry decoding gateway as OTLP/HTTP logs. Use it to try the [decoding gateway](../../docs/guides/opentelemetry-gateway.md) from the command line, or to ship captures from a machine that has no symbols.

`etwutils` keeps all capture logic and carries no protobuf or OTLP dependencies; those live here, so you install `etwrelay` only if you need it.

```text
etwutils realtime --format relay <provider-guid> | etwrelay --endpoint https://gateway.example.com
 capture, no symbols needed                          batches, retries, OTLP/HTTP
```

## Installation

```text
dotnet tool install -g Nefarius.Utilities.ETW.Relay.CLI
```

Requires the .NET 8, 9, or 10 **SDK**. `etwrelay` itself runs on Windows, Linux and macOS; capturing with `etwutils` needs Windows and an elevated shell.

## Piping from `etwutils`

Frames are binary, so the pipe must not treat them as text.

| Shell | Pipeline |
|-------|----------|
| PowerShell **7.4+**, `cmd.exe`, bash, zsh | `etwutils realtime --format relay <guid> \| etwrelay` |
| PowerShell 7.0–7.3, Windows PowerShell 5.1 | `etwutils realtime --format relay-ndjson <guid> \| etwrelay --input-format base64-ndjson` |

PowerShell 7.4 passes bytes unchanged between **native commands** (`PSNativeCommandPreserveBytePipe`). Placing a cmdlet such as `Where-Object` or `Out-File` in the middle turns the stream back into text and corrupts it. Older PowerShell versions always decode a native command's output as text, which is why `--format relay-ndjson` exists: each frame becomes one `{"frame":"<base64>"}` line that survives any text pipeline. Neither side guesses the format, and `etwutils` refuses to write binary frames to a terminal.

If `etwrelay` reports an invalid first frame it explains this and names the two fixes.

Press **Ctrl+C once** to stop: `etwutils` ends the session cleanly with an end frame and `etwrelay` keeps delivering until its input closes (at most 10 seconds). Press it again to stop immediately; frames still queued then count as a delivery failure (exit code 1).

You can also relay a recording: `etwrelay --input frames.bin`.

## Options

```text
etwrelay [--endpoint <url>]            # default http://localhost:4318; '/v1/logs' is appended
         [--input-format <binary|base64-ndjson>]   # default binary; never auto-detected
         [--input <file>]              # instead of standard input
         [--header name=value] ...     # repeatable
         [--resource name=value] ...   # OTLP resource attribute, e.g. service.name=lab-pc
         [--gzip]                      # compress request bodies
         [--batch-frames <1-64>]       # default 64
         [--batch-bytes <n>]           # default 1 MiB
         [--flush-ms <n>]              # default 250
         [--heartbeat-seconds <0-86400>] # default 300; 0 disables; keeps an open session from going idle
         [--queue-frames <n>]          # default 1024; a full queue slows the producer down
         [--timeout-seconds <n>]       # per request, default 30
         [--max-retries <n>]           # default 8
         [--max-frame-bytes <n>]       # default 1 MiB
```

Frames of one session are sent in order, in requests of at most 64 frames: that is the length of the gateway's duplicate-retry window. Sessions are never mixed in a request, and a request is flushed at the end frame, when a new hello frame starts another session, when it reaches the frame or byte limit, or after `--flush-ms`.

## Idle sessions

The gateway discards a session after `Gateway:Limits:SessionIdleTimeout` without a request (15 minutes by default). A quiet capture would otherwise lose its session and be rejected when the next event arrived. While a session is open and no new batch has been acknowledged, `etwrelay` replays the latest acknowledged frames after `--heartbeat-seconds` (default 5 minutes; `0` disables it). Set that interval below the gateway's idle timeout. An interval equal to or greater than the timeout can let the session expire before the first heartbeat. A batch with no hello is resent unchanged, so the gateway recognizes those frames as duplicates, refreshes the session, and does not decode or export them again. Every hello is omitted from the replay: after a gateway restart it would open a session and the events behind it would be exported a second time. The frames that remain are what a live session accepts as duplicates and what a restarted gateway rejects. A batch that contains only a hello has nothing safe to replay. A batch still waiting for `--flush-ms` is sent when the heartbeat comes due, which refreshes the session and starts the interval again. Heartbeats count as requests and stop at an end frame, when the input closes, or when another session starts. A rejected replay is fatal and the capture has to start again.

## Retries and failure

A request is serialized once and sent unchanged on every attempt, so a retry is byte-identical and the gateway can recognise it as a duplicate instead of decoding it twice.

| Outcome | Behavior |
|---------|----------|
| Connection failure, timeout, `408`, `429`, `5xx` | retried with exponential backoff (250 ms up to 10 s), honoring `Retry-After` (capped at 60 s), up to `--max-retries` times |
| Any other `4xx` | fatal, not retried |
| `2xx` with `partial_success.rejected_log_records > 0`, e.g. an unknown session | fatal, not retried |
| Input ended without an end frame | warning; what was read is delivered |

| Exit code | Meaning |
|-----------|---------|
| 0 | everything read was delivered |
| 1 | delivery failed (rejected by the gateway or retries exhausted) |
| 2 | invalid usage or invalid input |

Diagnostics go to standard error only; standard output stays empty.

## Security

- Raw frames carry event payloads exactly as captured and may contain sensitive data. Use `https://` endpoints outside a trusted network.
- Credential-like headers (`Authorization`, `Proxy-Authorization`, `Cookie`, and names containing `api-key`, `apikey`, `token`, `secret` or `password`) are **refused** for `http://` endpoints. Credentials in the URL are refused as well, including any query string on an `http://` endpoint. Redirects are not followed, so a credential header cannot be sent on to another host.
- Input is bounded before anything is allocated: a frame's declared length is checked against `--max-frame-bytes`, and a Base64 line is rejected as soon as it exceeds what such a frame needs.
- The relay session id is taken from the hello frame; `--resource etw.relay.session_id=…` is rejected.

See [Raw ETW relay](../../docs/guides/raw-etw-relay.md) for the wire format and [OpenTelemetry integration and decoding gateway](../../docs/guides/opentelemetry-gateway.md) for the receiving side.
