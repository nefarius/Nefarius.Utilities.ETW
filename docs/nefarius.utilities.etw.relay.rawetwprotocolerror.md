# RawEtwProtocolError

Namespace: Nefarius.Utilities.ETW.Relay

The reason a relay frame or stream was rejected.

```csharp
public enum RawEtwProtocolError
```

Inheritance [Object](https://learn.microsoft.com/dotnet/api/system.object) → [ValueType](https://learn.microsoft.com/dotnet/api/system.valuetype) → [Enum](https://learn.microsoft.com/dotnet/api/system.enum) → [RawEtwProtocolError](./nefarius.utilities.etw.relay.rawetwprotocolerror.md)<br>
Implements [IComparable](https://learn.microsoft.com/dotnet/api/system.icomparable), [ISpanFormattable](https://learn.microsoft.com/dotnet/api/system.ispanformattable), [IFormattable](https://learn.microsoft.com/dotnet/api/system.iformattable), [IConvertible](https://learn.microsoft.com/dotnet/api/system.iconvertible)

## Fields

| Name | Value | Description |
| --- | --: | --- |
| Truncated | 0 | The frame is shorter than its declared or minimum length, or the stream ended inside a frame. |
| BadMagic | 1 | The frame does not start with the relay magic. |
| UnsupportedVersion | 2 | The frame has a major version this implementation does not support. |
| InvalidLength | 3 | A length field is inconsistent with the frame, or exceeds a configured limit. |
| InvalidField | 4 | A reserved field is non-zero or a field holds a value the version does not define. |
| ChecksumMismatch | 5 | The CRC32C does not match, or is required and absent. |
| HelloRequired | 6 | The first frame of a session was not a hello frame. |
| UnexpectedHello | 7 | A hello frame arrived after the session was established. |
| OutOfOrder | 8 | A frame skipped one or more sequence numbers. |
| ConflictingReplay | 9 | A frame repeated a sequence number with different content, or one too old to verify. |
| AfterEnd | 10 | A frame arrived after the end frame. |
| ProducerLimitExceeded | 11 | The producer announced a maximum frame length above the receiver's limit. |
| DecodeFailed | 12 | The frame was well-formed but its event could not be decoded. The frame is consumed, so the session can continue with the next one. |
