# Example stack: relay, decoding gateway and Jaeger

```text
sender (no symbols) --OTLP Logs, raw relay frames--> gateway --OTLP traces--> Jaeger
```

* `sender` is a stand-in for a native capture client. It produces raw relay frames (here synthetic TraceLogging events) and posts them as OTLP Logs. It holds no PDB/TMF files.
* `gateway` is [`tools/Nefarius.Utilities.ETW.OtlpGateway`](../../tools/Nefarius.Utilities.ETW.OtlpGateway). It decodes the frames on the server.
* [Jaeger](https://www.jaegertracing.io/) v2 stores and shows the result.

Jaeger stores **traces only** and cannot show logs, so the gateway runs with `Gateway:Export:TracesEndpoint` set: every decoded event is also exported as an instantaneous span. All events of one tenant and relay session form one trace (the trace ID is derived from both, so two tenants do not share a trace), event properties become `etw.property.*` attributes, WPP messages become `etw.message`, and events at error level or above get an error status. Use `Gateway:Export:Endpoint` as well (or instead) to send the decoded events as OTLP Logs to a logs backend such as an OpenTelemetry Collector with Loki.

## Run

```text
docker compose -f examples/jaeger/docker-compose.yml up --build
```

Open <http://localhost:16686>, choose the service `relay-sample-sender` and find the trace. A new trace appears each time the sender starts a session (on start, and after the gateway rejects it, for example after a gateway restart).

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

* The Jaeger image is not pinned; pin a version for anything beyond a demo.
* The gateway and sender build from the repository root, see `gateway.Dockerfile` and `sender.Dockerfile`.
* The stack was verified by running the gateway and sender natively against a stand-in OTLP receiver; the container build and Jaeger UI are not covered by the automated tests.