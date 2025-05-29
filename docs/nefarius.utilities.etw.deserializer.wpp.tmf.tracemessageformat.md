# TraceMessageFormat

Namespace: Nefarius.Utilities.ETW.Deserializer.WPP.TMF

Decoding information for WPP events.

```csharp
public sealed class TraceMessageFormat : System.IEquatable`1[[Nefarius.Utilities.ETW.Deserializer.WPP.TMF.TraceMessageFormat, Nefarius.Utilities.ETW, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null]], System.IComparable`1[[Nefarius.Utilities.ETW.Deserializer.WPP.TMF.TraceMessageFormat, Nefarius.Utilities.ETW, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null]], System.IComparable
```

Inheritance [Object](https://docs.microsoft.com/en-us/dotnet/api/system.object) → [TraceMessageFormat](./nefarius.utilities.etw.deserializer.wpp.tmf.tracemessageformat.md)<br>
Implements [IEquatable&lt;TraceMessageFormat&gt;](https://docs.microsoft.com/en-us/dotnet/api/system.iequatable-1), [IComparable&lt;TraceMessageFormat&gt;](https://docs.microsoft.com/en-us/dotnet/api/system.icomparable-1), [IComparable](https://docs.microsoft.com/en-us/dotnet/api/system.icomparable)

## Properties

### <a id="properties-filename"/>**FileName**

```csharp
public string FileName { get; set; }
```

#### Property Value

[String](https://docs.microsoft.com/en-us/dotnet/api/system.string)<br>

### <a id="properties-flags"/>**Flags**

```csharp
public string Flags { get; set; }
```

#### Property Value

[String](https://docs.microsoft.com/en-us/dotnet/api/system.string)<br>

### <a id="properties-function"/>**Function**

```csharp
public string Function { get; set; }
```

#### Property Value

[String](https://docs.microsoft.com/en-us/dotnet/api/system.string)<br>

### <a id="properties-functionparameters"/>**FunctionParameters**

```csharp
public IReadOnlyList<FunctionParameter> FunctionParameters { get; set; }
```

#### Property Value

[IReadOnlyList&lt;FunctionParameter&gt;](https://docs.microsoft.com/en-us/dotnet/api/system.collections.generic.ireadonlylist-1)<br>

### <a id="properties-id"/>**Id**

```csharp
public int Id { get; set; }
```

#### Property Value

[Int32](https://docs.microsoft.com/en-us/dotnet/api/system.int32)<br>

### <a id="properties-level"/>**Level**

```csharp
public string Level { get; set; }
```

#### Property Value

[String](https://docs.microsoft.com/en-us/dotnet/api/system.string)<br>

### <a id="properties-messageformat"/>**MessageFormat**

```csharp
public string MessageFormat { get; set; }
```

#### Property Value

[String](https://docs.microsoft.com/en-us/dotnet/api/system.string)<br>

### <a id="properties-messageguid"/>**MessageGuid**

```csharp
public Guid MessageGuid { get; set; }
```

#### Property Value

[Guid](https://docs.microsoft.com/en-us/dotnet/api/system.guid)<br>

### <a id="properties-opcode"/>**Opcode**

```csharp
public string Opcode { get; set; }
```

#### Property Value

[String](https://docs.microsoft.com/en-us/dotnet/api/system.string)<br>

### <a id="properties-originalsymbolfilename"/>**OriginalSymbolFileName**

```csharp
public string OriginalSymbolFileName { get; set; }
```

#### Property Value

[String](https://docs.microsoft.com/en-us/dotnet/api/system.string)<br>

### <a id="properties-provider"/>**Provider**

```csharp
public string Provider { get; set; }
```

#### Property Value

[String](https://docs.microsoft.com/en-us/dotnet/api/system.string)<br>

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

`obj` [Object](https://docs.microsoft.com/en-us/dotnet/api/system.object)<br>

#### Returns

[Int32](https://docs.microsoft.com/en-us/dotnet/api/system.int32)

### <a id="methods-compareto"/>**CompareTo(TraceMessageFormat)**

```csharp
public int CompareTo(TraceMessageFormat other)
```

#### Parameters

`other` [TraceMessageFormat](./nefarius.utilities.etw.deserializer.wpp.tmf.tracemessageformat.md)<br>

#### Returns

[Int32](https://docs.microsoft.com/en-us/dotnet/api/system.int32)

### <a id="methods-equals"/>**Equals(TraceMessageFormat)**

```csharp
public bool Equals(TraceMessageFormat other)
```

#### Parameters

`other` [TraceMessageFormat](./nefarius.utilities.etw.deserializer.wpp.tmf.tracemessageformat.md)<br>

#### Returns

[Boolean](https://docs.microsoft.com/en-us/dotnet/api/system.boolean)

### <a id="methods-equals"/>**Equals(Object)**

```csharp
public bool Equals(object obj)
```

#### Parameters

`obj` [Object](https://docs.microsoft.com/en-us/dotnet/api/system.object)<br>

#### Returns

[Boolean](https://docs.microsoft.com/en-us/dotnet/api/system.boolean)

### <a id="methods-gethashcode"/>**GetHashCode()**

```csharp
public int GetHashCode()
```

#### Returns

[Int32](https://docs.microsoft.com/en-us/dotnet/api/system.int32)
