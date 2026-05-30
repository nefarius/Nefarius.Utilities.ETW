# EtwJsonConverterOptions

Namespace: Nefarius.Utilities.ETW

Adjustments for [EtwUtil](./nefarius.utilities.etw.etwutil.md).

```csharp
public sealed class EtwJsonConverterOptions
```

Inheritance [Object](https://learn.microsoft.com/dotnet/api/system.object) → [EtwJsonConverterOptions](./nefarius.utilities.etw.etwjsonconverteroptions.md)<br>
Attributes [NullableContextAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullablecontextattribute), [NullableAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullableattribute)

## Properties

### <a id="properties-customprovidermanifest"/>**CustomProviderManifest**

Custom manifest provider lookup.

```csharp
public Func<Guid, Stream> CustomProviderManifest { get; set; }
```

#### Property Value

[Func](https://learn.microsoft.com/dotnet/api/system.func-2)<[Guid](https://learn.microsoft.com/dotnet/api/system.guid), [Stream](https://learn.microsoft.com/dotnet/api/system.io.stream)><br>

### <a id="properties-onwppformatmissing"/>**OnWppFormatMissing**

Invoked whenever a WPP event is decoded but no matching [TraceMessageFormat](./nefarius.utilities.etw.deserializer.wpp.tmf.tracemessageformat.md)
 was found in the supplied [EtwJsonConverterOptions.WppDecodingContext](./nefarius.utilities.etw.etwjsonconverteroptions.md#wppdecodingcontext) (i.e. the event's `FormattedString` was
 substituted with the `"GUID=... - No format information found."` placeholder).
 Receives the provider trace GUID, the WPP event id, and the version.
 Fires once per affected event; consumers are expected to deduplicate as needed.

```csharp
public Action<Guid, UInt16, UInt32> OnWppFormatMissing { get; set; }
```

#### Property Value

[Action](https://learn.microsoft.com/dotnet/api/system.action-3)<[Guid](https://learn.microsoft.com/dotnet/api/system.guid), [UInt16](https://learn.microsoft.com/dotnet/api/system.uint16), [UInt32](https://learn.microsoft.com/dotnet/api/system.uint32)><br>

### <a id="properties-preserverawtimestamps"/>**PreserveRawTimestamps**

If set, `PROCESS_TRACE_MODE_RAW_TIMESTAMP` will be applied when processing the trace record.

```csharp
public bool PreserveRawTimestamps { get; set; }
```

#### Property Value

[Boolean](https://learn.microsoft.com/dotnet/api/system.boolean)<br>

**Remarks:**

See
 DUMMYUNIONNAME.ProcessTraceMode

### <a id="properties-reporterror"/>**ReportError**

Reports potential parsing errors.

```csharp
public Action<String> ReportError { get; set; }
```

#### Property Value

[Action](https://learn.microsoft.com/dotnet/api/system.action-1)<[String](https://learn.microsoft.com/dotnet/api/system.string)><br>

### <a id="properties-rewritewppprovidername"/>**RewriteWppProviderName**

When `true`, the `GuidName` / `Provider` field in decoded WPP events is
 overridden with the friendly name declared in the `TMC:``WPP_DEFINE_CONTROL_GUID` annotation
 (e.g. `DsHidMiniTraceGuid`) instead of the raw TMF module token (e.g. `sys`).

```csharp
public bool RewriteWppProviderName { get; set; }
```

#### Property Value

[Boolean](https://learn.microsoft.com/dotnet/api/system.boolean)<br>

**Remarks:**

The override is resolved on a per-PDB basis: if a PDB declares exactly one control GUID whose
 name is non-empty, every message format that originated from that PDB is mapped to that name.
 PDBs with zero or more than one control GUID are skipped (ambiguous mapping) and the original
 `format.Provider` value is kept unchanged — so the fallback is always silent and safe.

Has no effect when [EtwJsonConverterOptions.WppDecodingContext](./nefarius.utilities.etw.etwjsonconverteroptions.md#wppdecodingcontext) is not set or was built from TMF files only.

Default is `false` (existing output is unchanged).

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
