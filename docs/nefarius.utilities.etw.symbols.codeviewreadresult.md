# CodeViewReadResult

Namespace: Nefarius.Utilities.ETW.Symbols

Result of reading a PE CodeView debug directory.

```csharp
public record struct CodeViewReadResult
```

Inheritance [Object](https://learn.microsoft.com/dotnet/api/system.object) → [ValueType](https://learn.microsoft.com/dotnet/api/system.valuetype) → [CodeViewReadResult](./nefarius.utilities.etw.symbols.codeviewreadresult.md)<br>
Attributes [NullableContextAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullablecontextattribute), [NullableAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullableattribute), [IsReadOnlyAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.isreadonlyattribute)

## Properties

### <a id="properties-diagnostic"/>**Diagnostic**

A human-readable failure description; otherwise `null`.

```csharp
public string Diagnostic { get; set; }
```

#### Property Value

[String](https://learn.microsoft.com/dotnet/api/system.string)<br>

### <a id="properties-metadata"/>**Metadata**

The embedded PDB identity when a CodeView entry was found.

```csharp
public Nullable<PdbMetaData> Metadata { get; set; }
```

#### Property Value

[Nullable](https://learn.microsoft.com/dotnet/api/system.nullable-1)<[PdbMetaData](./nefarius.utilities.etw.deserializer.wpp.pdbmetadata.md)><br>

## Constructors

### <a id="constructors-.ctor"/>**CodeViewReadResult(Nullable&lt;PdbMetaData&gt;, String)**

Result of reading a PE CodeView debug directory.

```csharp
CodeViewReadResult(Nullable<PdbMetaData> Metadata, string Diagnostic)
```

#### Parameters

`Metadata` [Nullable](https://learn.microsoft.com/dotnet/api/system.nullable-1)<[PdbMetaData](./nefarius.utilities.etw.deserializer.wpp.pdbmetadata.md)><br>
The embedded PDB identity when a CodeView entry was found.

`Diagnostic` [String](https://learn.microsoft.com/dotnet/api/system.string)<br>
A human-readable failure description; otherwise `null`.

## Methods

### <a id="methods-equals"/>**Equals(CodeViewReadResult)**

```csharp
bool Equals(CodeViewReadResult other)
```

#### Parameters

`other` [CodeViewReadResult](./nefarius.utilities.etw.symbols.codeviewreadresult.md)<br>

#### Returns

[Boolean](https://learn.microsoft.com/dotnet/api/system.boolean)
