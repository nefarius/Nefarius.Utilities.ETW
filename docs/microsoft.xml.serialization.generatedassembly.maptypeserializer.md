# MapTypeSerializer

Namespace: Microsoft.Xml.Serialization.GeneratedAssembly

```csharp
public sealed class MapTypeSerializer : XmlSerializer1
```

Inheritance [Object](https://docs.microsoft.com/en-us/dotnet/api/system.object) → XmlSerializer → [XmlSerializer1](./microsoft.xml.serialization.generatedassembly.xmlserializer1.md) → [MapTypeSerializer](./microsoft.xml.serialization.generatedassembly.maptypeserializer.md)

## Constructors

### <a id="constructors-.ctor"/>**MapTypeSerializer()**

```csharp
public MapTypeSerializer()
```

## Methods

### <a id="methods-candeserialize"/>**CanDeserialize(XmlReader)**

```csharp
public bool CanDeserialize(XmlReader xmlReader)
```

#### Parameters

`xmlReader` XmlReader<br>

#### Returns

[Boolean](https://docs.microsoft.com/en-us/dotnet/api/system.boolean)

### <a id="methods-deserialize"/>**Deserialize(XmlSerializationReader)**

```csharp
protected object Deserialize(XmlSerializationReader reader)
```

#### Parameters

`reader` XmlSerializationReader<br>

#### Returns

[Object](https://docs.microsoft.com/en-us/dotnet/api/system.object)

### <a id="methods-serialize"/>**Serialize(Object, XmlSerializationWriter)**

```csharp
protected void Serialize(object objectToSerialize, XmlSerializationWriter writer)
```

#### Parameters

`objectToSerialize` [Object](https://docs.microsoft.com/en-us/dotnet/api/system.object)<br>

`writer` XmlSerializationWriter<br>

## Events

### <a id="events-unknownattribute"/>**UnknownAttribute**

```csharp
public event XmlAttributeEventHandler UnknownAttribute;
```

### <a id="events-unknownelement"/>**UnknownElement**

```csharp
public event XmlElementEventHandler UnknownElement;
```

### <a id="events-unknownnode"/>**UnknownNode**

```csharp
public event XmlNodeEventHandler UnknownNode;
```

### <a id="events-unreferencedobject"/>**UnreferencedObject**

```csharp
public event UnreferencedObjectEventHandler UnreferencedObject;
```
