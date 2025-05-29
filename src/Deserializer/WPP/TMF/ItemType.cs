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
    ///     A list of one or more zero-indexed array items.
    /// </summary>
    ItemListByte,

    #endregion

    #region special formats

    /// <summary>
    ///     An NTSTATUS error code. This type is valid for the UInt32 input type. The service retrieves and renders the message
    ///     string associated with the NT status code if it exists; otherwise, the service renders a string in the form,
    ///     "Unknown NTSTATUS Error code: 0x" with the NT status code appended as hexadecimal number.Prior to MC version
    ///     1.12.7051 and Windows 7: Not available
    /// </summary>
    ItemNTSTATUS,

    /// <summary>
    ///     Represents a Windows error code and displays the string associated with the error.
    /// </summary>
    ItemWINERROR,
    
    ItemHRESULT

    #endregion
}