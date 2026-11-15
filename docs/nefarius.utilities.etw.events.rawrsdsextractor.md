# RawRsdsExtractor

Namespace: Nefarius.Utilities.ETW.Events

Extracts PDB identities from relayed [RawEtwRecord](./nefarius.utilities.etw.relay.rawetwrecord.md) payloads without TDH or native pointers.

```csharp
public static class RawRsdsExtractor
```

Inheritance [Object](https://learn.microsoft.com/dotnet/api/system.object) → [RawRsdsExtractor](./nefarius.utilities.etw.events.rawrsdsextractor.md)

**Remarks:**

Supports `KernelTraceControl/ImageID/DbgID_RSDS` (opcode 36) and the legacy
 `MSNT_SystemTrace/EventTrace/DbgIdRSDS` (opcode 64). Malformed records are reported as
 `false` and never throw. The relayed data is untrusted: only the leaf file name is kept
 and it must be a plain printable-ASCII name.

## Methods

### <a id="methods-tryextract"/>**TryExtract(RawEtwRecord, ref PdbMetaData)**

Attempts to extract a PDB identity from `record`.

```csharp
public static bool TryExtract(RawEtwRecord record, ref PdbMetaData pdb)
```

#### Parameters

`record` [RawEtwRecord](./nefarius.utilities.etw.relay.rawetwrecord.md)<br>

`pdb` [PdbMetaData&](./nefarius.utilities.etw.deserializer.wpp.pdbmetadata&.md)<br>

#### Returns

`true` when the record is a well-formed RSDS event.
