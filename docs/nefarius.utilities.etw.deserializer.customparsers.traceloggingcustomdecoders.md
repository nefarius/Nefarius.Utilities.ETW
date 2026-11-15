# TraceLoggingCustomDecoders

Namespace: Nefarius.Utilities.ETW.Deserializer.CustomParsers

Optional registry of decoders for TraceLogging custom-serialized fields, keyed by their exact type-info bytes.
 Without a registered decoder the field is emitted using its base in-type (raw bytes for binary fields); the
 type info is opaque to this library.

```csharp
public static class TraceLoggingCustomDecoders
```

Inheritance [Object](https://learn.microsoft.com/dotnet/api/system.object) → [TraceLoggingCustomDecoders](./nefarius.utilities.etw.deserializer.customparsers.traceloggingcustomdecoders.md)

## Methods

### <a id="methods-register"/>**Register(ReadOnlySpan&lt;Byte&gt;, TraceLoggingCustomDecoder)**

Registers (or replaces) the decoder for fields carrying exactly `typeInfo`.

```csharp
public static void Register(ReadOnlySpan<Byte> typeInfo, TraceLoggingCustomDecoder decoder)
```

#### Parameters

`typeInfo` [ReadOnlySpan](https://learn.microsoft.com/dotnet/api/system.readonlyspan-1)<[Byte](https://learn.microsoft.com/dotnet/api/system.byte)><br>

`decoder` [TraceLoggingCustomDecoder](./nefarius.utilities.etw.deserializer.customparsers.traceloggingcustomdecoder.md)<br>

### <a id="methods-tryget"/>**TryGet(Byte[], ref TraceLoggingCustomDecoder)**

```csharp
internal static bool TryGet(Byte[] typeInfo, ref TraceLoggingCustomDecoder decoder)
```

#### Parameters

`typeInfo` [Byte[]](https://learn.microsoft.com/dotnet/api/system.byte[])<br>

`decoder` [TraceLoggingCustomDecoder&](./nefarius.utilities.etw.deserializer.customparsers.traceloggingcustomdecoder&.md)<br>

#### Returns

[Boolean](https://learn.microsoft.com/dotnet/api/system.boolean)

### <a id="methods-unregister"/>**Unregister(ReadOnlySpan&lt;Byte&gt;)**

Removes the decoder registered for `typeInfo`.

```csharp
public static bool Unregister(ReadOnlySpan<Byte> typeInfo)
```

#### Parameters

`typeInfo` [ReadOnlySpan](https://learn.microsoft.com/dotnet/api/system.readonlyspan-1)<[Byte](https://learn.microsoft.com/dotnet/api/system.byte)><br>

#### Returns

[Boolean](https://learn.microsoft.com/dotnet/api/system.boolean)
