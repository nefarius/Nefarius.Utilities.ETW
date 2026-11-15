# RawEtwTimestampKind

Namespace: Nefarius.Utilities.ETW.Relay

How [RawEtwRecord.TimeStamp](./nefarius.utilities.etw.relay.rawetwrecord.md#timestamp) is expressed.

```csharp
public enum RawEtwTimestampKind
```

Inheritance [Object](https://learn.microsoft.com/dotnet/api/system.object) → [ValueType](https://learn.microsoft.com/dotnet/api/system.valuetype) → [Enum](https://learn.microsoft.com/dotnet/api/system.enum) → [RawEtwTimestampKind](./nefarius.utilities.etw.relay.rawetwtimestampkind.md)<br>
Implements [IComparable](https://learn.microsoft.com/dotnet/api/system.icomparable), [ISpanFormattable](https://learn.microsoft.com/dotnet/api/system.ispanformattable), [IFormattable](https://learn.microsoft.com/dotnet/api/system.iformattable), [IConvertible](https://learn.microsoft.com/dotnet/api/system.iconvertible)

## Fields

| Name | Value | Description |
| --- | --: | --- |
| Unknown | 0 | Not specified. Rejected by version 1 receivers. |
| FileTime | 1 | 100-nanosecond intervals since 1601-01-01 UTC, the `ProcessTrace` default. |
