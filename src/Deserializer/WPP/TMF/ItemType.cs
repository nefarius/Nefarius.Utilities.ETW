using System.Diagnostics.CodeAnalysis;

namespace Nefarius.Utilities.ETW.Deserializer.WPP.TMF;

/// <summary>
///     Possible trace message parameter types.
/// </summary>
/// <remarks>
///     Sourced from
///     <li>
///         <ul>https://learn.microsoft.com/en-us/windows/win32/wes/eventmanifestschema-inputtype-complextype#remarks</ul>
///         <ul>https://learn.microsoft.com/en-us/windows/win32/wes/eventmanifestschema-outputtype-complextype#remarks</ul>
///         <ul>https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/what-are-the-wpp-extended-format-specification-strings-#return-codes</ul>
///         <ul>C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\WppConfig\Rev1\defaultwpp.ini</ul>
///     </li>
/// </remarks>
[SuppressMessage("ReSharper", "InconsistentNaming")]
[SuppressMessage("ReSharper", "UnusedMember.Global")]
public enum ItemType
{
    #region basic arithmetic types

    ItemLongLongX,

    /// <summary>
    ///     A hexadecimal number that is preceded by "0x". The formatted value is not padded with leading zeros.
    /// </summary>
    ItemLongLongXX,

    ItemLongLongO,

    ItemChar,
    ItemUChar,
    ItemShort,

    /// <summary>
    ///     A 32-Bit (signed) integer.
    /// </summary>
    ItemLong,

    /// <summary>
    ///     A signed 64-bit integer.
    /// </summary>
    ItemLongLong,

    /// <summary>
    ///     An unsigned 64-bit integer.
    /// </summary>
    ItemULongLong,

    /// <summary>
    ///     An IEEE 8-byte floating-point number.
    /// </summary>
    ItemDouble,

    #endregion

    #region arch dependent types

    /// <summary>
    ///     An unsigned 32-bit or 64-bit pointer value. The size depends on the architecture of the computer logging the event.
    /// </summary>
    ItemPtr,

    /// <summary>
    ///     A GUID value that is formatted in the registry string form, {xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}.
    /// </summary>
    ItemGuid,

    #endregion

    #region Complex types
    
    ItemString,
    ItemRString,
    ItemRWString,
    ItemWString,
    ItemPString,
    ItemPWString,

    ItemSid,
    
    ItemHexDump,

    #endregion

    #region enumeration types

    /// <summary>
    ///     A list of one or more zero-indexed array items, byte-sized index.
    /// </summary>
    ItemListByte,

    /// <summary>
    ///     A list of one or more zero-indexed array items, 16-bit (short) index.
    /// </summary>
    ItemListShort,

    /// <summary>
    ///     A list of one or more zero-indexed array items, 32-bit (long) index.
    /// </summary>
    ItemListLong,

    /// <summary>
    ///     A bitset enum whose items are addressed by bit position (0 = bit 0), byte-sized value.
    /// </summary>
    ItemSetByte,

    /// <summary>
    ///     A bitset enum whose items are addressed by bit position, 16-bit value.
    /// </summary>
    ItemSetShort,

    /// <summary>
    ///     A bitset enum whose items are addressed by bit position, 32-bit value.
    /// </summary>
    ItemSetLong,

    /// <summary>
    ///     A named enumeration resolved from PDB symbols (UInt32). Displays the raw numeric value when
    ///     PDB-based name resolution is not available.
    /// </summary>
    ItemEnum,

    /// <summary>
    ///     A named flags enumeration resolved from PDB symbols (UInt32). Displays the raw hex value when
    ///     PDB-based name resolution is not available.
    /// </summary>
    ItemFlagsEnum,

    #endregion

    #region special formats

    /// <summary>
    ///     An NTSTATUS error code. This type is valid for the UInt32 input type. The service retrieves and renders the message
    ///     string associated with the NT status code if it exists; otherwise, the service renders a string in the form,
    ///     "Unknown NTSTATUS Error code: 0x" with the NT status code appended as hexadecimal number. Prior to MC version
    ///     1.12.7051 and Windows 7: Not available.
    /// </summary>
    ItemNTSTATUS,

    /// <summary>
    ///     Represents a Windows error code and displays the string associated with the error.
    /// </summary>
    ItemWINERROR,

    /// <summary>
    ///     Represents an HRESULT error or warning code.
    /// </summary>
    ItemHRESULT,

    /// <summary>
    ///     An NDIS status code. Formatted the same as <see cref="ItemNTSTATUS" />.
    /// </summary>
    ItemNDIS_STATUS,

    /// <summary>
    ///     An NDIS OID value displayed as a hexadecimal number.
    /// </summary>
    ItemNDIS_OID,

    /// <summary>
    ///     An IPv4 address stored as a UInt32 in network byte order. Displayed in dotted-quad notation.
    /// </summary>
    ItemIPAddr,

    /// <summary>
    ///     A TCP/UDP port number stored as a UInt16 in network byte order.
    /// </summary>
    ItemPort,

    /// <summary>
    ///     A 64-bit FILETIME value representing an absolute timestamp. Displayed in ISO-8601 format.
    ///     Used by <c>%!TIMESTAMP!</c>, <c>%!TIME!</c>, <c>%!DATE!</c>, <c>%!datetime!</c>, and <c>%!WAITTIME!</c>.
    /// </summary>
    ItemTimestamp,

    /// <summary>
    ///     A 64-bit value representing a duration in milliseconds, displayed as <c>day~h:mm:ss</c>.
    ///     Used by <c>%!delta!</c>.
    /// </summary>
    ItemTimeDelta,

    /// <summary>
    ///     A 64-bit FILETIME value representing a wait/due time. Displayed as <c>day~h:mm:ss</c>.
    ///     Used by <c>%!due!</c>.
    /// </summary>
    ItemWaitTime,

    /// <summary>
    ///     A COM Class ID (CLSID) stored as a GUID. Displayed in registry string form.
    /// </summary>
    ItemCLSID,

    /// <summary>
    ///     A COM Type Library ID (LIBID) stored as a GUID. Displayed in registry string form.
    /// </summary>
    ItemLIBID,

    /// <summary>
    ///     A COM Interface ID (IID) stored as a GUID. Displayed in registry string form.
    /// </summary>
    ItemIID,

    /// <summary>
    ///     A four-character code (FOURCC) stored as a 32-bit integer. Each byte is rendered as a
    ///     printable ASCII character (non-printable bytes are replaced with a dot).
    ///     Used by <c>%!cccc!</c>.
    /// </summary>
    ItemChar4,

    #endregion
}