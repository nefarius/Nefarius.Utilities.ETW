# XmlSerializerContract

Namespace: Microsoft.Xml.Serialization.GeneratedAssembly

```csharp
public class XmlSerializerContract : System.Xml.Serialization.XmlSerializerImplementation
```

Inheritance [Object](https://docs.microsoft.com/en-us/dotnet/api/system.object) → XmlSerializerImplementation → [XmlSerializerContract](./microsoft.xml.serialization.generatedassembly.xmlserializercontract.md)

## Properties

### <a id="properties-reader"/>**Reader**

```csharp
public XmlSerializationReader Reader { get; }
```

#### Property Value

XmlSerializationReader<br>

### <a id="properties-readmethods"/>**ReadMethods**

```csharp
public Hashtable ReadMethods { get; }
```

#### Property Value

[Hashtable](https://docs.microsoft.com/en-us/dotnet/api/system.collections.hashtable)<br>

### <a id="properties-typedserializers"/>**TypedSerializers**

```csharp
public Hashtable TypedSerializers { get; }
```

#### Property Value

[Hashtable](https://docs.microsoft.com/en-us/dotnet/api/system.collections.hashtable)<br>

### <a id="properties-writemethods"/>**WriteMethods**

```csharp
public Hashtable WriteMethods { get; }
```

#### Property Value

[Hashtable](https://docs.microsoft.com/en-us/dotnet/api/system.collections.hashtable)<br>

### <a id="properties-writer"/>**Writer**

```csharp
public XmlSerializationWriter Writer { get; }
```

#### Property Value

XmlSerializationWriter<br>

## Constructors

### <a id="constructors-.ctor"/>**XmlSerializerContract()**

```csharp
public XmlSerializerContract()
```

## Methods

### <a id="methods-canserialize"/>**CanSerialize(Type)**

```csharp
public bool CanSerialize(Type type)
```

#### Parameters

`type` [Type](https://docs.microsoft.com/en-us/dotnet/api/system.type)<br>

#### Returns

[Boolean](https://docs.microsoft.com/en-us/dotnet/api/system.boolean)

### <a id="methods-getserializer"/>**GetSerializer(Type)**

```csharp
public XmlSerializer GetSerializer(Type type)
```

#### Parameters

`type` [Type](https://docs.microsoft.com/en-us/dotnet/api/system.type)<br>

#### Returns

XmlSerializer
