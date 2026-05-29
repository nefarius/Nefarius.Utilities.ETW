# DbgIdRsdsEventInfo

Namespace: Nefarius.Utilities.ETW.Events

Strongly-typed representation of a `KernelTraceControl/ImageID/DbgID_RSDS` event (opcode 36,
 provider `b3e675d7-2554-4f18-830b-2762732560de`).

```csharp
public record struct DbgIdRsdsEventInfo
```

Inheritance [Object](https://learn.microsoft.com/dotnet/api/system.object) → [ValueType](https://learn.microsoft.com/dotnet/api/system.valuetype) → [DbgIdRsdsEventInfo](./nefarius.utilities.etw.events.dbgidrsdseventinfo.md)<br>
Attributes [IsReadOnlyAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.isreadonlyattribute)

## Properties

### <a id="properties-age"/>**Age**

PDB age (revision counter).

```csharp
public uint Age { get; set; }
```

#### Property Value

[UInt32](https://learn.microsoft.com/dotnet/api/system.uint32)<br>

### <a id="properties-guidsig"/>**GuidSig**

The GUID embedded in the PDB that uniquely identifies the symbol file.

```csharp
public Guid GuidSig { get; set; }
```

#### Property Value

[Guid](https://learn.microsoft.com/dotnet/api/system.guid)<br>

### <a id="properties-imagebase"/>**ImageBase**

Base address of the image at the time the debug information was recorded.

```csharp
public ulong ImageBase { get; set; }
```

#### Property Value

[UInt64](https://learn.microsoft.com/dotnet/api/system.uint64)<br>

### <a id="properties-pdbfilename"/>**PdbFileName**

Original file name of the PDB (may be a full path recorded at build time).

```csharp
public string PdbFileName { get; set; }
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

### <a id="methods-equals"/>**Equals(DbgIdRsdsEventInfo)**

```csharp
bool Equals(DbgIdRsdsEventInfo other)
```

#### Parameters

`other` [DbgIdRsdsEventInfo](./nefarius.utilities.etw.events.dbgidrsdseventinfo.md)<br>

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
Thrown when [DbgIdRsdsEventInfo.Age](./nefarius.utilities.etw.events.dbgidrsdseventinfo.md#age) exceeds [Int32.MaxValue](https://learn.microsoft.com/dotnet/api/system.int32.maxvalue) and cannot be safely narrowed.
