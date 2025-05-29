# TmfParser

Namespace: Nefarius.Utilities.ETW.Deserializer.WPP.TMF

Trace Message Format parsing utilities.

```csharp
public static class TmfParser
```

Inheritance [Object](https://docs.microsoft.com/en-us/dotnet/api/system.object) → [TmfParser](./nefarius.utilities.etw.deserializer.wpp.tmf.tmfparser.md)

## Methods

### <a id="methods-extracttracemessageformats"/>**ExtractTraceMessageFormats(IEnumerable&lt;SymProc32AnnotationPair&gt;, String)**

Converts a collection of [SymProc32AnnotationPair](./nefarius.utilities.etw.deserializer.wpp.tmf.symproc32annotationpair.md) into [TraceMessageFormat](./nefarius.utilities.etw.deserializer.wpp.tmf.tracemessageformat.md)s.

```csharp
internal static IEnumerable<TraceMessageFormat> ExtractTraceMessageFormats(IEnumerable<SymProc32AnnotationPair> pairs, string originalSymbolFileName)
```

#### Parameters

`pairs` [IEnumerable&lt;SymProc32AnnotationPair&gt;](https://docs.microsoft.com/en-us/dotnet/api/system.collections.generic.ienumerable-1)<br>
The source [SymProc32AnnotationPair](./nefarius.utilities.etw.deserializer.wpp.tmf.symproc32annotationpair.md)s to convert.

`originalSymbolFileName` [String](https://docs.microsoft.com/en-us/dotnet/api/system.string)<br>
Optional source file name the data was extracted from.

#### Returns

A collection of extracted [TraceMessageFormat](./nefarius.utilities.etw.deserializer.wpp.tmf.tracemessageformat.md)s.

### <a id="methods-parsedirectory"/>**ParseDirectory(String)**

Processes a given directory of `.TMF` files and parses them.

```csharp
public static IEnumerable<TraceMessageFormat> ParseDirectory(string path)
```

#### Parameters

`path` [String](https://docs.microsoft.com/en-us/dotnet/api/system.string)<br>
The directory to search in.

#### Returns

A collection of extracted [TraceMessageFormat](./nefarius.utilities.etw.deserializer.wpp.tmf.tracemessageformat.md) entries.
