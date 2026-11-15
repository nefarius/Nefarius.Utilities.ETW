# RawEtwFrameCodec

Namespace: Nefarius.Utilities.ETW.Relay

Encodes and decodes the raw ETW relay wire format (version 1).

```csharp
public static class RawEtwFrameCodec
```

Inheritance [Object](https://learn.microsoft.com/dotnet/api/system.object) → [RawEtwFrameCodec](./nefarius.utilities.etw.relay.rawetwframecodec.md)

**Remarks:**

All integers are little-endian. GUIDs are stored in RFC 4122 byte order, never as the in-memory Windows
 layout. Every frame is a multiple of 8 bytes long and starts with a 32-byte prefix:

```csharp
0   4  magic "NER1"
4   1  major version (1)
5   1  minor version (0)
6   2  frame type
8   4  total frame length, prefix included
12  4  frame flags (0 in version 1)
16  8  producer sequence number
24  2  length of the type-specific header that follows the prefix
26  2  number of extended data items (event frames only)
28  4  CRC32C of the whole frame computed with this field zeroed, or 0 when absent
```

See the repository README for the type-specific layouts.

## Fields

### <a id="fields-eventheaderlength"/>**EventHeaderLength**

Length of the event frame header in bytes.

```csharp
public static int EventHeaderLength;
```

### <a id="fields-helloheaderlength"/>**HelloHeaderLength**

Length of the hello frame header in bytes.

```csharp
public static int HelloHeaderLength;
```

### <a id="fields-lossheaderlength"/>**LossHeaderLength**

Length of the loss frame header in bytes.

```csharp
public static int LossHeaderLength;
```

### <a id="fields-majorversion"/>**MajorVersion**

The supported major protocol version.

```csharp
public static byte MajorVersion;
```

### <a id="fields-minorversion"/>**MinorVersion**

The minor protocol version written by this implementation.

```csharp
public static byte MinorVersion;
```

### <a id="fields-prefixlength"/>**PrefixLength**

Length of the frame prefix in bytes.

```csharp
public static int PrefixLength;
```

## Methods

### <a id="methods-decode"/>**Decode(ReadOnlySpan&lt;Byte&gt;, RawEtwRelayLimits)**

Decodes and validates one complete frame.

```csharp
public static RawEtwFrame Decode(ReadOnlySpan<Byte> frame, RawEtwRelayLimits limits)
```

#### Parameters

`frame` [ReadOnlySpan](https://learn.microsoft.com/dotnet/api/system.readonlyspan-1)<[Byte](https://learn.microsoft.com/dotnet/api/system.byte)><br>
Exactly one frame.

`limits` [RawEtwRelayLimits](./nefarius.utilities.etw.relay.rawetwrelaylimits.md)<br>
The limits to enforce; [RawEtwRelayLimits.Default](./nefarius.utilities.etw.relay.rawetwrelaylimits.md#default) when `null`.

#### Returns

[RawEtwFrame](./nefarius.utilities.etw.relay.rawetwframe.md)

#### Exceptions

[RawEtwProtocolException](./nefarius.utilities.etw.relay.rawetwprotocolexception.md)<br>
The frame is malformed or exceeds a limit.

### <a id="methods-encode"/>**Encode(RawEtwFrame, Boolean)**

Serializes a frame.

```csharp
public static Byte[] Encode(RawEtwFrame frame, bool withCrc)
```

#### Parameters

`frame` [RawEtwFrame](./nefarius.utilities.etw.relay.rawetwframe.md)<br>
The frame to serialize.

`withCrc` [Boolean](https://learn.microsoft.com/dotnet/api/system.boolean)<br>
Whether to include a CRC32C.

#### Returns

The complete wire representation.

### <a id="methods-readframeasync"/>**ReadFrameAsync(Stream, RawEtwRelayLimits, CancellationToken)**

Reads the next frame from a stream.

```csharp
public static ValueTask<RawEtwFrame> ReadFrameAsync(Stream stream, RawEtwRelayLimits limits, CancellationToken cancellationToken)
```

#### Parameters

`stream` [Stream](https://learn.microsoft.com/dotnet/api/system.io.stream)<br>
The stream to read from.

`limits` [RawEtwRelayLimits](./nefarius.utilities.etw.relay.rawetwrelaylimits.md)<br>
The limits to enforce; [RawEtwRelayLimits.Default](./nefarius.utilities.etw.relay.rawetwrelaylimits.md#default) when `null`.

`cancellationToken` [CancellationToken](https://learn.microsoft.com/dotnet/api/system.threading.cancellationtoken)<br>
Cancels the read.

#### Returns

The frame, or `null` when the stream ended cleanly between frames.

#### Exceptions

[RawEtwProtocolException](./nefarius.utilities.etw.relay.rawetwprotocolexception.md)<br>
The stream ended inside a frame or the frame is malformed.

### <a id="methods-readframebytesasync"/>**ReadFrameBytesAsync(Stream, RawEtwRelayLimits, CancellationToken)**

Reads the bytes of the next frame without decoding its content. The length is validated against
 `limits` before the buffer is allocated.

```csharp
public static ValueTask<Byte[]> ReadFrameBytesAsync(Stream stream, RawEtwRelayLimits limits, CancellationToken cancellationToken)
```

#### Parameters

`stream` [Stream](https://learn.microsoft.com/dotnet/api/system.io.stream)<br>

`limits` [RawEtwRelayLimits](./nefarius.utilities.etw.relay.rawetwrelaylimits.md)<br>

`cancellationToken` [CancellationToken](https://learn.microsoft.com/dotnet/api/system.threading.cancellationtoken)<br>

#### Returns

The frame bytes, or `null` when the stream ended cleanly between frames.
