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
