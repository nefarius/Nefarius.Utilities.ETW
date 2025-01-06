using System.Diagnostics.CodeAnalysis;

namespace Nefarius.Utilities.ETW.Deserializer.WPP.TMF;

/// <summary>
///     Possible trace message parameter types.
/// </summary>
/// <remarks>
///     Partial source:
///     https://learn.microsoft.com/en-us/windows/win32/wes/eventmanifestschema-inputtype-complextype#remarks
/// </remarks>
[SuppressMessage("ReSharper", "InconsistentNaming")]
internal enum ItemType
{
    /// <summary>
    ///     A list of one or more zero-indexed array items.
    /// </summary>
    ItemListByte,

    /// <summary>
    ///     A 32-Bit (signed) integer.
    /// </summary>
    ItemLong,

    /// <summary>
    ///     A 64-Bit (signed) integer.
    /// </summary>
    ItemLongLong,

    /// <summary>
    ///     A hexadecimal number that is preceded by "0x". The formatted value is not padded with leading zeros.
    /// </summary>
    ItemLongLongXX,

    /// <summary>
    ///     An NTSTATUS error code. This type is valid for the UInt32 input type. The service retrieves and renders the message
    ///     string associated with the NT status code if it exists; otherwise, the service renders a string in the form,
    ///     "Unknown NTSTATUS Error code: 0x" with the NT status code appended as hexadecimal number.Prior to MC version
    ///     1.12.7051 and Windows 7: Not available
    /// </summary>
    ItemNTSTATUS,

    /// <summary>
    ///     A string of 16-bit characters. By default, assumed to have been encoded using UTF-16LE.
    /// </summary>
    ItemPWString,

    /// <summary>
    ///     An unsigned 32-bit or 64-bit pointer value. The size depends on the architecture of the computer logging the event.
    /// </summary>
    ItemPtr,

    /// <summary>
    ///     A string of 8-bit characters. By default or when used with the xs:string output type, the string is assumed to have
    ///     been encoded using the event provider s ANSI code page. When used with the win:Xml, win:Json, or win:Utf8 output
    ///     types, the string is assumed to have been encoded using UTF-8.
    /// </summary>
    ItemString
}

internal struct FunctionParameter
{
    public required string Expression { get; set; }

    public required ItemType Type { get; set; }

    public required int Index { get; set; }

    public IReadOnlyDictionary<int, string>? ListItems { get; set; }
}