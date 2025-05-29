# DecodingContext

Namespace: Nefarius.Utilities.ETW.Deserializer.WPP

WPP decoding context used to extract TMF information from resources like `.PDB` or `.TMF` files.

```csharp
public sealed class DecodingContext
```

Inheritance [Object](https://docs.microsoft.com/en-us/dotnet/api/system.object) → [DecodingContext](./nefarius.utilities.etw.deserializer.wpp.decodingcontext.md)

## Constructors

### <a id="constructors-.ctor"/>**DecodingContext(IList&lt;DecodingContextType&gt;)**

New decoding context instance.

```csharp
public DecodingContext(IList<DecodingContextType> decodingTypes)
```

#### Parameters

`decodingTypes` [IList&lt;DecodingContextType&gt;](https://docs.microsoft.com/en-us/dotnet/api/system.collections.generic.ilist-1)<br>
One or more [DecodingContextType](./nefarius.utilities.etw.deserializer.wpp.decodingcontexttype.md)s to look up decoding information in.

## Methods

### <a id="methods-extendwith"/>**ExtendWith(IList&lt;DecodingContextType&gt;)**

Creates a new [DecodingContext](./nefarius.utilities.etw.deserializer.wpp.decodingcontext.md) instance with additionally provided [DecodingContextType](./nefarius.utilities.etw.deserializer.wpp.decodingcontexttype.md)
 s.

```csharp
public DecodingContext ExtendWith(IList<DecodingContextType> additionalDecodingTypes)
```

#### Parameters

`additionalDecodingTypes` [IList&lt;DecodingContextType&gt;](https://docs.microsoft.com/en-us/dotnet/api/system.collections.generic.ilist-1)<br>
One or more [DecodingContextType](./nefarius.utilities.etw.deserializer.wpp.decodingcontexttype.md)s to be added to this instances'
 types.

#### Returns

The extended [DecodingContext](./nefarius.utilities.etw.deserializer.wpp.decodingcontext.md) instance.

### <a id="methods-gettracemessageformatfor"/>**GetTraceMessageFormatFor(Nullable&lt;Guid&gt;, Int32)**

```csharp
internal TraceMessageFormat GetTraceMessageFormatFor(Nullable<Guid> messageGuid, int id)
```

#### Parameters

`messageGuid` [Nullable&lt;Guid&gt;](https://docs.microsoft.com/en-us/dotnet/api/system.nullable-1)<br>

`id` [Int32](https://docs.microsoft.com/en-us/dotnet/api/system.int32)<br>

#### Returns

[TraceMessageFormat](./nefarius.utilities.etw.deserializer.wpp.tmf.tracemessageformat.md)
