# PdbFileDecodingContextType

Namespace: Nefarius.Utilities.ETW.Deserializer.WPP

A [DecodingContextType](./nefarius.utilities.etw.deserializer.wpp.decodingcontexttype.md) parsing one or more given `.pdb` files.

```csharp
public sealed class PdbFileDecodingContextType : DecodingContextType
```

Inheritance [Object](https://docs.microsoft.com/en-us/dotnet/api/system.object) → [DecodingContextType](./nefarius.utilities.etw.deserializer.wpp.decodingcontexttype.md) → [PdbFileDecodingContextType](./nefarius.utilities.etw.deserializer.wpp.pdbfiledecodingcontexttype.md)

## Properties

### <a id="properties-tracemessageformats"/>**TraceMessageFormats**

Collection of extracted [TraceMessageFormat](./nefarius.utilities.etw.deserializer.wpp.tmf.tracemessageformat.md)s of this [DecodingContextType](./nefarius.utilities.etw.deserializer.wpp.decodingcontexttype.md).

```csharp
public IEnumerable<TraceMessageFormat> TraceMessageFormats { get; protected set; }
```

#### Property Value

[IEnumerable&lt;TraceMessageFormat&gt;](https://docs.microsoft.com/en-us/dotnet/api/system.collections.generic.ienumerable-1)<br>

## Constructors

### <a id="constructors-.ctor"/>**PdbFileDecodingContextType()**

A [DecodingContextType](./nefarius.utilities.etw.deserializer.wpp.decodingcontexttype.md) parsing one or more given `.pdb` files.

```csharp
public PdbFileDecodingContextType()
```

### <a id="constructors-.ctor"/>**PdbFileDecodingContextType(String)**

Gets decoding info from one or multiple `.pdb` files by file path.

```csharp
public PdbFileDecodingContextType(string path)
```

#### Parameters

`path` [String](https://docs.microsoft.com/en-us/dotnet/api/system.string)<br>
Relative or absolute path to a `.pdb` file.

### <a id="constructors-.ctor"/>**PdbFileDecodingContextType(Stream)**

Gets decoding info from a stream containing PDB file data.

```csharp
public PdbFileDecodingContextType(Stream stream)
```

#### Parameters

`stream` [Stream](https://docs.microsoft.com/en-us/dotnet/api/system.io.stream)<br>
A stream containing a `.pdb` file.

## Methods

### <a id="methods-createfrom"/>**CreateFrom(IList&lt;String&gt;)**

Converts a list of paths to `*.pdb` files into their corresponding [PdbFileDecodingContextType](./nefarius.utilities.etw.deserializer.wpp.pdbfiledecodingcontexttype.md)
 objects.

```csharp
public static IList<DecodingContextType> CreateFrom(IList<String> pathList)
```

#### Parameters

`pathList` [IList&lt;String&gt;](https://docs.microsoft.com/en-us/dotnet/api/system.collections.generic.ilist-1)<br>
One or more paths to `.pdb` files.

#### Returns

One or more [PdbFileDecodingContextType](./nefarius.utilities.etw.deserializer.wpp.pdbfiledecodingcontexttype.md)s.
