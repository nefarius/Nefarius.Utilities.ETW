# EtwValue

Namespace: Nefarius.Utilities.ETW.Events

A typed, transport-neutral value of a decoded event property.

```csharp
public sealed class EtwValue
```

Inheritance [Object](https://learn.microsoft.com/dotnet/api/system.object) → [EtwValue](./nefarius.utilities.etw.events.etwvalue.md)<br>
Attributes [NullableContextAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullablecontextattribute), [NullableAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullableattribute)

**Remarks:**

This is the structured counterpart of the JSON the library otherwise emits. Integers are widened to 64 bits;
 the original ETW width is not retained.

## Properties

### <a id="properties-asbinary"/>**AsBinary**

The value as bytes.

```csharp
public Byte[] AsBinary { get; }
```

#### Property Value

[Byte[]](https://learn.microsoft.com/dotnet/api/system.byte[])<br>

### <a id="properties-asboolean"/>**AsBoolean**

The value as [Boolean](https://learn.microsoft.com/dotnet/api/system.boolean).

```csharp
public bool AsBoolean { get; }
```

#### Property Value

[Boolean](https://learn.microsoft.com/dotnet/api/system.boolean)<br>

### <a id="properties-asdatetime"/>**AsDateTime**

The value as UTC [DateTime](https://learn.microsoft.com/dotnet/api/system.datetime).

```csharp
public DateTime AsDateTime { get; }
```

#### Property Value

[DateTime](https://learn.microsoft.com/dotnet/api/system.datetime)<br>

### <a id="properties-asdouble"/>**AsDouble**

The value as [Double](https://learn.microsoft.com/dotnet/api/system.double).

```csharp
public double AsDouble { get; }
```

#### Property Value

[Double](https://learn.microsoft.com/dotnet/api/system.double)<br>

### <a id="properties-asguid"/>**AsGuid**

The value as [Guid](https://learn.microsoft.com/dotnet/api/system.guid).

```csharp
public Guid AsGuid { get; }
```

#### Property Value

[Guid](https://learn.microsoft.com/dotnet/api/system.guid)<br>

### <a id="properties-asint64"/>**AsInt64**

The value as [Int64](https://learn.microsoft.com/dotnet/api/system.int64).

```csharp
public long AsInt64 { get; }
```

#### Property Value

[Int64](https://learn.microsoft.com/dotnet/api/system.int64)<br>

### <a id="properties-asstring"/>**AsString**

The value as [String](https://learn.microsoft.com/dotnet/api/system.string).

```csharp
public string AsString { get; }
```

#### Property Value

[String](https://learn.microsoft.com/dotnet/api/system.string)<br>

### <a id="properties-asuint64"/>**AsUInt64**

The value as [UInt64](https://learn.microsoft.com/dotnet/api/system.uint64).

```csharp
public ulong AsUInt64 { get; }
```

#### Property Value

[UInt64](https://learn.microsoft.com/dotnet/api/system.uint64)<br>

### <a id="properties-items"/>**Items**

The elements of an [EtwValueKind.Array](./nefarius.utilities.etw.events.etwvaluekind.md#array), otherwise empty.

```csharp
public IReadOnlyList<EtwValue> Items { get; }
```

#### Property Value

[IReadOnlyList](https://learn.microsoft.com/dotnet/api/system.collections.generic.ireadonlylist-1)<[EtwValue](./nefarius.utilities.etw.events.etwvalue.md)><br>

### <a id="properties-kind"/>**Kind**

The kind of this value.

```csharp
public EtwValueKind Kind { get; }
```

#### Property Value

[EtwValueKind](./nefarius.utilities.etw.events.etwvaluekind.md)<br>

### <a id="properties-members"/>**Members**

The members of an [EtwValueKind.Object](./nefarius.utilities.etw.events.etwvaluekind.md#object), otherwise empty. Names may repeat.

```csharp
public IReadOnlyList<KeyValuePair<String, EtwValue>> Members { get; }
```

#### Property Value

[IReadOnlyList](https://learn.microsoft.com/dotnet/api/system.collections.generic.ireadonlylist-1)<[KeyValuePair](https://learn.microsoft.com/dotnet/api/system.collections.generic.keyvaluepair-2)<[String](https://learn.microsoft.com/dotnet/api/system.string), [EtwValue](./nefarius.utilities.etw.events.etwvalue.md)>><br>

### <a id="properties-null"/>**Null**

The shared null value.

```csharp
public static EtwValue Null { get; }
```

#### Property Value

[EtwValue](./nefarius.utilities.etw.events.etwvalue.md)<br>

## Methods

### <a id="methods-fromarray"/>**FromArray(IReadOnlyList&lt;EtwValue&gt;)**

Creates an array value.

```csharp
public static EtwValue FromArray(IReadOnlyList<EtwValue> items)
```

#### Parameters

`items` [IReadOnlyList](https://learn.microsoft.com/dotnet/api/system.collections.generic.ireadonlylist-1)<[EtwValue](./nefarius.utilities.etw.events.etwvalue.md)><br>

#### Returns

[EtwValue](./nefarius.utilities.etw.events.etwvalue.md)

### <a id="methods-frombinary"/>**FromBinary(Byte[])**

Creates a binary value.

```csharp
public static EtwValue FromBinary(Byte[] value)
```

#### Parameters

`value` [Byte[]](https://learn.microsoft.com/dotnet/api/system.byte[])<br>

#### Returns

[EtwValue](./nefarius.utilities.etw.events.etwvalue.md)

### <a id="methods-fromboolean"/>**FromBoolean(Boolean)**

Creates a boolean value.

```csharp
public static EtwValue FromBoolean(bool value)
```

#### Parameters

`value` [Boolean](https://learn.microsoft.com/dotnet/api/system.boolean)<br>

#### Returns

[EtwValue](./nefarius.utilities.etw.events.etwvalue.md)

### <a id="methods-fromdatetime"/>**FromDateTime(DateTime)**

Creates a date and time value.

```csharp
public static EtwValue FromDateTime(DateTime value)
```

#### Parameters

`value` [DateTime](https://learn.microsoft.com/dotnet/api/system.datetime)<br>

#### Returns

[EtwValue](./nefarius.utilities.etw.events.etwvalue.md)

### <a id="methods-fromdouble"/>**FromDouble(Double)**

Creates a floating point value.

```csharp
public static EtwValue FromDouble(double value)
```

#### Parameters

`value` [Double](https://learn.microsoft.com/dotnet/api/system.double)<br>

#### Returns

[EtwValue](./nefarius.utilities.etw.events.etwvalue.md)

### <a id="methods-fromguid"/>**FromGuid(Guid)**

Creates a GUID value.

```csharp
public static EtwValue FromGuid(Guid value)
```

#### Parameters

`value` [Guid](https://learn.microsoft.com/dotnet/api/system.guid)<br>

#### Returns

[EtwValue](./nefarius.utilities.etw.events.etwvalue.md)

### <a id="methods-fromint64"/>**FromInt64(Int64)**

Creates a signed integer value.

```csharp
public static EtwValue FromInt64(long value)
```

#### Parameters

`value` [Int64](https://learn.microsoft.com/dotnet/api/system.int64)<br>

#### Returns

[EtwValue](./nefarius.utilities.etw.events.etwvalue.md)

### <a id="methods-fromobject"/>**FromObject(IReadOnlyList&lt;KeyValuePair&lt;String, EtwValue&gt;&gt;)**

Creates an object value.

```csharp
public static EtwValue FromObject(IReadOnlyList<KeyValuePair<String, EtwValue>> members)
```

#### Parameters

`members` [IReadOnlyList](https://learn.microsoft.com/dotnet/api/system.collections.generic.ireadonlylist-1)<[KeyValuePair](https://learn.microsoft.com/dotnet/api/system.collections.generic.keyvaluepair-2)<[String](https://learn.microsoft.com/dotnet/api/system.string), [EtwValue](./nefarius.utilities.etw.events.etwvalue.md)>><br>

#### Returns

[EtwValue](./nefarius.utilities.etw.events.etwvalue.md)

### <a id="methods-fromstring"/>**FromString(String)**

Creates a text value; `null` becomes [EtwValue.Null](./nefarius.utilities.etw.events.etwvalue.md#null).

```csharp
public static EtwValue FromString(string value)
```

#### Parameters

`value` [String](https://learn.microsoft.com/dotnet/api/system.string)<br>

#### Returns

[EtwValue](./nefarius.utilities.etw.events.etwvalue.md)

### <a id="methods-fromuint64"/>**FromUInt64(UInt64)**

Creates an unsigned integer value.

```csharp
public static EtwValue FromUInt64(ulong value)
```

#### Parameters

`value` [UInt64](https://learn.microsoft.com/dotnet/api/system.uint64)<br>

#### Returns

[EtwValue](./nefarius.utilities.etw.events.etwvalue.md)

### <a id="methods-tostring"/>**ToString()**

```csharp
public string ToString()
```

#### Returns

[String](https://learn.microsoft.com/dotnet/api/system.string)
