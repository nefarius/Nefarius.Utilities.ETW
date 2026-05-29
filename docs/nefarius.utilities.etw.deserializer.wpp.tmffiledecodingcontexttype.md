# TmfFileDecodingContextType

Namespace: Nefarius.Utilities.ETW.Deserializer.WPP

A [TDH_CONTEXT_TYPE.TDH_CONTEXT_WPP_TMFFILE](./windows.win32.system.diagnostics.etw.tdh_context_type.md#tdh_context_wpp_tmffile) wrapper for use with [DecodingContext](./nefarius.utilities.etw.deserializer.wpp.decodingcontext.md).

```csharp
public sealed class TmfFileDecodingContextType : DecodingContextType
```

Inheritance [Object](https://learn.microsoft.com/dotnet/api/system.object) → [DecodingContextType](./nefarius.utilities.etw.deserializer.wpp.decodingcontexttype.md) → [TmfFileDecodingContextType](./nefarius.utilities.etw.deserializer.wpp.tmffiledecodingcontexttype.md)<br>
Attributes [NullableContextAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullablecontextattribute), [NullableAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullableattribute)

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

[IEnumerable](https://learn.microsoft.com/dotnet/api/system.collections.generic.ienumerable-1)<[Guid](https://learn.microsoft.com/dotnet/api/system.guid)><br>

### <a id="properties-tracemessageformats"/>**TraceMessageFormats**

Collection of extracted [TraceMessageFormat](./nefarius.utilities.etw.deserializer.wpp.tmf.tracemessageformat.md)s of this [DecodingContextType](./nefarius.utilities.etw.deserializer.wpp.decodingcontexttype.md).

```csharp
public IEnumerable<TraceMessageFormat> TraceMessageFormats { get; protected set; }
```

#### Property Value

[IEnumerable](https://learn.microsoft.com/dotnet/api/system.collections.generic.ienumerable-1)<[TraceMessageFormat](./nefarius.utilities.etw.deserializer.wpp.tmf.tracemessageformat.md)><br>

## Constructors

### <a id="constructors-.ctor"/>**TmfFileDecodingContextType()**

A [TDH_CONTEXT_TYPE.TDH_CONTEXT_WPP_TMFFILE](./windows.win32.system.diagnostics.etw.tdh_context_type.md#tdh_context_wpp_tmffile) wrapper for use with [DecodingContext](./nefarius.utilities.etw.deserializer.wpp.decodingcontext.md).

```csharp
public TmfFileDecodingContextType()
```

### <a id="constructors-.ctor"/>**TmfFileDecodingContextType(String)**

Gets decoding info from a single `.tmf` file.

```csharp
public TmfFileDecodingContextType(string path)
```

#### Parameters

`path` [String](https://learn.microsoft.com/dotnet/api/system.string)<br>
Path to a single `.tmf` file.

## Methods

### <a id="methods-createfrom"/>**CreateFrom(IList&lt;String&gt;)**

Converts a list of paths to `*.tmf` files into their corresponding
 [TmfFileDecodingContextType](./nefarius.utilities.etw.deserializer.wpp.tmffiledecodingcontexttype.md) objects.

```csharp
public static IList<DecodingContextType> CreateFrom(IList<String> pathList)
```

#### Parameters

`pathList` [IList](https://learn.microsoft.com/dotnet/api/system.collections.generic.ilist-1)<[String](https://learn.microsoft.com/dotnet/api/system.string)><br>
One or more paths to `.tmf` files.

#### Returns

One or more [TmfFileDecodingContextType](./nefarius.utilities.etw.deserializer.wpp.tmffiledecodingcontexttype.md)s.
