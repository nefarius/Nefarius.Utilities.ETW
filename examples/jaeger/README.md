# Example stack: relay, decoding gateway, Aspire and Jaeger

```text
sender (no symbols) --OTLP Logs, raw relay frames--> gateway --OTLP Logs--> Aspire Dashboard
                                                           \--OTLP traces--> Jaeger
```

* `sender` is a stand-in for a native capture client. It produces raw relay frames (here synthetic TraceLogging events) and posts them as OTLP Logs. It holds no PDB/TMF files.
* `gateway` is [`tools/Nefarius.Utilities.ETW.OtlpGateway`](../../tools/Nefarius.Utilities.ETW.OtlpGateway). It decodes the frames on the server.
* The [Aspire Dashboard](https://aspire.dev/dashboard/standalone/) shows those decoded events as native OTLP Logs.
* [Jaeger](https://www.jaegertracing.io/) v2 stores traces only, so it shows the same events as spans.

The gateway exports each decoded record twice. `Gateway:Export:Endpoint` sends it as an OTLP log to the Aspire Dashboard, where the event name, severity, structured body and `etw.*` attributes are visible. `Gateway:Export:TracesEndpoint` also exports it as an instantaneous span so Jaeger can show it. All events of one tenant and relay session form one trace (the trace ID is derived from both, so two tenants do not share a trace), event properties become `etw.property.*` attributes, WPP messages become `etw.message`, and events at error level or above get an error status. Point `Gateway:Export:Endpoint` at another logs backend, such as an OpenTelemetry Collector with Loki, when you want a different viewer.

## Run

```text
docker compose -f examples/jaeger/docker-compose.yml up --build
```

Open <http://localhost:18888> for the decoded logs. Choose the resource `relay-sample-sender` and inspect its structured log records. This example sets `ASPIRE_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS`, so the dashboard accepts anonymous access and needs no login token. That is for a trusted local machine only: the UI is published on loopback, and anyone who can reach the dashboard can read the telemetry it holds and send telemetry to its OTLP port.

Open <http://localhost:16686> for the span view, choose the service `relay-sample-sender` and find the trace. A new trace appears each time the sender starts a session (on start, and after the gateway rejects it, for example after a gateway restart).

The gateway is also reachable from the host on `http://localhost:4318/v1/logs` if you want to point your own relay client at it.

## Decoding WPP

Put `.pdb` or `.tmf` files in `examples/jaeger/wpp`, uncomment the `volumes` mount of the `gateway` service and send raw WPP records. The sample sender only emits TraceLogging events; a real client copies `EVENT_RECORD`s as described in [the relay protocol](../../docs/guides/raw-etw-relay.md).

## Downloading symbols from a symbol server

Set `SYMBOL_SERVER_URL` to let the gateway fetch PDBs by itself, then start the stack:

```text
SYMBOL_SERVER_URL=https://msdl.microsoft.com/download/symbols docker compose -f examples/jaeger/docker-compose.yml up --build
```

(PowerShell: `$env:SYMBOL_SERVER_URL = "..."` first.) Downloads land in the `symbols` volume and survive restarts; leave the variable empty to disable discovery. The gateway discovers what to download from the `DbgID_RSDS` image events in the relayed capture, so send a real capture: `etwutils` can relay a trace or a live session including those events, see [the CLI guide](../../docs/guides/opentelemetry-gateway.md#testing-the-gateway-from-the-command-line). The synthetic TraceLogging sender needs no symbols.

Discovery is best effort: events that arrive before their symbols are loaded keep their placeholder text and are not replayed. See [Symbol server discovery](../../docs/guides/opentelemetry-gateway.md#symbol-server-discovery) for the security boundaries.

## Notes

* The Aspire Dashboard and Jaeger images are not pinned; pin a version for anything beyond a demo.
* Anonymous dashboard access is for this local demo. Leave it enabled only on a trusted machine, and keep the UI on loopback.
* The gateway and sender build from the repository root, see `gateway.Dockerfile` and `sender.Dockerfile`.
* The stack was verified by running the gateway and sender natively against a stand-in OTLP receiver; the container build, Aspire Dashboard and Jaeger UI are not covered by the automated tests.
