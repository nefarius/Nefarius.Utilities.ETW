# ItemType

Namespace: Nefarius.Utilities.ETW.Deserializer.WPP.TMF

Possible trace message parameter types.

```csharp
public enum ItemType
```

Inheritance [Object](https://learn.microsoft.com/dotnet/api/system.object) → [ValueType](https://learn.microsoft.com/dotnet/api/system.valuetype) → [Enum](https://learn.microsoft.com/dotnet/api/system.enum) → [ItemType](./nefarius.utilities.etw.deserializer.wpp.tmf.itemtype.md)<br>
Implements [IComparable](https://learn.microsoft.com/dotnet/api/system.icomparable), [ISpanFormattable](https://learn.microsoft.com/dotnet/api/system.ispanformattable), [IFormattable](https://learn.microsoft.com/dotnet/api/system.iformattable), [IConvertible](https://learn.microsoft.com/dotnet/api/system.iconvertible)

**Remarks:**

Sourced from
 https://learn.microsoft.com/en-us/windows/win32/wes/eventmanifestschema-inputtype-complextype#remarkshttps://learn.microsoft.com/en-us/windows/win32/wes/eventmanifestschema-outputtype-complextype#remarkshttps://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/what-are-the-wpp-extended-format-specification-strings-#return-codesC:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\WppConfig\Rev1\defaultwpp.ini

## Fields

| Name | Value | Description |
| --- | --: | --- |
| ItemLongLongXX | 1 | A hexadecimal number that is preceded by "0x". The formatted value is not padded with leading zeros. |
| ItemLong | 6 | A 32-Bit (signed) integer. |
| ItemLongLong | 7 | A signed 64-bit integer. |
| ItemULongLong | 8 | An unsigned 64-bit integer. |
| ItemDouble | 9 | An IEEE 8-byte floating-point number. |
| ItemPtr | 10 | An unsigned 32-bit or 64-bit pointer value. The size depends on the architecture of the computer logging the event. |
| ItemGuid | 11 | A GUID value that is formatted in the registry string form, {xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}. |
| ItemListByte | 20 | A list of one or more zero-indexed array items, byte-sized index. |
| ItemListShort | 21 | A list of one or more zero-indexed array items, 16-bit (short) index. |
| ItemListLong | 22 | A list of one or more zero-indexed array items, 32-bit (long) index. |
| ItemSetByte | 23 | A bitset enum whose items are addressed by bit position (0 = bit 0), byte-sized value. |
| ItemSetShort | 24 | A bitset enum whose items are addressed by bit position, 16-bit value. |
| ItemSetLong | 25 | A bitset enum whose items are addressed by bit position, 32-bit value. |
| ItemEnum | 26 | A named enumeration resolved from PDB symbols (UInt32). Displays the raw numeric value when PDB-based name resolution is not available. |
| ItemFlagsEnum | 27 | A named flags enumeration resolved from PDB symbols (UInt32). Displays the raw hex value when PDB-based name resolution is not available. |
| ItemNTSTATUS | 28 | An NTSTATUS error code. This type is valid for the UInt32 input type. The service retrieves and renders the message string associated with the NT status code if it exists; otherwise, the service renders a string in the form, "Unknown NTSTATUS Error code: 0x" with the NT status code appended as hexadecimal number. Prior to MC version 1.12.7051 and Windows 7: Not available. |
| ItemWINERROR | 29 | Represents a Windows error code and displays the string associated with the error. |
| ItemHRESULT | 30 | Represents an HRESULT error or warning code. |
| ItemNDIS_STATUS | 31 | An NDIS status code. Formatted the same as [ItemType.ItemNTSTATUS](./nefarius.utilities.etw.deserializer.wpp.tmf.itemtype.md#itemntstatus). |
| ItemNDIS_OID | 32 | An NDIS OID value displayed as a hexadecimal number. |
| ItemIPAddr | 33 | An IPv4 address stored as a UInt32 in network byte order. Displayed in dotted-quad notation. |
| ItemPort | 34 | A TCP/UDP port number stored as a UInt16 in network byte order. |
| ItemTimestamp | 35 | A 64-bit FILETIME value representing an absolute timestamp. Displayed in ISO-8601 format. Used by `%!TIMESTAMP!`, `%!TIME!`, `%!DATE!`, `%!datetime!`, and `%!WAITTIME!`. |
| ItemTimeDelta | 36 | A 64-bit value representing a duration in milliseconds, displayed as `day~h:mm:ss`. Used by `%!delta!`. |
| ItemWaitTime | 37 | A 64-bit FILETIME value representing a wait/due time. Displayed as `day~h:mm:ss`. Used by `%!due!`. |
| ItemCLSID | 38 | A COM Class ID (CLSID) stored as a GUID. Displayed in registry string form. |
| ItemLIBID | 39 | A COM Type Library ID (LIBID) stored as a GUID. Displayed in registry string form. |
| ItemIID | 40 | A COM Interface ID (IID) stored as a GUID. Displayed in registry string form. |
| ItemChar4 | 41 | A four-character code (FOURCC) stored as a 32-bit integer. Each byte is rendered as a printable ASCII character (non-printable bytes are replaced with a dot). Used by `%!cccc!`. |
