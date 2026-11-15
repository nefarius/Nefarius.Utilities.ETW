# RawEtwFrameDisposition

Namespace: Nefarius.Utilities.ETW.Relay

What happened to a frame handed to a relay decoder.

```csharp
public enum RawEtwFrameDisposition
```

Inheritance [Object](https://learn.microsoft.com/dotnet/api/system.object) → [ValueType](https://learn.microsoft.com/dotnet/api/system.valuetype) → [Enum](https://learn.microsoft.com/dotnet/api/system.enum) → [RawEtwFrameDisposition](./nefarius.utilities.etw.relay.rawetwframedisposition.md)<br>
Implements [IComparable](https://learn.microsoft.com/dotnet/api/system.icomparable), [ISpanFormattable](https://learn.microsoft.com/dotnet/api/system.ispanformattable), [IFormattable](https://learn.microsoft.com/dotnet/api/system.iformattable), [IConvertible](https://learn.microsoft.com/dotnet/api/system.iconvertible)

## Fields

| Name | Value | Description |
| --- | --: | --- |
| Hello | 0 | The hello frame was accepted. |
| Event | 1 | An event frame was decoded. The decoder may have produced zero events, e.g. for metadata records. |
| Loss | 2 | A loss report was recorded. |
| End | 3 | The end frame was accepted; no further frames are valid. |
| Ignored | 4 | The frame of an unknown type was skipped. |
| Duplicate | 5 | The frame exactly repeated one already processed (a retry) and was dropped. |
