# TraceSymbolResolution

Namespace: Nefarius.Utilities.ETW.Symbols

Counts and sources produced by resolving the PDB references embedded in ETL files.

```csharp
public sealed class TraceSymbolResolution
```

Inheritance [Object](https://learn.microsoft.com/dotnet/api/system.object) → [TraceSymbolResolution](./nefarius.utilities.etw.symbols.tracesymbolresolution.md)<br>
Attributes [NullableContextAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullablecontextattribute), [NullableAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullableattribute), [RequiredMemberAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.requiredmemberattribute)

## Properties

### <a id="properties-diagnostics"/>**Diagnostics**

Gets human-readable resolution messages.

```csharp
public IReadOnlyList<String> Diagnostics { get; set; }
```

#### Property Value

[IReadOnlyList](https://learn.microsoft.com/dotnet/api/system.collections.generic.ireadonlylist-1)<[String](https://learn.microsoft.com/dotnet/api/system.string)><br>

### <a id="properties-fromcache"/>**FromCache**

Gets how many references were loaded from the cache.

```csharp
public int FromCache { get; set; }
```

#### Property Value

[Int32](https://learn.microsoft.com/dotnet/api/system.int32)<br>

### <a id="properties-fromlocal"/>**FromLocal**

Gets how many references were loaded from a search path.

```csharp
public int FromLocal { get; set; }
```

#### Property Value

[Int32](https://learn.microsoft.com/dotnet/api/system.int32)<br>

### <a id="properties-fromserver"/>**FromServer**

Gets how many references were downloaded.

```csharp
public int FromServer { get; set; }
```

#### Property Value

[Int32](https://learn.microsoft.com/dotnet/api/system.int32)<br>

### <a id="properties-referencedcount"/>**ReferencedCount**

Gets the number of distinct PDB references discovered in the traces.

```csharp
public int ReferencedCount { get; set; }
```

#### Property Value

[Int32](https://learn.microsoft.com/dotnet/api/system.int32)<br>

### <a id="properties-sources"/>**Sources**

Gets the loaded PDB decoding sources.

```csharp
public IReadOnlyList<DecodingContextType> Sources { get; set; }
```

#### Property Value

[IReadOnlyList](https://learn.microsoft.com/dotnet/api/system.collections.generic.ireadonlylist-1)<[DecodingContextType](./nefarius.utilities.etw.deserializer.wpp.decodingcontexttype.md)><br>

### <a id="properties-unresolved"/>**Unresolved**

Gets how many references could not be loaded.

```csharp
public int Unresolved { get; set; }
```

#### Property Value

[Int32](https://learn.microsoft.com/dotnet/api/system.int32)<br>

## Constructors

### <a id="constructors-.ctor"/>**TraceSymbolResolution()**

#### Caution

Constructors of types with required members are not supported in this version of your compiler.

---

```csharp
public TraceSymbolResolution()
```
