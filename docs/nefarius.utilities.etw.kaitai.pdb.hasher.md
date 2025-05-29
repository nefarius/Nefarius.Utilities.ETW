# Hasher

Namespace: Nefarius.Utilities.ETW.Kaitai.PDB

```csharp
public class Hasher
```

Inheritance [Object](https://docs.microsoft.com/en-us/dotnet/api/system.object) → [Hasher](./nefarius.utilities.etw.kaitai.pdb.hasher.md)

## Constructors

### <a id="constructors-.ctor"/>**Hasher()**

```csharp
public Hasher()
```

## Methods

### <a id="methods-hashulong"/>**HashUlong(UInt32)**

```csharp
public static uint HashUlong(uint value)
```

#### Parameters

`value` [UInt32](https://docs.microsoft.com/en-us/dotnet/api/system.uint32)<br>

#### Returns

[UInt32](https://docs.microsoft.com/en-us/dotnet/api/system.uint32)

### <a id="methods-hashv1"/>**HashV1(Memory&lt;Byte&gt;, UInt32)**

```csharp
public static uint HashV1(Memory<Byte> buf, uint modulo)
```

#### Parameters

`buf` [Memory&lt;Byte&gt;](https://docs.microsoft.com/en-us/dotnet/api/system.memory-1)<br>

`modulo` [UInt32](https://docs.microsoft.com/en-us/dotnet/api/system.uint32)<br>

#### Returns

[UInt32](https://docs.microsoft.com/en-us/dotnet/api/system.uint32)

### <a id="methods-hashv2"/>**HashV2(Memory&lt;Byte&gt;, UInt32)**

```csharp
public static uint HashV2(Memory<Byte> buf, uint modulo)
```

#### Parameters

`buf` [Memory&lt;Byte&gt;](https://docs.microsoft.com/en-us/dotnet/api/system.memory-1)<br>

`modulo` [UInt32](https://docs.microsoft.com/en-us/dotnet/api/system.uint32)<br>

#### Returns

[UInt32](https://docs.microsoft.com/en-us/dotnet/api/system.uint32)
