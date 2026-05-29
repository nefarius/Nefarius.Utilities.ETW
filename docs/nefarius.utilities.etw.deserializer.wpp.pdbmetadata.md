# PdbMetaData

Namespace: Nefarius.Utilities.ETW.Deserializer.WPP

Describes a Program DataBase metaobject extracted from the provided trace.

```csharp
public struct PdbMetaData
```

Inheritance [Object](https://learn.microsoft.com/dotnet/api/system.object) → [ValueType](https://learn.microsoft.com/dotnet/api/system.valuetype) → [PdbMetaData](./nefarius.utilities.etw.deserializer.wpp.pdbmetadata.md)<br>
Implements [IEquatable](https://learn.microsoft.com/dotnet/api/system.iequatable-1)<[PdbMetaData](./nefarius.utilities.etw.deserializer.wpp.pdbmetadata.md)><br>
Attributes [NullableContextAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullablecontextattribute), [NullableAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullableattribute), [IsReadOnlyAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.isreadonlyattribute), [RequiredMemberAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.requiredmemberattribute)

## Properties

### <a id="properties-age"/>**Age**

The age a.k.a. the revision of the build of the symbol file.

```csharp
public int Age { get; set; }
```

#### Property Value

[Int32](https://learn.microsoft.com/dotnet/api/system.int32)<br>

### <a id="properties-downloadpath"/>**DownloadPath**

Gets the typical symbol server download path.

```csharp
public string DownloadPath { get; }
```

#### Property Value

[String](https://learn.microsoft.com/dotnet/api/system.string)<br>

### <a id="properties-guid"/>**Guid**

The GUID uniquely identifying the symbol file.

```csharp
public Guid Guid { get; set; }
```

#### Property Value

[Guid](https://learn.microsoft.com/dotnet/api/system.guid)<br>

### <a id="properties-indexprefix"/>**IndexPrefix**

Index prefix (relative path name) of the symbol to lookup on a symbol server.

```csharp
public string IndexPrefix { get; }
```

#### Property Value

[String](https://learn.microsoft.com/dotnet/api/system.string)<br>

**Remarks:**

For example
 hidhide.pdb/779e56ef8d244145a64a3aee304b9de91/hidhide.pdb

### <a id="properties-pdbname"/>**PdbName**

The full path of the PDB extracted from the session information.

```csharp
public string PdbName { get; set; }
```

#### Property Value

[String](https://learn.microsoft.com/dotnet/api/system.string)<br>

## Methods

### <a id="methods-equals"/>**Equals(PdbMetaData)**

```csharp
bool Equals(PdbMetaData other)
```

#### Parameters

`other` [PdbMetaData](./nefarius.utilities.etw.deserializer.wpp.pdbmetadata.md)<br>

#### Returns

[Boolean](https://learn.microsoft.com/dotnet/api/system.boolean)

### <a id="methods-equals"/>**Equals(Object)**

```csharp
bool Equals(object obj)
```

#### Parameters

`obj` [Object](https://learn.microsoft.com/dotnet/api/system.object)<br>

#### Returns

[Boolean](https://learn.microsoft.com/dotnet/api/system.boolean)

### <a id="methods-gethashcode"/>**GetHashCode()**

```csharp
int GetHashCode()
```

#### Returns

[Int32](https://learn.microsoft.com/dotnet/api/system.int32)
