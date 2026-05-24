# EtwJsonConverterOptions

Namespace: Nefarius.Utilities.ETW

Adjustments for [EtwUtil](./nefarius.utilities.etw.etwutil.md).

```csharp
public sealed class EtwJsonConverterOptions
```

Inheritance [Object](https://docs.microsoft.com/en-us/dotnet/api/system.object) → [EtwJsonConverterOptions](./nefarius.utilities.etw.etwjsonconverteroptions.md)

## Properties

### <a id="properties-customprovidermanifest"/>**CustomProviderManifest**

Custom manifest provider lookup.

```csharp
public Func<Guid, Stream> CustomProviderManifest { get; set; }
```

#### Property Value

[Func&lt;Guid, Stream&gt;](https://docs.microsoft.com/en-us/dotnet/api/system.func-2)<br>

### <a id="properties-preserverawtimestamps"/>**PreserveRawTimestamps**

If set, `PROCESS_TRACE_MODE_RAW_TIMESTAMP` will be applied when processing the trace record.

```csharp
public bool PreserveRawTimestamps { get; set; }
```

#### Property Value

[Boolean](https://docs.microsoft.com/en-us/dotnet/api/system.boolean)<br>

**Remarks:**

See
 DUMMYUNIONNAME.ProcessTraceMode

### <a id="properties-reporterror"/>**ReportError**

Reports potential parsing errors.

```csharp
public Action<String> ReportError { get; set; }
```

#### Property Value

[Action&lt;String&gt;](https://docs.microsoft.com/en-us/dotnet/api/system.action-1)<br>

### <a id="properties-wppdecodingcontext"/>**WppDecodingContext**

[DecodingContext](./nefarius.utilities.etw.deserializer.wpp.decodingcontext.md) to read WPP events.

```csharp
public DecodingContext WppDecodingContext { get; set; }
```

#### Property Value

[DecodingContext](./nefarius.utilities.etw.deserializer.wpp.decodingcontext.md)<br>

**Remarks:**

Build this context by calling [EtwUtil.EnumeratePdbReferences(IEnumerable&lt;String&gt;, Action&lt;EtwMetadataScanOptions&gt;)](./nefarius.utilities.etw.etwutil.md#enumeratepdbreferencesienumerablestring-actionetwmetadatascanoptions) first to discover all PDB
 references in the trace, resolve each [PdbMetaData](./nefarius.utilities.etw.deserializer.wpp.pdbmetadata.md) to its actual `.pdb` file, then
 construct a [DecodingContext](./nefarius.utilities.etw.deserializer.wpp.decodingcontext.md) from the resulting
 [PdbFileDecodingContextType](./nefarius.utilities.etw.deserializer.wpp.pdbfiledecodingcontexttype.md) (or [TmfFilesDirectoryDecodingContextType](./nefarius.utilities.etw.deserializer.wpp.tmffilesdirectorydecodingcontexttype.md)) instances
 before calling [EtwUtil.ConvertToJson(Utf8JsonWriter, IEnumerable&lt;String&gt;, Action&lt;EtwJsonConverterOptions&gt;)](./nefarius.utilities.etw.etwutil.md#converttojsonutf8jsonwriter-ienumerablestring-actionetwjsonconverteroptions).
