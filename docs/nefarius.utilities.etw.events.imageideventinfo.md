# ImageIdEventInfo

Namespace: Nefarius.Utilities.ETW.Events

Strongly-typed representation of a `KernelTraceControl/ImageID` event (opcode 0,
 provider `b3e675d7-2554-4f18-830b-2762732560de`).

```csharp
public record struct ImageIdEventInfo
```

Inheritance [Object](https://learn.microsoft.com/dotnet/api/system.object) → [ValueType](https://learn.microsoft.com/dotnet/api/system.valuetype) → [ImageIdEventInfo](./nefarius.utilities.etw.events.imageideventinfo.md)<br>
Attributes [IsReadOnlyAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.isreadonlyattribute)

## Properties

### <a id="properties-imagebase"/>**ImageBase**

Base virtual address at which the image was loaded.

```csharp
public ulong ImageBase { get; set; }
```

#### Property Value

[UInt64](https://learn.microsoft.com/dotnet/api/system.uint64)<br>

### <a id="properties-imagesize"/>**ImageSize**

Size of the image in bytes.

```csharp
public uint ImageSize { get; set; }
```

#### Property Value

[UInt32](https://learn.microsoft.com/dotnet/api/system.uint32)<br>

### <a id="properties-originalfilename"/>**OriginalFileName**

Original file name of the image as recorded in the trace.

```csharp
public string OriginalFileName { get; set; }
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

### <a id="properties-timedatestamp"/>**TimeDateStamp**

PE timestamp / date-stamp from the image header.

```csharp
public uint TimeDateStamp { get; set; }
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

### <a id="methods-equals"/>**Equals(ImageIdEventInfo)**

```csharp
bool Equals(ImageIdEventInfo other)
```

#### Parameters

`other` [ImageIdEventInfo](./nefarius.utilities.etw.events.imageideventinfo.md)<br>

#### Returns

[Boolean](https://learn.microsoft.com/dotnet/api/system.boolean)
