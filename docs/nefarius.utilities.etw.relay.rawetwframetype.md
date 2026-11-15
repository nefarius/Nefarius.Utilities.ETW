# RawEtwFrameType

Namespace: Nefarius.Utilities.ETW.Relay

The kind of a relay frame.

```csharp
public enum RawEtwFrameType
```

Inheritance [Object](https://learn.microsoft.com/dotnet/api/system.object) → [ValueType](https://learn.microsoft.com/dotnet/api/system.valuetype) → [Enum](https://learn.microsoft.com/dotnet/api/system.enum) → [RawEtwFrameType](./nefarius.utilities.etw.relay.rawetwframetype.md)<br>
Implements [IComparable](https://learn.microsoft.com/dotnet/api/system.icomparable), [ISpanFormattable](https://learn.microsoft.com/dotnet/api/system.ispanformattable), [IFormattable](https://learn.microsoft.com/dotnet/api/system.iformattable), [IConvertible](https://learn.microsoft.com/dotnet/api/system.iconvertible)

**Remarks:**

Receivers ignore (but still sequence) frame types they do not know, so minor protocol versions can add types.
 Type 2 is reserved.

## Fields

| Name | Value | Description |
| --- | --: | --- |
| Hello | 0 | Opens a session. Must be the first frame, with sequence number 0. |
| Event | 1 | One captured ETW event record. |
| Loss | 3 | Reports events the producer had to drop. |
| End | 4 | Orderly end of the session. |
