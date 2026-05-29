# KernelDbgIdRsdsEventInfo

Namespace: Nefarius.Utilities.ETW.Events

Strongly-typed representation of a `MSNT_SystemTrace/EventTrace/DbgIdRSDS` event emitted by the
 legacy kernel Event Trace provider (EventTraceGuid).

```csharp
public record struct KernelDbgIdRsdsEventInfo
```

Inheritance [Object](https://learn.microsoft.com/dotnet/api/system.object) → [ValueType](https://learn.microsoft.com/dotnet/api/system.valuetype) → [KernelDbgIdRsdsEventInfo](./nefarius.utilities.etw.events.kerneldbgidrsdseventinfo.md)<br>
Attributes [IsReadOnlyAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.isreadonlyattribute)

**Remarks:**

The layout of this event is: `Guid` (16 bytes) + `Age` (uint32) + null-terminated ANSI PDB name.
 This is distinct from the [DbgIdRsdsEventInfo](./nefarius.utilities.etw.events.dbgidrsdseventinfo.md) emitted by the
 `KernelTraceControl/ImageID` provider, which prepends `ImageBase` and `ProcessId`.

## Properties

### <a id="properties-age"/>**Age**

PDB age (revision counter).

```csharp
public uint Age { get; set; }
```

#### Property Value

[UInt32](https://learn.microsoft.com/dotnet/api/system.uint32)<br>

### <a id="properties-guid"/>**Guid**

The GUID embedded in the PDB that uniquely identifies the symbol file.

```csharp
public Guid Guid { get; set; }
```

#### Property Value

[Guid](https://learn.microsoft.com/dotnet/api/system.guid)<br>

### <a id="properties-pdbname"/>**PdbName**

Original file name of the PDB (may be a full path recorded at build time).

```csharp
public string PdbName { get; set; }
```

#### Property Value

[String](https://learn.microsoft.com/dotnet/api/system.string)<br>

### <a id="properties-processid"/>**ProcessId**

Process ID of the process that logged the event.

```csharp
public uint ProcessId { get; set; }
```

#### Property Value

[UInt32](https://learn.microsoft.com/dotnet/api/system.uint32)<br>

### <a id="properties-threadid"/>**ThreadId**

Thread ID of the thread that logged the event.

```csharp
public uint ThreadId { get; set; }
```

#### Property Value

[UInt32](https://learn.microsoft.com/dotnet/api/system.uint32)<br>

### <a id="properties-timestamp"/>**Timestamp**

Raw event timestamp (100-nanosecond intervals since January 1, 1601).

```csharp
public long Timestamp { get; set; }
```

#### Property Value

[Int64](https://learn.microsoft.com/dotnet/api/system.int64)<br>

## Methods

### <a id="methods-equals"/>**Equals(KernelDbgIdRsdsEventInfo)**

```csharp
bool Equals(KernelDbgIdRsdsEventInfo other)
```

#### Parameters

`other` [KernelDbgIdRsdsEventInfo](./nefarius.utilities.etw.events.kerneldbgidrsdseventinfo.md)<br>

#### Returns

[Boolean](https://learn.microsoft.com/dotnet/api/system.boolean)

### <a id="methods-topdbmetadata"/>**ToPdbMetaData()**

Projects this event into the [PdbMetaData](./nefarius.utilities.etw.deserializer.wpp.pdbmetadata.md) structure used for symbol server lookups.

```csharp
PdbMetaData ToPdbMetaData()
```

#### Returns

[PdbMetaData](./nefarius.utilities.etw.deserializer.wpp.pdbmetadata.md)

#### Exceptions

[ArgumentOutOfRangeException](https://learn.microsoft.com/dotnet/api/system.argumentoutofrangeexception)<br>
Thrown when [KernelDbgIdRsdsEventInfo.Age](./nefarius.utilities.etw.events.kerneldbgidrsdseventinfo.md#age) exceeds [Int32.MaxValue](https://learn.microsoft.com/dotnet/api/system.int32.maxvalue) and cannot be safely narrowed.
