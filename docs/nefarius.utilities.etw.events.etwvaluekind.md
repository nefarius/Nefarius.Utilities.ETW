# EtwValueKind

Namespace: Nefarius.Utilities.ETW.Events

The kind of an [EtwValue](./nefarius.utilities.etw.events.etwvalue.md).

```csharp
public enum EtwValueKind
```

Inheritance [Object](https://learn.microsoft.com/dotnet/api/system.object) → [ValueType](https://learn.microsoft.com/dotnet/api/system.valuetype) → [Enum](https://learn.microsoft.com/dotnet/api/system.enum) → [EtwValueKind](./nefarius.utilities.etw.events.etwvaluekind.md)<br>
Implements [IComparable](https://learn.microsoft.com/dotnet/api/system.icomparable), [ISpanFormattable](https://learn.microsoft.com/dotnet/api/system.ispanformattable), [IFormattable](https://learn.microsoft.com/dotnet/api/system.iformattable), [IConvertible](https://learn.microsoft.com/dotnet/api/system.iconvertible)

## Fields

| Name | Value | Description |
| --- | --: | --- |
| Null | 0 | No value. |
| Boolean | 1 | A [Boolean](https://learn.microsoft.com/dotnet/api/system.boolean). |
| Int64 | 2 | A signed integer, widened to [Int64](https://learn.microsoft.com/dotnet/api/system.int64). |
| UInt64 | 3 | An unsigned integer or pointer, widened to [UInt64](https://learn.microsoft.com/dotnet/api/system.uint64). |
| Double | 4 | A floating point number, widened to [Double](https://learn.microsoft.com/dotnet/api/system.double). |
| String | 5 | Text, including SIDs. |
| Binary | 6 | Opaque bytes, including hex dumps. |
| Guid | 7 | A [Guid](https://learn.microsoft.com/dotnet/api/system.guid). |
| DateTime | 8 | A UTC [DateTime](https://learn.microsoft.com/dotnet/api/system.datetime). |
| Array | 9 | An ordered list of values. |
| Object | 10 | An ordered list of named values. |
