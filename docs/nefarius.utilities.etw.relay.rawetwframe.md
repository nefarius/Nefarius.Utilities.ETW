# RawEtwFrame

Namespace: Nefarius.Utilities.ETW.Relay

A decoded relay frame.

```csharp
public sealed class RawEtwFrame
```

Inheritance [Object](https://learn.microsoft.com/dotnet/api/system.object) → [RawEtwFrame](./nefarius.utilities.etw.relay.rawetwframe.md)<br>
Attributes [NullableContextAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullablecontextattribute), [NullableAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullableattribute)

## Properties

### <a id="properties-hello"/>**Hello**

Set for [RawEtwFrameType.Hello](./nefarius.utilities.etw.relay.rawetwframetype.md#hello).

```csharp
public RawEtwHello Hello { get; set; }
```

#### Property Value

[RawEtwHello](./nefarius.utilities.etw.relay.rawetwhello.md)<br>

### <a id="properties-loss"/>**Loss**

Set for [RawEtwFrameType.Loss](./nefarius.utilities.etw.relay.rawetwframetype.md#loss).

```csharp
public RawEtwLoss Loss { get; set; }
```

#### Property Value

[RawEtwLoss](./nefarius.utilities.etw.relay.rawetwloss.md)<br>

### <a id="properties-record"/>**Record**

Set for [RawEtwFrameType.Event](./nefarius.utilities.etw.relay.rawetwframetype.md#event).

```csharp
public RawEtwRecord Record { get; set; }
```

#### Property Value

[RawEtwRecord](./nefarius.utilities.etw.relay.rawetwrecord.md)<br>

### <a id="properties-sequence"/>**Sequence**

The producer's monotonic frame sequence number; the hello frame is 0.

```csharp
public ulong Sequence { get; set; }
```

#### Property Value

[UInt64](https://learn.microsoft.com/dotnet/api/system.uint64)<br>

### <a id="properties-type"/>**Type**

The frame kind.

```csharp
public RawEtwFrameType Type { get; set; }
```

#### Property Value

[RawEtwFrameType](./nefarius.utilities.etw.relay.rawetwframetype.md)<br>

## Constructors

### <a id="constructors-.ctor"/>**RawEtwFrame()**

```csharp
public RawEtwFrame()
```
