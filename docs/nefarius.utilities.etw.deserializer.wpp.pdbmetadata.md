# PdbMetaData

Namespace: Nefarius.Utilities.ETW.Deserializer.WPP

Describes a Program DataBase metaobject extracted from the provided trace.

```csharp
public struct PdbMetaData
```

Inheritance [Object](https://docs.microsoft.com/en-us/dotnet/api/system.object) → [ValueType](https://docs.microsoft.com/en-us/dotnet/api/system.valuetype) → [PdbMetaData](./nefarius.utilities.etw.deserializer.wpp.pdbmetadata.md)

## Properties

### <a id="properties-age"/>**Age**

The age a.k.a. the revision of the build of the symbol file.

```csharp
public int Age { get; set; }
```

#### Property Value

[Int32](https://docs.microsoft.com/en-us/dotnet/api/system.int32)<br>

### <a id="properties-guid"/>**Guid**

The GUID uniquely identifying the symbol file.

```csharp
public Guid Guid { get; set; }
```

#### Property Value

[Guid](https://docs.microsoft.com/en-us/dotnet/api/system.guid)<br>

### <a id="properties-indexprefix"/>**IndexPrefix**

Index prefix (relative path name) of the symbol to lookup on a symbol server.

```csharp
public string IndexPrefix { get; }
```

#### Property Value

[String](https://docs.microsoft.com/en-us/dotnet/api/system.string)<br>

### <a id="properties-pdbname"/>**PdbName**

The full path of the PDB extracted from the session information.

```csharp
public string PdbName { get; set; }
```

#### Property Value

[String](https://docs.microsoft.com/en-us/dotnet/api/system.string)<br>
