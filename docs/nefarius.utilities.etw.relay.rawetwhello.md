# RawEtwHello

Namespace: Nefarius.Utilities.ETW.Relay

Content of a hello frame.

```csharp
public sealed class RawEtwHello
```

Inheritance [Object](https://learn.microsoft.com/dotnet/api/system.object) → [RawEtwHello](./nefarius.utilities.etw.relay.rawetwhello.md)<br>
Attributes [NullableContextAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullablecontextattribute), [NullableAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullableattribute)

## Properties

### <a id="properties-capabilities"/>**Capabilities**

Producer capability bits. None are defined in version 1; send 0.

```csharp
public uint Capabilities { get; set; }
```

#### Property Value

[UInt32](https://learn.microsoft.com/dotnet/api/system.uint32)<br>

### <a id="properties-maxframelength"/>**MaxFrameLength**

The largest frame the producer will send.

```csharp
public uint MaxFrameLength { get; set; }
```

#### Property Value

[UInt32](https://learn.microsoft.com/dotnet/api/system.uint32)<br>

### <a id="properties-producer"/>**Producer**

A free-form UTF-8 description of the producer, for diagnostics.

```csharp
public string Producer { get; set; }
```

#### Property Value

[String](https://learn.microsoft.com/dotnet/api/system.string)<br>

### <a id="properties-sessionid"/>**SessionId**

Identifies the capture session. Decoder state is tied to this value. A producer that restarts its capture
 must pick a new one.

```csharp
public Guid SessionId { get; set; }
```

#### Property Value

[Guid](https://learn.microsoft.com/dotnet/api/system.guid)<br>

## Constructors

### <a id="constructors-.ctor"/>**RawEtwHello()**

```csharp
public RawEtwHello()
```
