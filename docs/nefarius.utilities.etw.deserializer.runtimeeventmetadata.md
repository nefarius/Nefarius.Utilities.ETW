# RuntimeEventMetadata

Namespace: Nefarius.Utilities.ETW.Deserializer

```csharp
public struct RuntimeEventMetadata
```

Inheritance [Object](https://docs.microsoft.com/en-us/dotnet/api/system.object) → [ValueType](https://docs.microsoft.com/en-us/dotnet/api/system.valuetype) → [RuntimeEventMetadata](./nefarius.utilities.etw.deserializer.runtimeeventmetadata.md)

## Properties

### <a id="properties-activityid"/>**ActivityId**

```csharp
public Guid ActivityId { get; }
```

#### Property Value

[Guid](https://docs.microsoft.com/en-us/dotnet/api/system.guid)<br>

### <a id="properties-eventid"/>**EventId**

```csharp
public ushort EventId { get; }
```

#### Property Value

[UInt16](https://docs.microsoft.com/en-us/dotnet/api/system.uint16)<br>

### <a id="properties-flags"/>**Flags**

```csharp
public ushort Flags { get; }
```

#### Property Value

[UInt16](https://docs.microsoft.com/en-us/dotnet/api/system.uint16)<br>

### <a id="properties-processid"/>**ProcessId**

```csharp
public uint ProcessId { get; }
```

#### Property Value

[UInt32](https://docs.microsoft.com/en-us/dotnet/api/system.uint32)<br>

### <a id="properties-processornumber"/>**ProcessorNumber**

```csharp
public ulong ProcessorNumber { get; }
```

#### Property Value

[UInt64](https://docs.microsoft.com/en-us/dotnet/api/system.uint64)<br>

### <a id="properties-providerid"/>**ProviderId**

```csharp
public Guid ProviderId { get; }
```

#### Property Value

[Guid](https://docs.microsoft.com/en-us/dotnet/api/system.guid)<br>

### <a id="properties-relatedactivityid"/>**RelatedActivityId**

```csharp
public Guid RelatedActivityId { get; }
```

#### Property Value

[Guid](https://docs.microsoft.com/en-us/dotnet/api/system.guid)<br>

### <a id="properties-threadid"/>**ThreadId**

```csharp
public uint ThreadId { get; }
```

#### Property Value

[UInt32](https://docs.microsoft.com/en-us/dotnet/api/system.uint32)<br>

### <a id="properties-timestamp"/>**Timestamp**

```csharp
public long Timestamp { get; }
```

#### Property Value

[Int64](https://docs.microsoft.com/en-us/dotnet/api/system.int64)<br>

### <a id="properties-userdatalength"/>**UserDataLength**

```csharp
public ushort UserDataLength { get; }
```

#### Property Value

[UInt16](https://docs.microsoft.com/en-us/dotnet/api/system.uint16)<br>

## Methods

### <a id="methods-getstacks"/>**GetStacks(ref UInt64)**

```csharp
UInt64[] GetStacks(ref UInt64 matchId)
```

#### Parameters

`matchId` [UInt64&](https://docs.microsoft.com/en-us/dotnet/api/system.uint64&)<br>

#### Returns

[UInt64[]](https://docs.microsoft.com/en-us/dotnet/api/system.uint64)
