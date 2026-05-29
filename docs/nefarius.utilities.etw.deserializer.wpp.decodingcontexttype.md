# DecodingContextType

Namespace: Nefarius.Utilities.ETW.Deserializer.WPP

Describes a decoding source for use with one or more [DecodingContext](./nefarius.utilities.etw.deserializer.wpp.decodingcontext.md)s.

```csharp
public abstract class DecodingContextType
```

Inheritance [Object](https://learn.microsoft.com/dotnet/api/system.object) → [DecodingContextType](./nefarius.utilities.etw.deserializer.wpp.decodingcontexttype.md)<br>
Attributes [NullableContextAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullablecontextattribute), [NullableAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullableattribute)

## Properties

### <a id="properties-providerguids"/>**ProviderGuids**

The distinct set of WPP trace control GUIDs (= ETW provider GUIDs) available from this
 decoding source. Subclasses that carry the provider GUID — currently only
 [PdbFileDecodingContextType](./nefarius.utilities.etw.deserializer.wpp.pdbfiledecodingcontexttype.md) via its `TMC:` annotations — override this
 property. TMF-only sources return an empty collection because `.tmf` files only
 contain per-call-site message hash GUIDs, not the WPP control GUID.

```csharp
public IEnumerable<Guid> ProviderGuids { get; }
```

#### Property Value

[IEnumerable](https://learn.microsoft.com/dotnet/api/system.collections.generic.ienumerable-1)<[Guid](https://learn.microsoft.com/dotnet/api/system.guid)><br>

### <a id="properties-tracemessageformats"/>**TraceMessageFormats**

Collection of extracted [TraceMessageFormat](./nefarius.utilities.etw.deserializer.wpp.tmf.tracemessageformat.md)s of this [DecodingContextType](./nefarius.utilities.etw.deserializer.wpp.decodingcontexttype.md).

```csharp
public IEnumerable<TraceMessageFormat> TraceMessageFormats { get; protected set; }
```

#### Property Value

[IEnumerable](https://learn.microsoft.com/dotnet/api/system.collections.generic.ienumerable-1)<[TraceMessageFormat](./nefarius.utilities.etw.deserializer.wpp.tmf.tracemessageformat.md)><br>
