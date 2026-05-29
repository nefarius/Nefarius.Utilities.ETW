# DecodingContext

Namespace: Nefarius.Utilities.ETW.Deserializer.WPP

WPP decoding context used to extract TMF information from resources like `.PDB` or `.TMF` files.

```csharp
public sealed class DecodingContext
```

Inheritance [Object](https://learn.microsoft.com/dotnet/api/system.object) → [DecodingContext](./nefarius.utilities.etw.deserializer.wpp.decodingcontext.md)<br>
Attributes [NullableContextAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullablecontextattribute), [NullableAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullableattribute)

## Properties

### <a id="properties-providerguids"/>**ProviderGuids**

The deduplicated union of all WPP trace control GUIDs (= ETW provider GUIDs) across every
 underlying [DecodingContextType](./nefarius.utilities.etw.deserializer.wpp.decodingcontexttype.md). Each GUID corresponds to one
 `WPP_DEFINE_CONTROL_GUID` declaration found in the loaded PDB files.
 The collection is empty when the context was built from TMF files only.

```csharp
public IReadOnlyCollection<Guid> ProviderGuids { get; }
```

#### Property Value

[IReadOnlyCollection](https://learn.microsoft.com/dotnet/api/system.collections.generic.ireadonlycollection-1)<[Guid](https://learn.microsoft.com/dotnet/api/system.guid)><br>

## Constructors

### <a id="constructors-.ctor"/>**DecodingContext(IList&lt;DecodingContextType&gt;)**

New decoding context instance.

```csharp
public DecodingContext(IList<DecodingContextType> decodingTypes)
```

#### Parameters

`decodingTypes` [IList](https://learn.microsoft.com/dotnet/api/system.collections.generic.ilist-1)<[DecodingContextType](./nefarius.utilities.etw.deserializer.wpp.decodingcontexttype.md)><br>
One or more [DecodingContextType](./nefarius.utilities.etw.deserializer.wpp.decodingcontexttype.md)s to look up decoding information in.

## Methods

### <a id="methods-extendwith"/>**ExtendWith(IList&lt;DecodingContextType&gt;)**

Creates a new [DecodingContext](./nefarius.utilities.etw.deserializer.wpp.decodingcontext.md) instance with additionally provided [DecodingContextType](./nefarius.utilities.etw.deserializer.wpp.decodingcontexttype.md)
 s.

```csharp
public DecodingContext ExtendWith(IList<DecodingContextType> additionalDecodingTypes)
```

#### Parameters

`additionalDecodingTypes` [IList](https://learn.microsoft.com/dotnet/api/system.collections.generic.ilist-1)<[DecodingContextType](./nefarius.utilities.etw.deserializer.wpp.decodingcontexttype.md)><br>
One or more [DecodingContextType](./nefarius.utilities.etw.deserializer.wpp.decodingcontexttype.md)s to be added to this instances'
 types.

#### Returns

The extended [DecodingContext](./nefarius.utilities.etw.deserializer.wpp.decodingcontext.md) instance.

### <a id="methods-gettracemessageformatfor"/>**GetTraceMessageFormatFor(Nullable&lt;Guid&gt;, Int32)**

```csharp
internal TraceMessageFormat GetTraceMessageFormatFor(Nullable<Guid> messageGuid, int id)
```

#### Parameters

`messageGuid` [Nullable](https://learn.microsoft.com/dotnet/api/system.nullable-1)<[Guid](https://learn.microsoft.com/dotnet/api/system.guid)><br>

`id` [Int32](https://learn.microsoft.com/dotnet/api/system.int32)<br>

#### Returns

[TraceMessageFormat](./nefarius.utilities.etw.deserializer.wpp.tmf.tracemessageformat.md)
