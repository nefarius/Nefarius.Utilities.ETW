# TmfFilesDirectoryDecodingContextType

Namespace: Nefarius.Utilities.ETW.Deserializer.WPP

A [TDH_CONTEXT_TYPE.TDH_CONTEXT_WPP_TMFSEARCHPATH](./windows.win32.system.diagnostics.etw.tdh_context_type.md#tdh_context_wpp_tmfsearchpath) wrapper for use with
 [DecodingContext](./nefarius.utilities.etw.deserializer.wpp.decodingcontext.md).

```csharp
public sealed class TmfFilesDirectoryDecodingContextType : DecodingContextType
```

Inheritance [Object](https://docs.microsoft.com/en-us/dotnet/api/system.object) → [DecodingContextType](./nefarius.utilities.etw.deserializer.wpp.decodingcontexttype.md) → [TmfFilesDirectoryDecodingContextType](./nefarius.utilities.etw.deserializer.wpp.tmffilesdirectorydecodingcontexttype.md)

## Properties

### <a id="properties-providerguids"/>**ProviderGuids**

The distinct set of WPP trace control GUIDs (= ETW provider GUIDs) available from this
 decoding source. Subclasses that carry the provider GUID — currently only
 [PdbFileDecodingContextType](./nefarius.utilities.etw.deserializer.wpp.pdbfiledecodingcontexttype.md) via its `TMC:` annotations — override this
 property. TMF-only sources return an empty collection because `.tmf` files only
 contain per-call-site message hash GUIDs, not the WPP control GUID.

```csharp
public IEnumerable<Guid> ProviderGuids { get; }
```

#### Property Value

[IEnumerable&lt;Guid&gt;](https://docs.microsoft.com/en-us/dotnet/api/system.collections.generic.ienumerable-1)<br>

### <a id="properties-tracemessageformats"/>**TraceMessageFormats**

Collection of extracted [TraceMessageFormat](./nefarius.utilities.etw.deserializer.wpp.tmf.tracemessageformat.md)s of this [DecodingContextType](./nefarius.utilities.etw.deserializer.wpp.decodingcontexttype.md).

```csharp
public IEnumerable<TraceMessageFormat> TraceMessageFormats { get; protected set; }
```

#### Property Value

[IEnumerable&lt;TraceMessageFormat&gt;](https://docs.microsoft.com/en-us/dotnet/api/system.collections.generic.ienumerable-1)<br>

## Constructors

### <a id="constructors-.ctor"/>**TmfFilesDirectoryDecodingContextType()**

A [TDH_CONTEXT_TYPE.TDH_CONTEXT_WPP_TMFSEARCHPATH](./windows.win32.system.diagnostics.etw.tdh_context_type.md#tdh_context_wpp_tmfsearchpath) wrapper for use with
 [DecodingContext](./nefarius.utilities.etw.deserializer.wpp.decodingcontext.md).

```csharp
public TmfFilesDirectoryDecodingContextType()
```

### <a id="constructors-.ctor"/>**TmfFilesDirectoryDecodingContextType(String)**

Gets decoding info from a directory containing multiple `.tmf` files.

```csharp
public TmfFilesDirectoryDecodingContextType(string path)
```

#### Parameters

`path` [String](https://docs.microsoft.com/en-us/dotnet/api/system.string)<br>
Path to a directory containing multiple `.tmf` files.

## Methods

### <a id="methods-createfrom"/>**CreateFrom(IList&lt;String&gt;)**

Converts a list of paths to `*.tmf` files into their corresponding
 [TmfFilesDirectoryDecodingContextType](./nefarius.utilities.etw.deserializer.wpp.tmffilesdirectorydecodingcontexttype.md)
 objects.

```csharp
public static IList<DecodingContextType> CreateFrom(IList<String> pathList)
```

#### Parameters

`pathList` [IList&lt;String&gt;](https://docs.microsoft.com/en-us/dotnet/api/system.collections.generic.ilist-1)<br>
One or more paths to `*.pdb` files.

#### Returns

One or more [TmfFilesDirectoryDecodingContextType](./nefarius.utilities.etw.deserializer.wpp.tmffilesdirectorydecodingcontexttype.md)s.
