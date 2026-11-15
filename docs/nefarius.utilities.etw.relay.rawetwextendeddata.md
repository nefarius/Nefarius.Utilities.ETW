# RawEtwExtendedData

Namespace: Nefarius.Utilities.ETW.Relay

One extended data item of an ETW event, copied by value.

```csharp
public sealed class RawEtwExtendedData
```

Inheritance [Object](https://learn.microsoft.com/dotnet/api/system.object) → [RawEtwExtendedData](./nefarius.utilities.etw.relay.rawetwextendeddata.md)<br>
Attributes [NullableContextAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullablecontextattribute), [NullableAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullableattribute)

## Properties

### <a id="properties-data"/>**Data**

The item data, exactly `DataSize` bytes.

```csharp
public Byte[] Data { get; set; }
```

#### Property Value

[Byte[]](https://learn.microsoft.com/dotnet/api/system.byte[])<br>

### <a id="properties-exttype"/>**ExtType**

The `ExtType` field, e.g. 11 for a TraceLogging schema.

```csharp
public ushort ExtType { get; set; }
```

#### Property Value

[UInt16](https://learn.microsoft.com/dotnet/api/system.uint16)<br>

### <a id="properties-linkage"/>**Linkage**

The 16 bits holding `Linkage` and reserved flags.

```csharp
public ushort Linkage { get; set; }
```

#### Property Value

[UInt16](https://learn.microsoft.com/dotnet/api/system.uint16)<br>

### <a id="properties-reserved1"/>**Reserved1**

The `Reserved1` field of `EVENT_HEADER_EXTENDED_DATA_ITEM`.

```csharp
public ushort Reserved1 { get; set; }
```

#### Property Value

[UInt16](https://learn.microsoft.com/dotnet/api/system.uint16)<br>

## Constructors

### <a id="constructors-.ctor"/>**RawEtwExtendedData()**

```csharp
public RawEtwExtendedData()
```
