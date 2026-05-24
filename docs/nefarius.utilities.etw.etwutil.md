# EtwUtil

Namespace: Nefarius.Utilities.ETW

ETW utility class.

```csharp
public static class EtwUtil
```

Inheritance [Object](https://docs.microsoft.com/en-us/dotnet/api/system.object) → [EtwUtil](./nefarius.utilities.etw.etwutil.md)

## Methods

### <a id="methods-converttojson"/>**ConvertToJson(Utf8JsonWriter, IEnumerable&lt;String&gt;, Action&lt;EtwJsonConverterOptions&gt;)**

Converts one or more .ETL files to a JSON object.

```csharp
public static bool ConvertToJson(Utf8JsonWriter jsonWriter, IEnumerable<String> inputFiles, Action<EtwJsonConverterOptions> options)
```

#### Parameters

`jsonWriter` Utf8JsonWriter<br>
The target JSON writer to write to.

`inputFiles` [IEnumerable&lt;String&gt;](https://docs.microsoft.com/en-us/dotnet/api/system.collections.generic.ienumerable-1)<br>
One or more input files.

`options` [Action&lt;EtwJsonConverterOptions&gt;](https://docs.microsoft.com/en-us/dotnet/api/system.action-1)<br>
Options to further tweak the parsing operation.

#### Returns

True on success, false otherwise.

### <a id="methods-enumeratepdbreferences"/>**EnumeratePdbReferences(IEnumerable&lt;String&gt;, Action&lt;EtwMetadataScanOptions&gt;)**

Performs a lightweight pre-scan of one or more `.ETL` files and returns the deduplicated set of
 [PdbMetaData](./nefarius.utilities.etw.deserializer.wpp.pdbmetadata.md) entries discovered in the trace.

```csharp
public static IReadOnlyCollection<PdbMetaData> EnumeratePdbReferences(IEnumerable<String> inputFiles, Action<EtwMetadataScanOptions> options)
```

#### Parameters

`inputFiles` [IEnumerable&lt;String&gt;](https://docs.microsoft.com/en-us/dotnet/api/system.collections.generic.ienumerable-1)<br>
One or more input `.etl` files to scan. Must not be  and must not
 contain any  or whitespace-only entries.

`options` [Action&lt;EtwMetadataScanOptions&gt;](https://docs.microsoft.com/en-us/dotnet/api/system.action-1)<br>
Optional callback to configure scan behaviour. Use it to subscribe to
 [EtwMetadataScanOptions.OnDbgIdRsds](./nefarius.utilities.etw.etwmetadatascanoptions.md#ondbgidrsds) (`KernelTraceControl/ImageID/DbgID_RSDS`),
 [EtwMetadataScanOptions.OnKernelDbgIdRsds](./nefarius.utilities.etw.etwmetadatascanoptions.md#onkerneldbgidrsds) (`MSNT_SystemTrace/EventTrace/DbgIdRSDS`),
 [EtwMetadataScanOptions.OnImageId](./nefarius.utilities.etw.etwmetadatascanoptions.md#onimageid) (`KernelTraceControl/ImageID`), and
 [EtwMetadataScanOptions.OnImageIdFileVersion](./nefarius.utilities.etw.etwmetadatascanoptions.md#onimageidfileversion) (`KernelTraceControl/ImageID/FileVersion`)
 event notifications, or to provide a [EtwMetadataScanOptions.ReportError](./nefarius.utilities.etw.etwmetadatascanoptions.md#reporterror) handler.

#### Returns

A deduplicated, read-only collection of every [PdbMetaData](./nefarius.utilities.etw.deserializer.wpp.pdbmetadata.md) referenced by the trace.
 The caller should use these entries to locate or download the corresponding `.pdb` files (or find
 matching `.tmf` files), then pass the resulting [DecodingContext](./nefarius.utilities.etw.deserializer.wpp.decodingcontext.md) to
 [EtwUtil.ConvertToJson(Utf8JsonWriter, IEnumerable&lt;String&gt;, Action&lt;EtwJsonConverterOptions&gt;)](./nefarius.utilities.etw.etwutil.md#converttojsonutf8jsonwriter-ienumerablestring-actionetwjsonconverteroptions) via [EtwJsonConverterOptions.WppDecodingContext](./nefarius.utilities.etw.etwjsonconverteroptions.md#wppdecodingcontext).

#### Exceptions

[ArgumentNullException](https://docs.microsoft.com/en-us/dotnet/api/system.argumentnullexception)<br>
`inputFiles` is .

[ArgumentException](https://docs.microsoft.com/en-us/dotnet/api/system.argumentexception)<br>
One or more entries in `inputFiles` are , empty, or consist only of
 whitespace.
