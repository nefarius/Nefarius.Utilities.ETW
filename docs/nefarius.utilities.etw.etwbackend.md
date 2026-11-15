# EtwBackend

Namespace: Nefarius.Utilities.ETW

Selects the implementation used to read ETL files.

```csharp
public enum EtwBackend
```

Inheritance [Object](https://learn.microsoft.com/dotnet/api/system.object) → [ValueType](https://learn.microsoft.com/dotnet/api/system.valuetype) → [Enum](https://learn.microsoft.com/dotnet/api/system.enum) → [EtwBackend](./nefarius.utilities.etw.etwbackend.md)<br>
Implements [IComparable](https://learn.microsoft.com/dotnet/api/system.icomparable), [ISpanFormattable](https://learn.microsoft.com/dotnet/api/system.ispanformattable), [IFormattable](https://learn.microsoft.com/dotnet/api/system.iformattable), [IConvertible](https://learn.microsoft.com/dotnet/api/system.iconvertible)

## Fields

| Name | Value | Description |
| --- | --: | --- |
| Auto | 0 | [EtwBackend.Native](./nefarius.utilities.etw.etwbackend.md#native) on Windows, [EtwBackend.Managed](./nefarius.utilities.etw.etwbackend.md#managed) everywhere else. |
| Native | 1 | The Windows ETW APIs (`OpenTrace`/`ProcessTrace` and TDH). Windows only. |
| Managed | 2 | The portable managed ETL reader. Runs on every platform; supports offline `.etl` files only. |
