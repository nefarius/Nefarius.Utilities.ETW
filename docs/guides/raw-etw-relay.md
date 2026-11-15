# Raw ETW relay

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

## Wire format, version 1

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

Golden vectors live in `../../tests/UnitTestRawEtwRelay.cs` (`GoldenEventFrameHex`, `GoldenHelloFrameHex`) and are stable for version 1.

## Producing frames from C or C++

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

## Limits and trust model

The sender is untrusted. All lengths are validated against `RawEtwRelayLimits` (frame length, extended item count and size) **before** anything is allocated, records are rebuilt in private memory with no sender pointers, and relayed records are never handed to TDH (a manifest event therefore needs a custom manifest registered with the decoder). Malformed input throws `RawEtwProtocolException` and leaves an established session intact.

## Hosting contract

The library decodes; **you** own everything around it. The stream passed in must already be authenticated and authorized, and the host is responsible for TLS, identity, authorization, replay protection across sessions, quotas and rate limits, and any SSE framing of the output. PDB/TMF files stay on the server. Copy pooled output buffers before advancing the enumerator.


See also: [OpenTelemetry integration and decoding gateway](opentelemetry-gateway.md).
