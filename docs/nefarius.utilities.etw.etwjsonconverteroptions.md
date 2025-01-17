# EtwJsonConverterOptions

Namespace: Nefarius.Utilities.ETW

Adjustments for [EtwUtil](./nefarius.utilities.etw.etwutil.md).

```csharp
public sealed class EtwJsonConverterOptions
```

Inheritance [Object](https://docs.microsoft.com/en-us/dotnet/api/system.object) → [EtwJsonConverterOptions](./nefarius.utilities.etw.etwjsonconverteroptions.md)

## Properties

### <a id="properties-contextproviderlookup"/>**ContextProviderLookup**

Custom [DecodingContext](./nefarius.utilities.etw.deserializer.wpp.decodingcontext.md) provider lookup.

```csharp
public Func<PdbMetaData, DecodingContext> ContextProviderLookup { get; set; }
```

#### Property Value

[Func&lt;PdbMetaData, DecodingContext&gt;](https://docs.microsoft.com/en-us/dotnet/api/system.func-2)<br>

### <a id="properties-customprovidermanifest"/>**CustomProviderManifest**

Custom manifest provider lookup.

```csharp
public Func<Guid, Stream> CustomProviderManifest { get; set; }
```

#### Property Value

[Func&lt;Guid, Stream&gt;](https://docs.microsoft.com/en-us/dotnet/api/system.func-2)<br>

### <a id="properties-reporterror"/>**ReportError**

Reports potential parsing errors.

```csharp
public Action<String> ReportError { get; set; }
```

#### Property Value

[Action&lt;String&gt;](https://docs.microsoft.com/en-us/dotnet/api/system.action-1)<br>

### <a id="properties-wppdecodingcontext"/>**WppDecodingContext**

[EtwJsonConverterOptions.WppDecodingContext](./nefarius.utilities.etw.etwjsonconverteroptions.md#wppdecodingcontext) to read WPP events.

```csharp
public DecodingContext WppDecodingContext { get; set; }
```

#### Property Value

[DecodingContext](./nefarius.utilities.etw.deserializer.wpp.decodingcontext.md)<br>
