# SymbolPathPatterns

Namespace: Nefarius.Utilities.ETW.Symbols

Validates and expands filename globs used when discovering PDB and TMF inputs.

```csharp
public static class SymbolPathPatterns
```

Inheritance [Object](https://learn.microsoft.com/dotnet/api/system.object) → [SymbolPathPatterns](./nefarius.utilities.etw.symbols.symbolpathpatterns.md)<br>
Attributes [NullableContextAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullablecontextattribute), [NullableAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullableattribute)

## Methods

### <a id="methods-enumerate"/>**Enumerate(String)**

Enumerates files matched by a glob. The caller must already have validated `glob`.
 Directory segments after the wildcard-free root are honoured: a literal segment must exist, and a
 standalone `**` matches zero or more directories, so `root\**\sub\*.pdb` only returns files
 inside directories named `sub` below `root`.

```csharp
public static IEnumerable<String> Enumerate(string glob)
```

#### Parameters

`glob` [String](https://learn.microsoft.com/dotnet/api/system.string)<br>

#### Returns

[IEnumerable](https://learn.microsoft.com/dotnet/api/system.collections.generic.ienumerable-1)<[String](https://learn.microsoft.com/dotnet/api/system.string)>

### <a id="methods-normalizeroot"/>**NormalizeRoot(String)**

Returns the nearest directory portion of `glob` that contains no wildcard characters.

```csharp
public static string NormalizeRoot(string glob)
```

#### Parameters

`glob` [String](https://learn.microsoft.com/dotnet/api/system.string)<br>

#### Returns

[String](https://learn.microsoft.com/dotnet/api/system.string)

### <a id="methods-validate"/>**Validate(String)**

Validates that `glob` only uses a filename wildcard, optionally preceded by a
 standalone `**` recursion marker.

```csharp
public static string Validate(string glob)
```

#### Parameters

`glob` [String](https://learn.microsoft.com/dotnet/api/system.string)<br>

#### Returns

A human-readable error when the pattern is unsupported; otherwise `null`.
