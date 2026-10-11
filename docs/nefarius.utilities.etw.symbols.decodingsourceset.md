# DecodingSourceSet

Namespace: Nefarius.Utilities.ETW.Symbols

PDB and TMF sources loaded into one WPP [DecodingContext](./nefarius.utilities.etw.deserializer.wpp.decodingcontext.md).

```csharp
public sealed class DecodingSourceSet
```

Inheritance [Object](https://learn.microsoft.com/dotnet/api/system.object) → [DecodingSourceSet](./nefarius.utilities.etw.symbols.decodingsourceset.md)<br>
Attributes [NullableContextAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullablecontextattribute), [NullableAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullableattribute)

## Properties

### <a id="properties-canrewriteprovidernames"/>**CanRewriteProviderNames**

Returns whether any loaded PDB declares a single usable WPP control-GUID name.

```csharp
public bool CanRewriteProviderNames { get; }
```

#### Property Value

[Boolean](https://learn.microsoft.com/dotnet/api/system.boolean)<br>

### <a id="properties-context"/>**Context**

Gets the combined decoding context, or `null` when nothing loaded.

```csharp
public DecodingContext Context { get; }
```

#### Property Value

[DecodingContext](./nefarius.utilities.etw.deserializer.wpp.decodingcontext.md)<br>

### <a id="properties-diagnostics"/>**Diagnostics**

Gets human-readable load messages, including skipped inputs.

```csharp
public IReadOnlyList<String> Diagnostics { get; }
```

#### Property Value

[IReadOnlyList](https://learn.microsoft.com/dotnet/api/system.collections.generic.ireadonlylist-1)<[String](https://learn.microsoft.com/dotnet/api/system.string)><br>

### <a id="properties-sources"/>**Sources**

Gets the individual sources that were loaded.

```csharp
public IReadOnlyList<DecodingContextType> Sources { get; }
```

#### Property Value

[IReadOnlyList](https://learn.microsoft.com/dotnet/api/system.collections.generic.ireadonlylist-1)<[DecodingContextType](./nefarius.utilities.etw.deserializer.wpp.decodingcontexttype.md)><br>

## Constructors

### <a id="constructors-.ctor"/>**DecodingSourceSet(DecodingContext, IReadOnlyList&lt;DecodingContextType&gt;, IReadOnlyList&lt;String&gt;)**

Creates a loaded source set.

```csharp
public DecodingSourceSet(DecodingContext context, IReadOnlyList<DecodingContextType> sources, IReadOnlyList<String> diagnostics)
```

#### Parameters

`context` [DecodingContext](./nefarius.utilities.etw.deserializer.wpp.decodingcontext.md)<br>

`sources` [IReadOnlyList](https://learn.microsoft.com/dotnet/api/system.collections.generic.ireadonlylist-1)<[DecodingContextType](./nefarius.utilities.etw.deserializer.wpp.decodingcontexttype.md)><br>

`diagnostics` [IReadOnlyList](https://learn.microsoft.com/dotnet/api/system.collections.generic.ireadonlylist-1)<[String](https://learn.microsoft.com/dotnet/api/system.string)><br>
