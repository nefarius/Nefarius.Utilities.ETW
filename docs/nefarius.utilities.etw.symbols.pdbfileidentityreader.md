# PdbFileIdentityReader

Namespace: Nefarius.Utilities.ETW.Symbols

Reads the GUID and age embedded in a Windows PDB without requiring a separate PDB parser package.

```csharp
public static class PdbFileIdentityReader
```

Inheritance [Object](https://learn.microsoft.com/dotnet/api/system.object) → [PdbFileIdentityReader](./nefarius.utilities.etw.symbols.pdbfileidentityreader.md)

## Methods

### <a id="methods-tryread"/>**TryRead(String, ref PdbFileIdentity)**

Attempts to read the VC7 signature and age from `pdbPath`.

```csharp
public static bool TryRead(string pdbPath, ref PdbFileIdentity identity)
```

#### Parameters

`pdbPath` [String](https://learn.microsoft.com/dotnet/api/system.string)<br>

`identity` [PdbFileIdentity&](./nefarius.utilities.etw.symbols.pdbfileidentity&.md)<br>

#### Returns

`false` when the file is not a readable VC7 PDB.
