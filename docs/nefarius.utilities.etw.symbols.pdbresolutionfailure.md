# PdbResolutionFailure

Namespace: Nefarius.Utilities.ETW.Symbols

Why a PDB reference was not resolved.

```csharp
public enum PdbResolutionFailure
```

Inheritance [Object](https://learn.microsoft.com/dotnet/api/system.object) → [ValueType](https://learn.microsoft.com/dotnet/api/system.valuetype) → [Enum](https://learn.microsoft.com/dotnet/api/system.enum) → [PdbResolutionFailure](./nefarius.utilities.etw.symbols.pdbresolutionfailure.md)<br>
Implements [IComparable](https://learn.microsoft.com/dotnet/api/system.icomparable), [ISpanFormattable](https://learn.microsoft.com/dotnet/api/system.ispanformattable), [IFormattable](https://learn.microsoft.com/dotnet/api/system.iformattable), [IConvertible](https://learn.microsoft.com/dotnet/api/system.iconvertible)

## Fields

| Name | Value | Description |
| --- | --: | --- |
| None | 0 | The reference was resolved, or it was absent without a server response. |
| NotFoundOnServer | 1 | The symbol server returned HTTP 404. |
| DownloadFailed | 2 | The symbol server request failed before a PDB could be stored. |
