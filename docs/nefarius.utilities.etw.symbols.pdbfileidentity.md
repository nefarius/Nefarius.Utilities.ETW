# PdbFileIdentity

Namespace: Nefarius.Utilities.ETW.Symbols

PDB signature and age read from a Windows PDB information stream.

```csharp
public record struct PdbFileIdentity
```

Inheritance [Object](https://learn.microsoft.com/dotnet/api/system.object) → [ValueType](https://learn.microsoft.com/dotnet/api/system.valuetype) → [PdbFileIdentity](./nefarius.utilities.etw.symbols.pdbfileidentity.md)<br>
Attributes [IsReadOnlyAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.isreadonlyattribute)

## Properties

### <a id="properties-age"/>**Age**

The PDB age.

```csharp
public int Age { get; set; }
```

#### Property Value

[Int32](https://learn.microsoft.com/dotnet/api/system.int32)<br>

### <a id="properties-guid"/>**Guid**

The VC7 PDB signature.

```csharp
public Guid Guid { get; set; }
```

#### Property Value

[Guid](https://learn.microsoft.com/dotnet/api/system.guid)<br>

## Constructors

### <a id="constructors-.ctor"/>**PdbFileIdentity(Guid, Int32)**

PDB signature and age read from a Windows PDB information stream.

```csharp
PdbFileIdentity(Guid Guid, int Age)
```

#### Parameters

`Guid` [Guid](https://learn.microsoft.com/dotnet/api/system.guid)<br>
The VC7 PDB signature.

`Age` [Int32](https://learn.microsoft.com/dotnet/api/system.int32)<br>
The PDB age.

## Methods

### <a id="methods-equals"/>**Equals(PdbFileIdentity)**

```csharp
bool Equals(PdbFileIdentity other)
```

#### Parameters

`other` [PdbFileIdentity](./nefarius.utilities.etw.symbols.pdbfileidentity.md)<br>

#### Returns

[Boolean](https://learn.microsoft.com/dotnet/api/system.boolean)
