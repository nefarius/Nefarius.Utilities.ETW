# RawEtwLoss

Namespace: Nefarius.Utilities.ETW.Relay

Content of a loss frame.

```csharp
public sealed class RawEtwLoss
```

Inheritance [Object](https://learn.microsoft.com/dotnet/api/system.object) → [RawEtwLoss](./nefarius.utilities.etw.relay.rawetwloss.md)

## Properties

### <a id="properties-lostevents"/>**LostEvents**

The number of events dropped immediately before this frame.

```csharp
public ulong LostEvents { get; set; }
```

#### Property Value

[UInt64](https://learn.microsoft.com/dotnet/api/system.uint64)<br>

### <a id="properties-reason"/>**Reason**

A producer defined reason code. 0 means unspecified, 1 a full producer queue, 2 ETW buffer loss.

```csharp
public uint Reason { get; set; }
```

#### Property Value

[UInt32](https://learn.microsoft.com/dotnet/api/system.uint32)<br>

## Constructors

### <a id="constructors-.ctor"/>**RawEtwLoss()**

```csharp
public RawEtwLoss()
```
