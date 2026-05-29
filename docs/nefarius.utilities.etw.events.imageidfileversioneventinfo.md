# ImageIdFileVersionEventInfo

Namespace: Nefarius.Utilities.ETW.Events

Strongly-typed representation of a `KernelTraceControl/ImageID/FileVersion` event (opcode 64,
 provider `b3e675d7-2554-4f18-830b-2762732560de`).

```csharp
public record struct ImageIdFileVersionEventInfo
```

Inheritance [Object](https://learn.microsoft.com/dotnet/api/system.object) → [ValueType](https://learn.microsoft.com/dotnet/api/system.valuetype) → [ImageIdFileVersionEventInfo](./nefarius.utilities.etw.events.imageidfileversioneventinfo.md)<br>
Attributes [NullableContextAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullablecontextattribute), [NullableAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullableattribute), [IsReadOnlyAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.isreadonlyattribute)

## Properties

### <a id="properties-binfileversion"/>**BinFileVersion**

Binary (numeric) file version from the version resource.

```csharp
public string BinFileVersion { get; set; }
```

#### Property Value

[String](https://learn.microsoft.com/dotnet/api/system.string)<br>

### <a id="properties-companyname"/>**CompanyName**

Company name from the version resource.

```csharp
public string CompanyName { get; set; }
```

#### Property Value

[String](https://learn.microsoft.com/dotnet/api/system.string)<br>

### <a id="properties-filedescription"/>**FileDescription**

Human-readable file description from the version resource.

```csharp
public string FileDescription { get; set; }
```

#### Property Value

[String](https://learn.microsoft.com/dotnet/api/system.string)<br>

### <a id="properties-fileid"/>**FileId**

File identifier.

```csharp
public string FileId { get; set; }
```

#### Property Value

[String](https://learn.microsoft.com/dotnet/api/system.string)<br>

### <a id="properties-fileversion"/>**FileVersion**

File version string from the version resource.

```csharp
public string FileVersion { get; set; }
```

#### Property Value

[String](https://learn.microsoft.com/dotnet/api/system.string)<br>

### <a id="properties-imagesize"/>**ImageSize**

Size of the image in bytes.

```csharp
public uint ImageSize { get; set; }
```

#### Property Value

[UInt32](https://learn.microsoft.com/dotnet/api/system.uint32)<br>

### <a id="properties-origfilename"/>**OrigFileName**

Original file name of the image.

```csharp
public string OrigFileName { get; set; }
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

### <a id="properties-productname"/>**ProductName**

Product name from the version resource.

```csharp
public string ProductName { get; set; }
```

#### Property Value

[String](https://learn.microsoft.com/dotnet/api/system.string)<br>

### <a id="properties-productversion"/>**ProductVersion**

Product version string from the version resource.

```csharp
public string ProductVersion { get; set; }
```

#### Property Value

[String](https://learn.microsoft.com/dotnet/api/system.string)<br>

### <a id="properties-programid"/>**ProgramId**

Program identifier.

```csharp
public string ProgramId { get; set; }
```

#### Property Value

[String](https://learn.microsoft.com/dotnet/api/system.string)<br>

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

### <a id="properties-verlanguage"/>**VerLanguage**

Language identifier of the version resource.

```csharp
public string VerLanguage { get; set; }
```

#### Property Value

[String](https://learn.microsoft.com/dotnet/api/system.string)<br>

## Methods

### <a id="methods-equals"/>**Equals(ImageIdFileVersionEventInfo)**

```csharp
bool Equals(ImageIdFileVersionEventInfo other)
```

#### Parameters

`other` [ImageIdFileVersionEventInfo](./nefarius.utilities.etw.events.imageidfileversioneventinfo.md)<br>

#### Returns

[Boolean](https://learn.microsoft.com/dotnet/api/system.boolean)
