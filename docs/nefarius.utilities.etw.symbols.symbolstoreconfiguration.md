# SymbolStoreConfiguration

Namespace: Nefarius.Utilities.ETW.Symbols

Local cache, search paths, and optional symbol server used to resolve PDB identities.

```csharp
public sealed class SymbolStoreConfiguration
```

Inheritance [Object](https://learn.microsoft.com/dotnet/api/system.object) → [SymbolStoreConfiguration](./nefarius.utilities.etw.symbols.symbolstoreconfiguration.md)<br>
Attributes [NullableContextAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullablecontextattribute), [NullableAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullableattribute)

## Properties

### <a id="properties-cachedirectory"/>**CacheDirectory**

Gets the directory that stores downloaded PDBs in symbol-server index layout.

```csharp
public string CacheDirectory { get; }
```

#### Property Value

[String](https://learn.microsoft.com/dotnet/api/system.string)<br>

### <a id="properties-notices"/>**Notices**

Gets human-readable notes produced while interpreting `_NT_SYMBOL_PATH`.

```csharp
public IReadOnlyList<String> Notices { get; private set; }
```

#### Property Value

[IReadOnlyList](https://learn.microsoft.com/dotnet/api/system.collections.generic.ireadonlylist-1)<[String](https://learn.microsoft.com/dotnet/api/system.string)><br>

### <a id="properties-searchpaths"/>**SearchPaths**

Gets directories or filename globs searched before the symbol server.

```csharp
public IReadOnlyList<String> SearchPaths { get; }
```

#### Property Value

[IReadOnlyList](https://learn.microsoft.com/dotnet/api/system.collections.generic.ireadonlylist-1)<[String](https://learn.microsoft.com/dotnet/api/system.string)><br>

### <a id="properties-serverurl"/>**ServerUrl**

Gets the optional HTTP symbol server, without a trailing slash requirement.

```csharp
public string ServerUrl { get; }
```

#### Property Value

[String](https://learn.microsoft.com/dotnet/api/system.string)<br>

## Constructors

### <a id="constructors-.ctor"/>**SymbolStoreConfiguration(String, String, IReadOnlyList&lt;String&gt;)**

Creates a symbol-store configuration.

```csharp
public SymbolStoreConfiguration(string cacheDirectory, string serverUrl, IReadOnlyList<String> searchPaths)
```

#### Parameters

`cacheDirectory` [String](https://learn.microsoft.com/dotnet/api/system.string)<br>

`serverUrl` [String](https://learn.microsoft.com/dotnet/api/system.string)<br>

`searchPaths` [IReadOnlyList](https://learn.microsoft.com/dotnet/api/system.collections.generic.ireadonlylist-1)<[String](https://learn.microsoft.com/dotnet/api/system.string)><br>

## Methods

### <a id="methods-resolve"/>**Resolve(String, String, String, String, IReadOnlyList&lt;String&gt;)**

Resolves the effective server and cache from explicit values, or from `ntSymbolPath`
 when both explicit values are absent.

```csharp
public static SymbolStoreConfiguration Resolve(string configuredServer, string configuredCache, string ntSymbolPath, string defaultCacheDirectory, IReadOnlyList<String> searchPaths)
```

#### Parameters

`configuredServer` [String](https://learn.microsoft.com/dotnet/api/system.string)<br>

`configuredCache` [String](https://learn.microsoft.com/dotnet/api/system.string)<br>

`ntSymbolPath` [String](https://learn.microsoft.com/dotnet/api/system.string)<br>

`defaultCacheDirectory` [String](https://learn.microsoft.com/dotnet/api/system.string)<br>

`searchPaths` [IReadOnlyList](https://learn.microsoft.com/dotnet/api/system.collections.generic.ireadonlylist-1)<[String](https://learn.microsoft.com/dotnet/api/system.string)><br>

#### Returns

[SymbolStoreConfiguration](./nefarius.utilities.etw.symbols.symbolstoreconfiguration.md)

**Remarks:**

Supports `srv*cache*url`, `srv*url`, and `cache*dir` segments. Explicit server or cache
 values bypass environment parsing.
