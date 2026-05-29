# FunctionParameter

Namespace: Nefarius.Utilities.ETW.Deserializer.WPP.TMF

```csharp
public struct FunctionParameter
```

Inheritance [Object](https://learn.microsoft.com/dotnet/api/system.object) → [ValueType](https://learn.microsoft.com/dotnet/api/system.valuetype) → [FunctionParameter](./nefarius.utilities.etw.deserializer.wpp.tmf.functionparameter.md)<br>
Attributes [NullableContextAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullablecontextattribute), [NullableAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullableattribute), [IsReadOnlyAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.isreadonlyattribute), [RequiredMemberAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.requiredmemberattribute)

## Properties

### <a id="properties-expression"/>**Expression**

The expression (variable) passed to the function parameter.

```csharp
public string Expression { get; set; }
```

#### Property Value

[String](https://learn.microsoft.com/dotnet/api/system.string)<br>

### <a id="properties-index"/>**Index**

The index used in the message format string to substitute this type with.

```csharp
public int Index { get; set; }
```

#### Property Value

[Int32](https://learn.microsoft.com/dotnet/api/system.int32)<br>

### <a id="properties-listitems"/>**ListItems**

List item values as their string representation.

```csharp
public IReadOnlyDictionary<Int32, String> ListItems { get; set; }
```

#### Property Value

[IReadOnlyDictionary](https://learn.microsoft.com/dotnet/api/system.collections.generic.ireadonlydictionary-2)<[Int32](https://learn.microsoft.com/dotnet/api/system.int32), [String](https://learn.microsoft.com/dotnet/api/system.string)><br>

**Remarks:**

Only populated if [FunctionParameter.Type](./nefarius.utilities.etw.deserializer.wpp.tmf.functionparameter.md#type) is [ItemType.ItemListByte](./nefarius.utilities.etw.deserializer.wpp.tmf.itemtype.md#itemlistbyte).

### <a id="properties-type"/>**Type**

The data type of the function parameter.

```csharp
public ItemType Type { get; set; }
```

#### Property Value

[ItemType](./nefarius.utilities.etw.deserializer.wpp.tmf.itemtype.md)<br>
