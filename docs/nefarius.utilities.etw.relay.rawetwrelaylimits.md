# RawEtwRelayLimits

Namespace: Nefarius.Utilities.ETW.Relay

Resource limits the relay codec enforces before it allocates memory for, or decodes, a frame received from an
 untrusted producer.

```csharp
public sealed class RawEtwRelayLimits
```

Inheritance [Object](https://learn.microsoft.com/dotnet/api/system.object) → [RawEtwRelayLimits](./nefarius.utilities.etw.relay.rawetwrelaylimits.md)<br>
Attributes [NullableContextAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullablecontextattribute), [NullableAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullableattribute)

**Remarks:**

All limits are checked against the lengths a frame declares, before any buffer sized from them is allocated.

## Fields

### <a id="fields-absolutemaxcachedeventkeys"/>**AbsoluteMaxCachedEventKeys**

Largest number of distinct event keys one session may cache parsers for.

```csharp
public static int AbsoluteMaxCachedEventKeys;
```

### <a id="fields-absolutemaxextendeditems"/>**AbsoluteMaxExtendedItems**

Largest number of extended data items a single event may carry.

```csharp
public static int AbsoluteMaxExtendedItems;
```

### <a id="fields-absolutemaxframelength"/>**AbsoluteMaxFrameLength**

Largest value any limit can be raised to.

```csharp
public static int AbsoluteMaxFrameLength;
```

### <a id="fields-absolutemaxmanifestbytes"/>**AbsoluteMaxManifestBytes**

Largest assembled manifest a session may retain.

```csharp
public static int AbsoluteMaxManifestBytes;
```

## Properties

### <a id="properties-default"/>**Default**

The limits applied when none are supplied.

```csharp
public static RawEtwRelayLimits Default { get; }
```

#### Property Value

[RawEtwRelayLimits](./nefarius.utilities.etw.relay.rawetwrelaylimits.md)<br>

### <a id="properties-maxcachedeventkeys"/>**MaxCachedEventKeys**

Maximum distinct wire keys one session accepts, and the maximum parsers it retains. Parsers installed from
 embedded kernel metadata count toward that parser ceiling, and a repeated metadata target reuses the parser
 already compiled for it. Further new keys are rejected. Default: 8192.

```csharp
public int MaxCachedEventKeys { get; set; }
```

#### Property Value

[Int32](https://learn.microsoft.com/dotnet/api/system.int32)<br>

### <a id="properties-maxextendeditems"/>**MaxExtendedItems**

Maximum number of extended data items per event. Default: 64.

```csharp
public int MaxExtendedItems { get; set; }
```

#### Property Value

[Int32](https://learn.microsoft.com/dotnet/api/system.int32)<br>

### <a id="properties-maxextendeditemsize"/>**MaxExtendedItemSize**

Maximum size of one extended data item in bytes. Default and maximum: 65535, the width of the ETW
 `DataSize` field.

```csharp
public int MaxExtendedItemSize { get; set; }
```

#### Property Value

[Int32](https://learn.microsoft.com/dotnet/api/system.int32)<br>

### <a id="properties-maxframelength"/>**MaxFrameLength**

Maximum total frame length in bytes, prefix included. Default: 1 MiB.

```csharp
public int MaxFrameLength { get; set; }
```

#### Property Value

[Int32](https://learn.microsoft.com/dotnet/api/system.int32)<br>

### <a id="properties-maxmanifestbytes"/>**MaxManifestBytes**

Maximum total EventSource manifest text one session retains. Further manifest bytes are rejected.
 Default: 1 MiB.

```csharp
public int MaxManifestBytes { get; set; }
```

#### Property Value

[Int32](https://learn.microsoft.com/dotnet/api/system.int32)<br>

### <a id="properties-maxpayloadlength"/>**MaxPayloadLength**

Maximum event payload size in bytes. Default and maximum: 65535, the width of the ETW
 `UserDataLength` field.

```csharp
public int MaxPayloadLength { get; set; }
```

#### Property Value

[Int32](https://learn.microsoft.com/dotnet/api/system.int32)<br>

### <a id="properties-maxproducerstringbytes"/>**MaxProducerStringBytes**

Maximum length of the producer description string in a hello frame. Default: 256.

```csharp
public int MaxProducerStringBytes { get; set; }
```

#### Property Value

[Int32](https://learn.microsoft.com/dotnet/api/system.int32)<br>

### <a id="properties-maxtotalextendedbytes"/>**MaxTotalExtendedBytes**

Maximum combined size of all extended data items of one event. Default: 512 KiB.

```csharp
public int MaxTotalExtendedBytes { get; set; }
```

#### Property Value

[Int32](https://learn.microsoft.com/dotnet/api/system.int32)<br>

### <a id="properties-requirecrc"/>**RequireCrc**

When `true`, frames without a CRC32C are rejected. Default: `false`.

```csharp
public bool RequireCrc { get; set; }
```

#### Property Value

[Boolean](https://learn.microsoft.com/dotnet/api/system.boolean)<br>

## Constructors

### <a id="constructors-.ctor"/>**RawEtwRelayLimits()**

```csharp
public RawEtwRelayLimits()
```

## Methods

### <a id="methods-validate"/>**Validate()**

```csharp
internal void Validate()
```
