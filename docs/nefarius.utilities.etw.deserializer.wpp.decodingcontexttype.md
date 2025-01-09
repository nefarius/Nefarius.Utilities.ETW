# DecodingContextType

Namespace: Nefarius.Utilities.ETW.Deserializer.WPP

Describes a decoding source for use with [DecodingContext](./nefarius.utilities.etw.deserializer.wpp.decodingcontext.md).

```csharp
public abstract class DecodingContextType
```

Inheritance [Object](https://docs.microsoft.com/en-us/dotnet/api/system.object) → [DecodingContextType](./nefarius.utilities.etw.deserializer.wpp.decodingcontexttype.md)

## Methods

### <a id="methods-ascontext"/>**AsContext()**

Turns this instance into a [TDH_CONTEXT](./windows.win32.system.diagnostics.etw.tdh_context.md) for use with the TDH APIs.

```csharp
internal TDH_CONTEXT AsContext()
```

#### Returns

An instance of [TDH_CONTEXT](./windows.win32.system.diagnostics.etw.tdh_context.md).
