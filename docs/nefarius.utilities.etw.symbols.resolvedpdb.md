# ResolvedPdb

Namespace: Nefarius.Utilities.ETW.Symbols

The outcome of resolving one [PdbMetaData](./nefarius.utilities.etw.deserializer.wpp.pdbmetadata.md) reference.

```csharp
public sealed class ResolvedPdb
```

Inheritance [Object](https://learn.microsoft.com/dotnet/api/system.object) → [ResolvedPdb](./nefarius.utilities.etw.symbols.resolvedpdb.md)<br>
Attributes [NullableContextAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullablecontextattribute), [NullableAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullableattribute), [RequiredMemberAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.requiredmemberattribute)

## Properties

### <a id="properties-detail"/>**Detail**

Gets a URL or exception detail associated with the attempt.

```csharp
public string Detail { get; set; }
```

#### Property Value

[String](https://learn.microsoft.com/dotnet/api/system.string)<br>

### <a id="properties-failure"/>**Failure**

Gets the server-side failure classification when [ResolvedPdb.Origin](./nefarius.utilities.etw.symbols.resolvedpdb.md#origin) is unresolved.

```csharp
public PdbResolutionFailure Failure { get; set; }
```

#### Property Value

[PdbResolutionFailure](./nefarius.utilities.etw.symbols.pdbresolutionfailure.md)<br>

### <a id="properties-localpath"/>**LocalPath**

Gets the local PDB path when resolution succeeded.

```csharp
public string LocalPath { get; set; }
```

#### Property Value

[String](https://learn.microsoft.com/dotnet/api/system.string)<br>

### <a id="properties-origin"/>**Origin**

Gets the source that supplied [ResolvedPdb.LocalPath](./nefarius.utilities.etw.symbols.resolvedpdb.md#localpath).

```csharp
public PdbResolutionOrigin Origin { get; set; }
```

#### Property Value

[PdbResolutionOrigin](./nefarius.utilities.etw.symbols.pdbresolutionorigin.md)<br>

### <a id="properties-reference"/>**Reference**

Gets the requested PDB identity.

```csharp
public PdbMetaData Reference { get; set; }
```

#### Property Value

[PdbMetaData](./nefarius.utilities.etw.deserializer.wpp.pdbmetadata.md)<br>

## Constructors

### <a id="constructors-.ctor"/>**ResolvedPdb()**

#### Caution

Constructors of types with required members are not supported in this version of your compiler.

---

```csharp
public ResolvedPdb()
```
