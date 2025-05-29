# ItemType

Namespace: Nefarius.Utilities.ETW.Deserializer.WPP.TMF

Possible trace message parameter types.

```csharp
public enum ItemType
```

Inheritance [Object](https://docs.microsoft.com/en-us/dotnet/api/system.object) → [ValueType](https://docs.microsoft.com/en-us/dotnet/api/system.valuetype) → [Enum](https://docs.microsoft.com/en-us/dotnet/api/system.enum) → [ItemType](./nefarius.utilities.etw.deserializer.wpp.tmf.itemtype.md)<br>
Implements [IComparable](https://docs.microsoft.com/en-us/dotnet/api/system.icomparable), [ISpanFormattable](https://docs.microsoft.com/en-us/dotnet/api/system.ispanformattable), [IFormattable](https://docs.microsoft.com/en-us/dotnet/api/system.iformattable), [IConvertible](https://docs.microsoft.com/en-us/dotnet/api/system.iconvertible)

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
| ItemListByte | 20 | A list of one or more zero-indexed array items. |
| ItemNTSTATUS | 21 | An NTSTATUS error code. This type is valid for the UInt32 input type. The service retrieves and renders the message string associated with the NT status code if it exists; otherwise, the service renders a string in the form, "Unknown NTSTATUS Error code: 0x" with the NT status code appended as hexadecimal number.Prior to MC version 1.12.7051 and Windows 7: Not available |
| ItemWINERROR | 22 | Represents a Windows error code and displays the string associated with the error. |
