# FunctionParameter

Namespace: Nefarius.Utilities.ETW.Deserializer.WPP.TMF

```csharp
public struct FunctionParameter
```

Inheritance [Object](https://docs.microsoft.com/en-us/dotnet/api/system.object) → [ValueType](https://docs.microsoft.com/en-us/dotnet/api/system.valuetype) → [FunctionParameter](./nefarius.utilities.etw.deserializer.wpp.tmf.functionparameter.md)

## Properties

### <a id="properties-expression"/>**Expression**

The expression (variable) passed to the function parameter.

```csharp
public string Expression { get; set; }
```

#### Property Value

[String](https://docs.microsoft.com/en-us/dotnet/api/system.string)<br>

### <a id="properties-index"/>**Index**

The index used in the message format string to substitute this type with.

```csharp
public int Index { get; set; }
```

#### Property Value

[Int32](https://docs.microsoft.com/en-us/dotnet/api/system.int32)<br>

### <a id="properties-listitems"/>**ListItems**

List item values as their string representation.

```csharp
public IReadOnlyDictionary<Int32, String> ListItems { get; set; }
```

#### Property Value

[IReadOnlyDictionary&lt;Int32, String&gt;](https://docs.microsoft.com/en-us/dotnet/api/system.collections.generic.ireadonlydictionary-2)<br>

**Remarks:**

Only populated if [FunctionParameter.Type](./nefarius.utilities.etw.deserializer.wpp.tmf.functionparameter.md#type) is [ItemType.ItemListByte](./nefarius.utilities.etw.deserializer.wpp.tmf.itemtype.md#itemlistbyte).

### <a id="properties-type"/>**Type**

The data type of the function parameter.

```csharp
public ItemType Type { get; set; }
```

#### Property Value

[ItemType](./nefarius.utilities.etw.deserializer.wpp.tmf.itemtype.md)<br>
