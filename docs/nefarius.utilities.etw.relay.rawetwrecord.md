# RawEtwRecord

Namespace: Nefarius.Utilities.ETW.Relay

A pointer-free copy of a native `EVENT_RECORD`.

```csharp
public sealed class RawEtwRecord
```

Inheritance [Object](https://learn.microsoft.com/dotnet/api/system.object) → [RawEtwRecord](./nefarius.utilities.etw.relay.rawetwrecord.md)<br>
Attributes [NullableContextAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullablecontextattribute), [NullableAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullableattribute)

**Remarks:**

The members mirror `EVENT_HEADER` field by field. GUIDs are held as [Guid](https://learn.microsoft.com/dotnet/api/system.guid) values; the wire
 format stores them in RFC 4122 byte order.

## Properties

### <a id="properties-activityid"/>**ActivityId**

`EVENT_HEADER.ActivityId`.

```csharp
public Guid ActivityId { get; set; }
```

#### Property Value

[Guid](https://learn.microsoft.com/dotnet/api/system.guid)<br>

### <a id="properties-buffercontext"/>**BufferContext**

The raw 32-bit `ETW_BUFFER_CONTEXT`; its first byte is the processor number.

```csharp
public uint BufferContext { get; set; }
```

#### Property Value

[UInt32](https://learn.microsoft.com/dotnet/api/system.uint32)<br>

### <a id="properties-channel"/>**Channel**

`EVENT_DESCRIPTOR.Channel`.

```csharp
public byte Channel { get; set; }
```

#### Property Value

[Byte](https://learn.microsoft.com/dotnet/api/system.byte)<br>

### <a id="properties-eventproperty"/>**EventProperty**

`EVENT_HEADER.EventProperty`.

```csharp
public ushort EventProperty { get; set; }
```

#### Property Value

[UInt16](https://learn.microsoft.com/dotnet/api/system.uint16)<br>

### <a id="properties-extendeddata"/>**ExtendedData**

The extended data items, in record order.

```csharp
public List<RawEtwExtendedData> ExtendedData { get; }
```

#### Property Value

[List](https://learn.microsoft.com/dotnet/api/system.collections.generic.list-1)<[RawEtwExtendedData](./nefarius.utilities.etw.relay.rawetwextendeddata.md)><br>

### <a id="properties-flags"/>**Flags**

`EVENT_HEADER.Flags`.

```csharp
public ushort Flags { get; set; }
```

#### Property Value

[UInt16](https://learn.microsoft.com/dotnet/api/system.uint16)<br>

### <a id="properties-headertype"/>**HeaderType**

`EVENT_HEADER.HeaderType`.

```csharp
public ushort HeaderType { get; set; }
```

#### Property Value

[UInt16](https://learn.microsoft.com/dotnet/api/system.uint16)<br>

### <a id="properties-id"/>**Id**

`EVENT_DESCRIPTOR.Id`.

```csharp
public ushort Id { get; set; }
```

#### Property Value

[UInt16](https://learn.microsoft.com/dotnet/api/system.uint16)<br>

### <a id="properties-keyword"/>**Keyword**

`EVENT_DESCRIPTOR.Keyword`.

```csharp
public ulong Keyword { get; set; }
```

#### Property Value

[UInt64](https://learn.microsoft.com/dotnet/api/system.uint64)<br>

### <a id="properties-level"/>**Level**

`EVENT_DESCRIPTOR.Level`.

```csharp
public byte Level { get; set; }
```

#### Property Value

[Byte](https://learn.microsoft.com/dotnet/api/system.byte)<br>

### <a id="properties-opcode"/>**Opcode**

`EVENT_DESCRIPTOR.Opcode`.

```csharp
public byte Opcode { get; set; }
```

#### Property Value

[Byte](https://learn.microsoft.com/dotnet/api/system.byte)<br>

### <a id="properties-payload"/>**Payload**

The event payload (`UserData`), exactly `UserDataLength` bytes.

```csharp
public Byte[] Payload { get; set; }
```

#### Property Value

[Byte[]](https://learn.microsoft.com/dotnet/api/system.byte[])<br>

### <a id="properties-pointersize"/>**PointerSize**

Pointer size of the producing process or system: 4 or 8.

```csharp
public byte PointerSize { get; set; }
```

#### Property Value

[Byte](https://learn.microsoft.com/dotnet/api/system.byte)<br>

### <a id="properties-processid"/>**ProcessId**

`EVENT_HEADER.ProcessId`.

```csharp
public uint ProcessId { get; set; }
```

#### Property Value

[UInt32](https://learn.microsoft.com/dotnet/api/system.uint32)<br>

### <a id="properties-processortime"/>**ProcessorTime**

The 8-byte union of `KernelTime`/`UserTime` and `ProcessorTime`.

```csharp
public ulong ProcessorTime { get; set; }
```

#### Property Value

[UInt64](https://learn.microsoft.com/dotnet/api/system.uint64)<br>

### <a id="properties-providerid"/>**ProviderId**

`EVENT_HEADER.ProviderId`.

```csharp
public Guid ProviderId { get; set; }
```

#### Property Value

[Guid](https://learn.microsoft.com/dotnet/api/system.guid)<br>

### <a id="properties-size"/>**Size**

`EVENT_HEADER.Size`.

```csharp
public ushort Size { get; set; }
```

#### Property Value

[UInt16](https://learn.microsoft.com/dotnet/api/system.uint16)<br>

### <a id="properties-task"/>**Task**

`EVENT_DESCRIPTOR.Task`.

```csharp
public ushort Task { get; set; }
```

#### Property Value

[UInt16](https://learn.microsoft.com/dotnet/api/system.uint16)<br>

### <a id="properties-threadid"/>**ThreadId**

`EVENT_HEADER.ThreadId`.

```csharp
public uint ThreadId { get; set; }
```

#### Property Value

[UInt32](https://learn.microsoft.com/dotnet/api/system.uint32)<br>

### <a id="properties-timestamp"/>**TimeStamp**

`EVENT_HEADER.TimeStamp`, expressed as [RawEtwRecord.TimestampKind](./nefarius.utilities.etw.relay.rawetwrecord.md#timestampkind) says.

```csharp
public long TimeStamp { get; set; }
```

#### Property Value

[Int64](https://learn.microsoft.com/dotnet/api/system.int64)<br>

### <a id="properties-timestampkind"/>**TimestampKind**

How [RawEtwRecord.TimeStamp](./nefarius.utilities.etw.relay.rawetwrecord.md#timestamp) is expressed. Version 1 only defines [RawEtwTimestampKind.FileTime](./nefarius.utilities.etw.relay.rawetwtimestampkind.md#filetime).

```csharp
public RawEtwTimestampKind TimestampKind { get; set; }
```

#### Property Value

[RawEtwTimestampKind](./nefarius.utilities.etw.relay.rawetwtimestampkind.md)<br>

### <a id="properties-version"/>**Version**

`EVENT_DESCRIPTOR.Version`.

```csharp
public byte Version { get; set; }
```

#### Property Value

[Byte](https://learn.microsoft.com/dotnet/api/system.byte)<br>

### <a id="properties-wppsequencenumber"/>**WppSequenceNumber**

The WPP message sequence number, valid only when [RawEtwRecord.WppSequenceValid](./nefarius.utilities.etw.relay.rawetwrecord.md#wppsequencevalid) is set.

```csharp
public uint WppSequenceNumber { get; set; }
```

#### Property Value

[UInt32](https://learn.microsoft.com/dotnet/api/system.uint32)<br>

### <a id="properties-wppsequencevalid"/>**WppSequenceValid**

True when [RawEtwRecord.WppSequenceNumber](./nefarius.utilities.etw.relay.rawetwrecord.md#wppsequencenumber) carries a value.

```csharp
public bool WppSequenceValid { get; set; }
```

#### Property Value

[Boolean](https://learn.microsoft.com/dotnet/api/system.boolean)<br>

## Constructors

### <a id="constructors-.ctor"/>**RawEtwRecord()**

```csharp
public RawEtwRecord()
```
