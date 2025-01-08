# EtwUtil

Namespace: Nefarius.Utilities.ETW

ETW utility class.

```csharp
public static class EtwUtil
```

Inheritance [Object](https://docs.microsoft.com/en-us/dotnet/api/system.object) → [EtwUtil](./nefarius.utilities.etw.etwutil.md)

## Methods

### <a id="methods-converttojson"/>**ConvertToJson(Utf8JsonWriter, IEnumerable&lt;String&gt;, Action&lt;String&gt;, Func&lt;Guid, Stream&gt;, DecodingContext)**

Converts one or more .ETL files to a JSON object.

```csharp
public static bool ConvertToJson(Utf8JsonWriter jsonWriter, IEnumerable<String> inputFiles, Action<String> reportError, Func<Guid, Stream> customProviderManifest, DecodingContext decodingContext)
```

#### Parameters

`jsonWriter` Utf8JsonWriter<br>
The target JSON writer to write to.

`inputFiles` [IEnumerable&lt;String&gt;](https://docs.microsoft.com/en-us/dotnet/api/system.collections.generic.ienumerable-1)<br>
One or more input files.

`reportError` [Action&lt;String&gt;](https://docs.microsoft.com/en-us/dotnet/api/system.action-1)<br>
Potential parsing errors.

`customProviderManifest` [Func&lt;Guid, Stream&gt;](https://docs.microsoft.com/en-us/dotnet/api/system.func-2)<br>
Optionally called to load custom manifests for providers.

`decodingContext` [DecodingContext](./nefarius.utilities.etw.deserializer.wpp.decodingcontext.md)<br>
Optional [DecodingContext](./nefarius.utilities.etw.deserializer.wpp.decodingcontext.md) to read WPP events.

#### Returns

True on success, false otherwise.
