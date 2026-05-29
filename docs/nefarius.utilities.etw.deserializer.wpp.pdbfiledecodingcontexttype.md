# PdbFileDecodingContextType

Namespace: Nefarius.Utilities.ETW.Deserializer.WPP

A [DecodingContextType](./nefarius.utilities.etw.deserializer.wpp.decodingcontexttype.md) parsing one or more given `.pdb` files.

```csharp
public sealed class PdbFileDecodingContextType : DecodingContextType
```

Inheritance [Object](https://learn.microsoft.com/dotnet/api/system.object) → [DecodingContextType](./nefarius.utilities.etw.deserializer.wpp.decodingcontexttype.md) → [PdbFileDecodingContextType](./nefarius.utilities.etw.deserializer.wpp.pdbfiledecodingcontexttype.md)<br>
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

### <a id="properties-wpptracecontrols"/>**WppTraceControls**

The WPP trace control blocks discovered in this PDB — one entry per
 `WPP_DEFINE_CONTROL_GUID` declaration found in the symbol stream.
 Each entry contains the provider GUID, its friendly name, and the declared
 `WPP_DEFINE_BIT` flag names.

```csharp
public IReadOnlyCollection<WppTraceControl> WppTraceControls { get; private set; }
```

#### Property Value

[IReadOnlyCollection](https://learn.microsoft.com/dotnet/api/system.collections.generic.ireadonlycollection-1)<[WppTraceControl](./nefarius.utilities.etw.deserializer.wpp.tmf.wpptracecontrol.md)><br>

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

`path` [String](https://learn.microsoft.com/dotnet/api/system.string)<br>
Relative or absolute path to a `.pdb` file.

### <a id="constructors-.ctor"/>**PdbFileDecodingContextType(Stream)**

Gets decoding info from a stream containing PDB file data.

```csharp
public PdbFileDecodingContextType(Stream stream)
```

#### Parameters

`stream` [Stream](https://learn.microsoft.com/dotnet/api/system.io.stream)<br>
A stream containing a `.pdb` file.

## Methods

### <a id="methods-createfrom"/>**CreateFrom(IList&lt;String&gt;)**

Converts a list of paths to `*.pdb` files into their corresponding
 [PdbFileDecodingContextType](./nefarius.utilities.etw.deserializer.wpp.pdbfiledecodingcontexttype.md) objects.

```csharp
public static IList<DecodingContextType> CreateFrom(IList<String> pathList)
```

#### Parameters

`pathList` [IList](https://learn.microsoft.com/dotnet/api/system.collections.generic.ilist-1)<[String](https://learn.microsoft.com/dotnet/api/system.string)><br>
One or more paths to `.pdb` files.

#### Returns

One or more [PdbFileDecodingContextType](./nefarius.utilities.etw.deserializer.wpp.pdbfiledecodingcontexttype.md)s.

### <a id="methods-enumerateproviderguids"/>**EnumerateProviderGuids(String)**

Convenience helper that parses a single `.pdb` file and returns the distinct set of
 WPP trace control GUIDs (= ETW provider GUIDs) embedded in it.

```csharp
public static IReadOnlyCollection<Guid> EnumerateProviderGuids(string path)
```

#### Parameters

`path` [String](https://learn.microsoft.com/dotnet/api/system.string)<br>
Relative or absolute path to a `.pdb` file.

#### Returns

A distinct, read-only collection of [Guid](https://learn.microsoft.com/dotnet/api/system.guid)s — one per
 `WPP_DEFINE_CONTROL_GUID` block found in the PDB. The collection is empty if the
 PDB contains no WPP `TMC:` annotations.

### <a id="methods-enumeratetracecontrols"/>**EnumerateTraceControls(String)**

Convenience helper that parses a single `.pdb` file and returns the full
 [WppTraceControl](./nefarius.utilities.etw.deserializer.wpp.tmf.wpptracecontrol.md) records found in it — including the provider GUID,
 friendly name, and `WPP_DEFINE_BIT` flag names.

```csharp
public static IReadOnlyCollection<WppTraceControl> EnumerateTraceControls(string path)
```

#### Parameters

`path` [String](https://learn.microsoft.com/dotnet/api/system.string)<br>
Relative or absolute path to a `.pdb` file.

#### Returns

A read-only collection of [WppTraceControl](./nefarius.utilities.etw.deserializer.wpp.tmf.wpptracecontrol.md)s, deduplicated by
 [WppTraceControl.ControlGuid](./nefarius.utilities.etw.deserializer.wpp.tmf.wpptracecontrol.md#controlguid). Empty if the PDB has no WPP annotations.
