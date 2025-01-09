# DecodingContext

Namespace: Nefarius.Utilities.ETW.Deserializer.WPP

WPP decoding context used to extract TMF information from resources like `.PDB` or `.TMF` files.

```csharp
public sealed class DecodingContext : System.IDisposable
```

Inheritance [Object](https://docs.microsoft.com/en-us/dotnet/api/system.object) → [DecodingContext](./nefarius.utilities.etw.deserializer.wpp.decodingcontext.md)<br>
Implements [IDisposable](https://docs.microsoft.com/en-us/dotnet/api/system.idisposable)

## Constructors

### <a id="constructors-.ctor"/>**DecodingContext(IList&lt;DecodingContextType&gt;)**

New decoding context instance.

```csharp
public DecodingContext(IList<DecodingContextType> decodingTypes)
```

#### Parameters

`decodingTypes` [IList&lt;DecodingContextType&gt;](https://docs.microsoft.com/en-us/dotnet/api/system.collections.generic.ilist-1)<br>
One or more [DecodingContextType](./nefarius.utilities.etw.deserializer.wpp.decodingcontexttype.md)s to look up decoding information in.

#### Exceptions

[Win32Exception](https://docs.microsoft.com/en-us/dotnet/api/system.componentmodel.win32exception)<br>
One or more TDH API calls failed.

## Methods

### <a id="methods-dispose"/>**Dispose()**

```csharp
public void Dispose()
```

### <a id="methods-finalize"/>**Finalize()**

```csharp
protected void Finalize()
```
