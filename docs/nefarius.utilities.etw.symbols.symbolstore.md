# SymbolStore

Namespace: Nefarius.Utilities.ETW.Symbols

Resolves PDB identities through a local cache, search paths, and an HTTP symbol server.

```csharp
public static class SymbolStore
```

Inheritance [Object](https://learn.microsoft.com/dotnet/api/system.object) → [SymbolStore](./nefarius.utilities.etw.symbols.symbolstore.md)<br>
Attributes [NullableContextAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullablecontextattribute), [NullableAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullableattribute)

## Methods

### <a id="methods-resolveasync"/>**ResolveAsync(IEnumerable&lt;PdbMetaData&gt;, SymbolStoreConfiguration, HttpClient, CancellationToken)**

Resolves each PDB reference in cache, local-search, symbol-server order.

```csharp
public static Task<IReadOnlyList<ResolvedPdb>> ResolveAsync(IEnumerable<PdbMetaData> references, SymbolStoreConfiguration configuration, HttpClient httpClient, CancellationToken cancellationToken)
```

#### Parameters

`references` [IEnumerable](https://learn.microsoft.com/dotnet/api/system.collections.generic.ienumerable-1)<[PdbMetaData](./nefarius.utilities.etw.deserializer.wpp.pdbmetadata.md)><br>

`configuration` [SymbolStoreConfiguration](./nefarius.utilities.etw.symbols.symbolstoreconfiguration.md)<br>

`httpClient` [HttpClient](https://learn.microsoft.com/dotnet/api/system.net.http.httpclient)<br>

`cancellationToken` [CancellationToken](https://learn.microsoft.com/dotnet/api/system.threading.cancellationtoken)<br>

#### Returns

[Task](https://learn.microsoft.com/dotnet/api/system.threading.tasks.task-1)<[IReadOnlyList](https://learn.microsoft.com/dotnet/api/system.collections.generic.ireadonlylist-1)<[ResolvedPdb](./nefarius.utilities.etw.symbols.resolvedpdb.md)>>
