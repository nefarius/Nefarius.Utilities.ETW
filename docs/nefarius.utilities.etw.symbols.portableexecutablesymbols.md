# PortableExecutableSymbols

Namespace: Nefarius.Utilities.ETW.Symbols

Reads the PDB identity embedded in a portable executable.

```csharp
public static class PortableExecutableSymbols
```

Inheritance [Object](https://learn.microsoft.com/dotnet/api/system.object) → [PortableExecutableSymbols](./nefarius.utilities.etw.symbols.portableexecutablesymbols.md)

## Methods

### <a id="methods-tryreadcodeviewpdb"/>**TryReadCodeViewPdb(String)**

Reads the first RSDS CodeView debug entry from `binaryPath`.

```csharp
public static CodeViewReadResult TryReadCodeViewPdb(string binaryPath)
```

#### Parameters

`binaryPath` [String](https://learn.microsoft.com/dotnet/api/system.string)<br>

#### Returns

[CodeViewReadResult](./nefarius.utilities.etw.symbols.codeviewreadresult.md)
