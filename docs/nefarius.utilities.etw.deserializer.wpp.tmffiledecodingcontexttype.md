# TmfFileDecodingContextType

Namespace: Nefarius.Utilities.ETW.Deserializer.WPP

A [TDH_CONTEXT_TYPE.TDH_CONTEXT_WPP_TMFFILE](./windows.win32.system.diagnostics.etw.tdh_context_type.md#tdh_context_wpp_tmffile) wrapper for use with [DecodingContext](./nefarius.utilities.etw.deserializer.wpp.decodingcontext.md).

```csharp
public sealed class TmfFileDecodingContextType : DecodingContextType
```

Inheritance [Object](https://docs.microsoft.com/en-us/dotnet/api/system.object) → [DecodingContextType](./nefarius.utilities.etw.deserializer.wpp.decodingcontexttype.md) → [TmfFileDecodingContextType](./nefarius.utilities.etw.deserializer.wpp.tmffiledecodingcontexttype.md)

## Constructors

### <a id="constructors-.ctor"/>**TmfFileDecodingContextType()**

A [TDH_CONTEXT_TYPE.TDH_CONTEXT_WPP_TMFFILE](./windows.win32.system.diagnostics.etw.tdh_context_type.md#tdh_context_wpp_tmffile) wrapper for use with [DecodingContext](./nefarius.utilities.etw.deserializer.wpp.decodingcontext.md).

```csharp
public TmfFileDecodingContextType()
```

### <a id="constructors-.ctor"/>**TmfFileDecodingContextType(String)**

Gets decoding info from a single `.TMF` file.

```csharp
public TmfFileDecodingContextType(string path)
```

#### Parameters

`path` [String](https://docs.microsoft.com/en-us/dotnet/api/system.string)<br>
Null-terminated Unicode string that contains the name of the .tmf file used for parsing the WPP log.
 Typically, the .tmf file name is picked up from the event GUID so you do not have to specify the file name.
