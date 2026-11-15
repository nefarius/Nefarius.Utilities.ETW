# Example stack: relay, decoding gateway and Jaeger

```text
sender (no symbols) --OTLP Logs, raw relay frames--> gateway --OTLP traces--> Jaeger
```

* `sender` is a stand-in for a native capture client. It produces raw relay frames (here synthetic TraceLogging events) and posts them as OTLP Logs. It holds no PDB/TMF files.
* `gateway` is [`tools/Nefarius.Utilities.ETW.OtlpGateway`](../../tools/Nefarius.Utilities.ETW.OtlpGateway). It decodes the frames on the server.
* [Jaeger](https://www.jaegertracing.io/) v2 stores and shows the result.

Jaeger stores **traces only** and cannot show logs, so the gateway runs with `Gateway:Export:TracesEndpoint` set: every decoded event is also exported as an instantaneous span. All events of one relay session form one trace (the trace ID is the session GUID), event properties become `etw.property.*` attributes, WPP messages become `etw.message`, and events at error level or above get an error status. Use `Gateway:Export:Endpoint` as well (or instead) to send the decoded events as OTLP Logs to a logs backend such as an OpenTelemetry Collector with Loki.

## Run

```text
docker compose -f examples/jaeger/docker-compose.yml up --build
```

Open <http://localhost:16686>, choose the service `relay-sample-sender` and find the trace. A new trace appears each time the sender starts a session (on start, and after the gateway rejects it, for example after a gateway restart).

The gateway is also reachable from the host on `http://localhost:4318/v1/logs` if you want to point your own relay client at it.

## Decoding WPP

Put `.pdb` or `.tmf` files in `examples/jaeger/wpp`, uncomment the `volumes` mount of the `gateway` service and send raw WPP records. The sample sender only emits TraceLogging events; a real client copies `EVENT_RECORD`s as described in [the relay protocol](../../docs/guides/raw-etw-relay.md).

## Notes

* The Jaeger image is not pinned; pin a version for anything beyond a demo.
* The gateway and sender build from the repository root, see `gateway.Dockerfile` and `sender.Dockerfile`.
* The stack was verified by running the gateway and sender natively against a stand-in OTLP receiver; the container build and Jaeger UI are not covered by the automated tests.