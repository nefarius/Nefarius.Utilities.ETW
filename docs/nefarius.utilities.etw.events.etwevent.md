# EtwEvent

Namespace: Nefarius.Utilities.ETW.Events

A decoded ETW event with typed metadata and properties, independent of any serialization or telemetry format.

```csharp
public sealed class EtwEvent
```

Inheritance [Object](https://learn.microsoft.com/dotnet/api/system.object) → [EtwEvent](./nefarius.utilities.etw.events.etwevent.md)<br>
Attributes [NullableContextAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullablecontextattribute), [NullableAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullableattribute)

## Properties

### <a id="properties-activityid"/>**ActivityId**

The activity ID, or [Guid.Empty](https://learn.microsoft.com/dotnet/api/system.guid.empty).

```csharp
public Guid ActivityId { get; set; }
```

#### Property Value

[Guid](https://learn.microsoft.com/dotnet/api/system.guid)<br>

### <a id="properties-channel"/>**Channel**

The channel.

```csharp
public byte Channel { get; set; }
```

#### Property Value

[Byte](https://learn.microsoft.com/dotnet/api/system.byte)<br>

### <a id="properties-flags"/>**Flags**

The event header flags.

```csharp
public ushort Flags { get; set; }
```

#### Property Value

[UInt16](https://learn.microsoft.com/dotnet/api/system.uint16)<br>

### <a id="properties-id"/>**Id**

The event ID.

```csharp
public ushort Id { get; set; }
```

#### Property Value

[UInt16](https://learn.microsoft.com/dotnet/api/system.uint16)<br>

### <a id="properties-keyword"/>**Keyword**

The keyword bitmask.

```csharp
public ulong Keyword { get; set; }
```

#### Property Value

[UInt64](https://learn.microsoft.com/dotnet/api/system.uint64)<br>

### <a id="properties-level"/>**Level**

The level (1 critical to 5 verbose, 0 unspecified).

```csharp
public byte Level { get; set; }
```

#### Property Value

[Byte](https://learn.microsoft.com/dotnet/api/system.byte)<br>

### <a id="properties-name"/>**Name**

The event name, e.g. `WPP` or the TraceLogging event name.

```csharp
public string Name { get; set; }
```

#### Property Value

[String](https://learn.microsoft.com/dotnet/api/system.string)<br>

### <a id="properties-opcode"/>**Opcode**

The opcode.

```csharp
public byte Opcode { get; set; }
```

#### Property Value

[Byte](https://learn.microsoft.com/dotnet/api/system.byte)<br>

### <a id="properties-processid"/>**ProcessId**

The process ID.

```csharp
public uint ProcessId { get; set; }
```

#### Property Value

[UInt32](https://learn.microsoft.com/dotnet/api/system.uint32)<br>

### <a id="properties-processornumber"/>**ProcessorNumber**

The processor number as the JSON output reports it.

```csharp
public ulong ProcessorNumber { get; set; }
```

#### Property Value

[UInt64](https://learn.microsoft.com/dotnet/api/system.uint64)<br>

### <a id="properties-properties"/>**Properties**

The decoded properties as an [EtwValueKind.Object](./nefarius.utilities.etw.events.etwvaluekind.md#object) value.

```csharp
public EtwValue Properties { get; internal set; }
```

#### Property Value

[EtwValue](./nefarius.utilities.etw.events.etwvalue.md)<br>

### <a id="properties-providerid"/>**ProviderId**

The provider GUID; for WPP events the message GUID.

```csharp
public Guid ProviderId { get; set; }
```

#### Property Value

[Guid](https://learn.microsoft.com/dotnet/api/system.guid)<br>

### <a id="properties-relatedactivityid"/>**RelatedActivityId**

The related activity ID, or [Guid.Empty](https://learn.microsoft.com/dotnet/api/system.guid.empty).

```csharp
public Guid RelatedActivityId { get; set; }
```

#### Property Value

[Guid](https://learn.microsoft.com/dotnet/api/system.guid)<br>

### <a id="properties-stackmatchid"/>**StackMatchId**

The stack match ID, or 0.

```csharp
public ulong StackMatchId { get; set; }
```

#### Property Value

[UInt64](https://learn.microsoft.com/dotnet/api/system.uint64)<br>

### <a id="properties-stacks"/>**Stacks**

The captured stack, or `null`.

```csharp
public IReadOnlyList<UInt64> Stacks { get; set; }
```

#### Property Value

[IReadOnlyList](https://learn.microsoft.com/dotnet/api/system.collections.generic.ireadonlylist-1)<[UInt64](https://learn.microsoft.com/dotnet/api/system.uint64)><br>

### <a id="properties-task"/>**Task**

The task.

```csharp
public ushort Task { get; set; }
```

#### Property Value

[UInt16](https://learn.microsoft.com/dotnet/api/system.uint16)<br>

### <a id="properties-threadid"/>**ThreadId**

The thread ID.

```csharp
public uint ThreadId { get; set; }
```

#### Property Value

[UInt32](https://learn.microsoft.com/dotnet/api/system.uint32)<br>

### <a id="properties-timestamp"/>**Timestamp**

The header timestamp exactly as the JSON output reports it: a FILETIME unless raw timestamps were
 requested.

```csharp
public long Timestamp { get; set; }
```

#### Property Value

[Int64](https://learn.microsoft.com/dotnet/api/system.int64)<br>

### <a id="properties-timestamputc"/>**TimestampUtc**

[EtwEvent.Timestamp](./nefarius.utilities.etw.events.etwevent.md#timestamp) as UTC time, or `null` when it is not a valid FILETIME.

```csharp
public Nullable<DateTime> TimestampUtc { get; }
```

#### Property Value

[Nullable](https://learn.microsoft.com/dotnet/api/system.nullable-1)<[DateTime](https://learn.microsoft.com/dotnet/api/system.datetime)><br>

### <a id="properties-version"/>**Version**

The event version.

```csharp
public byte Version { get; set; }
```

#### Property Value

[Byte](https://learn.microsoft.com/dotnet/api/system.byte)<br>

## Constructors

### <a id="constructors-.ctor"/>**EtwEvent()**

```csharp
public EtwEvent()
```
