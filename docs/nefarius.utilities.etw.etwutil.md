# EtwUtil

Namespace: Nefarius.Utilities.ETW

ETW utility class.

```csharp
public static class EtwUtil
```

Inheritance [Object](https://docs.microsoft.com/en-us/dotnet/api/system.object) → [EtwUtil](./nefarius.utilities.etw.etwutil.md)

## Methods

### <a id="methods-enumeratepdbReferences"/>**EnumeratePdbReferences(IEnumerable&lt;String&gt;, Action&lt;EtwMetadataScanOptions&gt;)**

Performs a lightweight pre-scan of one or more `.ETL` files and returns the deduplicated set of `PdbMetaData` entries
discovered in the trace's image-ID events.

```csharp
public static IReadOnlyCollection<PdbMetaData> EnumeratePdbReferences(IEnumerable<String> inputFiles, Action<EtwMetadataScanOptions> options)
```

#### Parameters

`inputFiles` [IEnumerable&lt;String&gt;](https://docs.microsoft.com/en-us/dotnet/api/system.collections.generic.ienumerable-1)<br>
One or more input `.etl` files to scan.

`options` [Action&lt;EtwMetadataScanOptions&gt;](https://docs.microsoft.com/en-us/dotnet/api/system.action-1)<br>
Optional callback to configure scan behaviour and subscribe to image-ID event notifications.

#### Returns

A deduplicated, read-only collection of every `PdbMetaData` referenced by the trace. Use these entries to locate or
download the corresponding `.pdb` files, then pass the resulting
[DecodingContext](./nefarius.utilities.etw.deserializer.wpp.decodingcontext.md) to
[ConvertToJson](#methods-converttojson) via
[EtwJsonConverterOptions.WppDecodingContext](./nefarius.utilities.etw.etwjsonconverteroptions.md).

---

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
