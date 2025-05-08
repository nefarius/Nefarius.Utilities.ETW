# PdbFilesDecodingContextType

Namespace: Nefarius.Utilities.ETW.Deserializer.WPP

A [TDH_CONTEXT_TYPE.TDH_CONTEXT_PDB_PATH](./windows.win32.system.diagnostics.etw.tdh_context_type.md#tdh_context_pdb_path) wrapper for use with [DecodingContext](./nefarius.utilities.etw.deserializer.wpp.decodingcontext.md).

```csharp
public sealed class PdbFilesDecodingContextType : DecodingContextType
```

Inheritance [Object](https://docs.microsoft.com/en-us/dotnet/api/system.object) → [DecodingContextType](./nefarius.utilities.etw.deserializer.wpp.decodingcontexttype.md) → [PdbFilesDecodingContextType](./nefarius.utilities.etw.deserializer.wpp.pdbfilesdecodingcontexttype.md)

## Properties

### <a id="properties-value"/>**Value**

```csharp
public string Value { get; }
```

#### Property Value

[String](https://docs.microsoft.com/en-us/dotnet/api/system.string)<br>

## Constructors

### <a id="constructors-.ctor"/>**PdbFilesDecodingContextType()**

A [TDH_CONTEXT_TYPE.TDH_CONTEXT_PDB_PATH](./windows.win32.system.diagnostics.etw.tdh_context_type.md#tdh_context_pdb_path) wrapper for use with [DecodingContext](./nefarius.utilities.etw.deserializer.wpp.decodingcontext.md).

```csharp
public PdbFilesDecodingContextType()
```

### <a id="constructors-.ctor"/>**PdbFilesDecodingContextType(IList&lt;String&gt;)**

Gets decoding info from one or multiple `.PDB` files.

```csharp
public PdbFilesDecodingContextType(IList<String> pathList)
```

#### Parameters

`pathList` [IList&lt;String&gt;](https://docs.microsoft.com/en-us/dotnet/api/system.collections.generic.ilist-1)<br>
Null-terminated Unicode string that contains the name of the .pdb file for the binary that
 contains WPP messages. This parameter can be used as an alternative to TDH_CONTEXT_WPP_TMFFILE or
 TDH_CONTEXT_WPP_TMFSEARCHPATH.
