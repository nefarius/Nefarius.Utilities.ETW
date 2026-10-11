# PdbResolutionOrigin

Namespace: Nefarius.Utilities.ETW.Symbols

Where a resolved PDB file was found.

```csharp
public enum PdbResolutionOrigin
```

Inheritance [Object](https://learn.microsoft.com/dotnet/api/system.object) → [ValueType](https://learn.microsoft.com/dotnet/api/system.valuetype) → [Enum](https://learn.microsoft.com/dotnet/api/system.enum) → [PdbResolutionOrigin](./nefarius.utilities.etw.symbols.pdbresolutionorigin.md)<br>
Implements [IComparable](https://learn.microsoft.com/dotnet/api/system.icomparable), [ISpanFormattable](https://learn.microsoft.com/dotnet/api/system.ispanformattable), [IFormattable](https://learn.microsoft.com/dotnet/api/system.iformattable), [IConvertible](https://learn.microsoft.com/dotnet/api/system.iconvertible)

## Fields

| Name | Value | Description |
| --- | --: | --- |
| Unresolved | 0 | No matching PDB was found. |
| Cache | 1 | The symbol-server cache already contained the indexed PDB. |
| LocalSearch | 2 | A search path contained a PDB with the requested file name, GUID, and age. |
| SymbolServer | 3 | The PDB was downloaded from the configured symbol server. |
