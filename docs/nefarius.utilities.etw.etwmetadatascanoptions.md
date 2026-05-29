# EtwMetadataScanOptions

Namespace: Nefarius.Utilities.ETW

Adjustments for [EtwUtil.EnumeratePdbReferences(IEnumerable&lt;String&gt;, Action&lt;EtwMetadataScanOptions&gt;)](./nefarius.utilities.etw.etwutil.md#enumeratepdbreferencesienumerablestring-actionetwmetadatascanoptions).

```csharp
public sealed class EtwMetadataScanOptions
```

Inheritance [Object](https://learn.microsoft.com/dotnet/api/system.object) → [EtwMetadataScanOptions](./nefarius.utilities.etw.etwmetadatascanoptions.md)<br>
Attributes [NullableContextAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullablecontextattribute), [NullableAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullableattribute)

## Properties

### <a id="properties-ondbgidrsds"/>**OnDbgIdRsds**

Invoked for every `KernelTraceControl/ImageID/DbgID_RSDS` event (opcode 36, provider
 `b3e675d7-2554-4f18-830b-2762732560de`) encountered in the trace.

```csharp
public Action<DbgIdRsdsEventInfo> OnDbgIdRsds { get; set; }
```

#### Property Value

[Action](https://learn.microsoft.com/dotnet/api/system.action-1)<[DbgIdRsdsEventInfo](./nefarius.utilities.etw.events.dbgidrsdseventinfo.md)><br>

**Remarks:**

Each such event identifies a PDB that was used to instrument the code being traced.
 The [DbgIdRsdsEventInfo.ToPdbMetaData()](./nefarius.utilities.etw.events.dbgidrsdseventinfo.md#topdbmetadata) helper projects the event into the
 [PdbMetaData](./nefarius.utilities.etw.deserializer.wpp.pdbmetadata.md) shape expected by
 [EtwJsonConverterOptions.WppDecodingContext](./nefarius.utilities.etw.etwjsonconverteroptions.md#wppdecodingcontext).

### <a id="properties-onimageid"/>**OnImageId**

Invoked for every `KernelTraceControl/ImageID` event (opcode 0, provider
 `b3e675d7-2554-4f18-830b-2762732560de`) encountered in the trace.

```csharp
public Action<ImageIdEventInfo> OnImageId { get; set; }
```

#### Property Value

[Action](https://learn.microsoft.com/dotnet/api/system.action-1)<[ImageIdEventInfo](./nefarius.utilities.etw.events.imageideventinfo.md)><br>

**Remarks:**

These events carry the base address, size, and original file name of a loaded image.

### <a id="properties-onimageidfileversion"/>**OnImageIdFileVersion**

Invoked for every `KernelTraceControl/ImageID/FileVersion` event (opcode 64, provider
 `b3e675d7-2554-4f18-830b-2762732560de`) encountered in the trace.

```csharp
public Action<ImageIdFileVersionEventInfo> OnImageIdFileVersion { get; set; }
```

#### Property Value

[Action](https://learn.microsoft.com/dotnet/api/system.action-1)<[ImageIdFileVersionEventInfo](./nefarius.utilities.etw.events.imageidfileversioneventinfo.md)><br>

**Remarks:**

These events carry the version-resource strings of a loaded image.

### <a id="properties-onkerneldbgidrsds"/>**OnKernelDbgIdRsds**

Invoked for every `MSNT_SystemTrace/EventTrace/DbgIdRSDS` event emitted by the legacy kernel Event Trace
 provider (`EventTraceGuid`) encountered in the trace.

```csharp
public Action<KernelDbgIdRsdsEventInfo> OnKernelDbgIdRsds { get; set; }
```

#### Property Value

[Action](https://learn.microsoft.com/dotnet/api/system.action-1)<[KernelDbgIdRsdsEventInfo](./nefarius.utilities.etw.events.kerneldbgidrsdseventinfo.md)><br>

### <a id="properties-reporterror"/>**ReportError**

Reports potential scanning errors.

```csharp
public Action<String> ReportError { get; set; }
```

#### Property Value

[Action](https://learn.microsoft.com/dotnet/api/system.action-1)<[String](https://learn.microsoft.com/dotnet/api/system.string)><br>
