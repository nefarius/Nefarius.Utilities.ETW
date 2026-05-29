# TraceMessageFormat

Namespace: Nefarius.Utilities.ETW.Deserializer.WPP.TMF

Decoding information for WPP events.

```csharp
public sealed class TraceMessageFormat : System.IEquatable<Nefarius.Utilities.ETW.Deserializer.WPP.TMF.TraceMessageFormat>, System.IComparable<Nefarius.Utilities.ETW.Deserializer.WPP.TMF.TraceMessageFormat>, System.IComparable
```

Inheritance [Object](https://learn.microsoft.com/dotnet/api/system.object) → [TraceMessageFormat](./nefarius.utilities.etw.deserializer.wpp.tmf.tracemessageformat.md)<br>
Implements [IEquatable](https://learn.microsoft.com/dotnet/api/system.iequatable-1)<[TraceMessageFormat](./nefarius.utilities.etw.deserializer.wpp.tmf.tracemessageformat.md)>, [IComparable](https://learn.microsoft.com/dotnet/api/system.icomparable-1)<[TraceMessageFormat](./nefarius.utilities.etw.deserializer.wpp.tmf.tracemessageformat.md)>, [IComparable](https://learn.microsoft.com/dotnet/api/system.icomparable)<br>
Attributes [NullableContextAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullablecontextattribute), [NullableAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullableattribute), [RequiredMemberAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.requiredmemberattribute)

## Properties

### <a id="properties-filename"/>**FileName**

```csharp
public string FileName { get; set; }
```

#### Property Value

[String](https://learn.microsoft.com/dotnet/api/system.string)<br>

### <a id="properties-flags"/>**Flags**

```csharp
public string Flags { get; set; }
```

#### Property Value

[String](https://learn.microsoft.com/dotnet/api/system.string)<br>

### <a id="properties-function"/>**Function**

```csharp
public string Function { get; set; }
```

#### Property Value

[String](https://learn.microsoft.com/dotnet/api/system.string)<br>

### <a id="properties-functionparameters"/>**FunctionParameters**

```csharp
public IReadOnlyList<FunctionParameter> FunctionParameters { get; set; }
```

#### Property Value

[IReadOnlyList](https://learn.microsoft.com/dotnet/api/system.collections.generic.ireadonlylist-1)<[FunctionParameter](./nefarius.utilities.etw.deserializer.wpp.tmf.functionparameter.md)><br>

### <a id="properties-id"/>**Id**

```csharp
public int Id { get; set; }
```

#### Property Value

[Int32](https://learn.microsoft.com/dotnet/api/system.int32)<br>

### <a id="properties-level"/>**Level**

```csharp
public string Level { get; set; }
```

#### Property Value

[String](https://learn.microsoft.com/dotnet/api/system.string)<br>

### <a id="properties-messageformat"/>**MessageFormat**

```csharp
public string MessageFormat { get; set; }
```

#### Property Value

[String](https://learn.microsoft.com/dotnet/api/system.string)<br>

### <a id="properties-messageguid"/>**MessageGuid**

```csharp
public Guid MessageGuid { get; set; }
```

#### Property Value

[Guid](https://learn.microsoft.com/dotnet/api/system.guid)<br>

### <a id="properties-opcode"/>**Opcode**

```csharp
public string Opcode { get; set; }
```

#### Property Value

[String](https://learn.microsoft.com/dotnet/api/system.string)<br>

### <a id="properties-originalsymbolfilename"/>**OriginalSymbolFileName**

```csharp
public string OriginalSymbolFileName { get; set; }
```

#### Property Value

[String](https://learn.microsoft.com/dotnet/api/system.string)<br>

### <a id="properties-provider"/>**Provider**

```csharp
public string Provider { get; set; }
```

#### Property Value

[String](https://learn.microsoft.com/dotnet/api/system.string)<br>

## Constructors

### <a id="constructors-.ctor"/>**TraceMessageFormat()**

#### Caution

Constructors of types with required members are not supported in this version of your compiler.

---

```csharp
public TraceMessageFormat()
```

## Methods

### <a id="methods-compareto"/>**CompareTo(Object)**

```csharp
public int CompareTo(object obj)
```

#### Parameters

`obj` [Object](https://learn.microsoft.com/dotnet/api/system.object)<br>

#### Returns

[Int32](https://learn.microsoft.com/dotnet/api/system.int32)

### <a id="methods-compareto"/>**CompareTo(TraceMessageFormat)**

```csharp
public int CompareTo(TraceMessageFormat other)
```

#### Parameters

`other` [TraceMessageFormat](./nefarius.utilities.etw.deserializer.wpp.tmf.tracemessageformat.md)<br>

#### Returns

[Int32](https://learn.microsoft.com/dotnet/api/system.int32)

### <a id="methods-equals"/>**Equals(TraceMessageFormat)**

```csharp
public bool Equals(TraceMessageFormat other)
```

#### Parameters

`other` [TraceMessageFormat](./nefarius.utilities.etw.deserializer.wpp.tmf.tracemessageformat.md)<br>

#### Returns

[Boolean](https://learn.microsoft.com/dotnet/api/system.boolean)

### <a id="methods-equals"/>**Equals(Object)**

```csharp
public bool Equals(object obj)
```

#### Parameters

`obj` [Object](https://learn.microsoft.com/dotnet/api/system.object)<br>

#### Returns

[Boolean](https://learn.microsoft.com/dotnet/api/system.boolean)

### <a id="methods-gethashcode"/>**GetHashCode()**

```csharp
public int GetHashCode()
```

#### Returns

[Int32](https://learn.microsoft.com/dotnet/api/system.int32)
