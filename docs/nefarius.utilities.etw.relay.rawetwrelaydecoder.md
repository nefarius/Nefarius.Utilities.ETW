# RawEtwRelayDecoder

Namespace: Nefarius.Utilities.ETW.Relay

Decodes a stream of raw ETW relay frames into structured [EtwEvent](./nefarius.utilities.etw.events.etwevent.md) objects, entirely on the
 receiving side.

```csharp
public sealed class RawEtwRelayDecoder : System.IDisposable
```

Inheritance [Object](https://learn.microsoft.com/dotnet/api/system.object) → [RawEtwRelayDecoder](./nefarius.utilities.etw.relay.rawetwrelaydecoder.md)<br>
Implements [IDisposable](https://learn.microsoft.com/dotnet/api/system.idisposable)<br>
Attributes [NullableContextAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullablecontextattribute), [NullableAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullableattribute)

**Remarks:**

The producer only copies `EVENT_RECORD`s; this class resolves everything that needs symbols or
 formats (WPP PDB/TMF files via [EtwJsonConverterOptions.WppDecodingContext](./nefarius.utilities.etw.etwjsonconverteroptions.md#wppdecodingcontext), custom manifests
 via [EtwJsonConverterOptions.CustomProviderManifest](./nefarius.utilities.etw.etwjsonconverteroptions.md#customprovidermanifest)), so none of it has to exist on the
 capturing machine.

An instance represents one capture session and keeps parser and manifest state between frames, so frames
 must be passed in sequence order and an instance must not be shared between sessions. Members are
 serialized internally, but ordering across threads is the caller's responsibility.

Relayed records are decoded without TDH. Manifest-based events whose manifest is not supplied through
 [EtwJsonConverterOptions.CustomProviderManifest](./nefarius.utilities.etw.etwjsonconverteroptions.md#customprovidermanifest) or embedded in the stream (EventSource
 manifests, TraceLogging metadata) are reported as unknown events.

## Properties

### <a id="properties-ended"/>**Ended**

True once the producer's end frame was accepted.

```csharp
public bool Ended { get; }
```

#### Property Value

[Boolean](https://learn.microsoft.com/dotnet/api/system.boolean)<br>

### <a id="properties-lostevents"/>**LostEvents**

The number of events the producer reported as dropped.

```csharp
public ulong LostEvents { get; }
```

#### Property Value

[UInt64](https://learn.microsoft.com/dotnet/api/system.uint64)<br>

### <a id="properties-nextsequence"/>**NextSequence**

The sequence number the decoder expects next.

```csharp
public ulong NextSequence { get; }
```

#### Property Value

[UInt64](https://learn.microsoft.com/dotnet/api/system.uint64)<br>

### <a id="properties-sessionid"/>**SessionId**

The session identifier announced by the hello frame, or `null` before it.

```csharp
public Nullable<Guid> SessionId { get; }
```

#### Property Value

[Nullable](https://learn.microsoft.com/dotnet/api/system.nullable-1)<[Guid](https://learn.microsoft.com/dotnet/api/system.guid)><br>

## Constructors

### <a id="constructors-.ctor"/>**RawEtwRelayDecoder(Action&lt;EtwJsonConverterOptions&gt;, RawEtwRelayOptions)**

Creates a decoder for one capture session.

```csharp
public RawEtwRelayDecoder(Action<EtwJsonConverterOptions> decodingOptions, RawEtwRelayOptions relayOptions)
```

#### Parameters

`decodingOptions` [Action](https://learn.microsoft.com/dotnet/api/system.action-1)<[EtwJsonConverterOptions](./nefarius.utilities.etw.etwjsonconverteroptions.md)><br>
Decoding options, notably the server-side WPP context and manifests.

`relayOptions` [RawEtwRelayOptions](./nefarius.utilities.etw.relay.rawetwrelayoptions.md)<br>
Frame limits and notifications.

## Methods

### <a id="methods-decodeasync"/>**DecodeAsync(Stream, CancellationToken)**

Reads frames from `input` and yields the decoded events as they arrive.

```csharp
public IAsyncEnumerable<EtwEvent> DecodeAsync(Stream input, CancellationToken cancellationToken)
```

#### Parameters

`input` [Stream](https://learn.microsoft.com/dotnet/api/system.io.stream)<br>
A stream of back-to-back frames, e.g. an HTTP request body.

`cancellationToken` [CancellationToken](https://learn.microsoft.com/dotnet/api/system.threading.cancellationtoken)<br>
Cancels the read.

#### Returns

[IAsyncEnumerable](https://learn.microsoft.com/dotnet/api/system.collections.generic.iasyncenumerable-1)<[EtwEvent](./nefarius.utilities.etw.events.etwevent.md)>

#### Exceptions

[RawEtwProtocolException](./nefarius.utilities.etw.relay.rawetwprotocolexception.md)<br>
The input violates the protocol.

**Remarks:**

Enumeration ends after the end frame or when the stream ends; check [RawEtwRelayDecoder.Ended](./nefarius.utilities.etw.relay.rawetwrelaydecoder.md#ended) to tell the two
 apart. The stream is read only as fast as the consumer enumerates, which applies backpressure to the sender.

### <a id="methods-dispose"/>**Dispose()**

```csharp
public void Dispose()
```

### <a id="methods-process"/>**Process(ReadOnlySpan&lt;Byte&gt;, Action&lt;EtwEvent&gt;)**

Processes one frame and reports the events it decoded.

```csharp
public RawEtwFrameDisposition Process(ReadOnlySpan<Byte> frame, Action<EtwEvent> onEvent)
```

#### Parameters

`frame` [ReadOnlySpan](https://learn.microsoft.com/dotnet/api/system.readonlyspan-1)<[Byte](https://learn.microsoft.com/dotnet/api/system.byte)><br>
The complete frame bytes, e.g. one OTLP log record body.

`onEvent` [Action](https://learn.microsoft.com/dotnet/api/system.action-1)<[EtwEvent](./nefarius.utilities.etw.events.etwevent.md)><br>
Called for each decoded event, before this method returns.

#### Returns

What happened to the frame.

#### Exceptions

[RawEtwProtocolException](./nefarius.utilities.etw.relay.rawetwprotocolexception.md)<br>
The frame violates the protocol, the limits or the sequencing.

### <a id="methods-process"/>**Process(RawEtwFrame, Action&lt;EtwEvent&gt;)**

Processes one frame object by serializing it first. Prefer the span overload when the wire bytes exist.

```csharp
public RawEtwFrameDisposition Process(RawEtwFrame frame, Action<EtwEvent> onEvent)
```

#### Parameters

`frame` [RawEtwFrame](./nefarius.utilities.etw.relay.rawetwframe.md)<br>

`onEvent` [Action](https://learn.microsoft.com/dotnet/api/system.action-1)<[EtwEvent](./nefarius.utilities.etw.events.etwevent.md)><br>

#### Returns

[RawEtwFrameDisposition](./nefarius.utilities.etw.relay.rawetwframedisposition.md)
