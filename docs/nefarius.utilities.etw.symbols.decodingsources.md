# DecodingSources

Namespace: Nefarius.Utilities.ETW.Symbols

Loads PDB and TMF files, directories, and filename globs into WPP decoding sources.

```csharp
public static class DecodingSources
```

Inheritance [Object](https://learn.microsoft.com/dotnet/api/system.object) → [DecodingSources](./nefarius.utilities.etw.symbols.decodingsources.md)<br>
Attributes [NullableContextAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullablecontextattribute), [NullableAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullableattribute)

## Methods

### <a id="methods-load"/>**Load(IEnumerable&lt;String&gt;)**

Loads explicit symbol inputs. Unreadable or unsupported inputs are reported in
 [DecodingSourceSet.Diagnostics](./nefarius.utilities.etw.symbols.decodingsourceset.md#diagnostics) and skipped.

```csharp
public static DecodingSourceSet Load(IEnumerable<String> symbolPaths)
```

#### Parameters

`symbolPaths` [IEnumerable](https://learn.microsoft.com/dotnet/api/system.collections.generic.ienumerable-1)<[String](https://learn.microsoft.com/dotnet/api/system.string)><br>

#### Returns

[DecodingSourceSet](./nefarius.utilities.etw.symbols.decodingsourceset.md)

### <a id="methods-resolvetracesymbolsasync"/>**ResolveTraceSymbolsAsync(IEnumerable&lt;String&gt;, SymbolStoreConfiguration, HttpClient, CancellationToken)**

Pre-scans ETL files and resolves every referenced PDB through [SymbolStore](./nefarius.utilities.etw.symbols.symbolstore.md).

```csharp
public static Task<TraceSymbolResolution> ResolveTraceSymbolsAsync(IEnumerable<String> etlFiles, SymbolStoreConfiguration configuration, HttpClient httpClient, CancellationToken cancellationToken)
```

#### Parameters

`etlFiles` [IEnumerable](https://learn.microsoft.com/dotnet/api/system.collections.generic.ienumerable-1)<[String](https://learn.microsoft.com/dotnet/api/system.string)><br>

`configuration` [SymbolStoreConfiguration](./nefarius.utilities.etw.symbols.symbolstoreconfiguration.md)<br>

`httpClient` [HttpClient](https://learn.microsoft.com/dotnet/api/system.net.http.httpclient)<br>

`cancellationToken` [CancellationToken](https://learn.microsoft.com/dotnet/api/system.threading.cancellationtoken)<br>

#### Returns

[Task](https://learn.microsoft.com/dotnet/api/system.threading.tasks.task-1)<[TraceSymbolResolution](./nefarius.utilities.etw.symbols.tracesymbolresolution.md)>
