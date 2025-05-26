using System.Diagnostics.CodeAnalysis;

namespace Nefarius.Utilities.ETW.Deserializer.WPP.TMF;

/// <summary>
///     Possible trace message parameter types.
/// </summary>
/// <remarks>
///     Partial sources from
///     <a href="https://learn.microsoft.com/en-us/windows/win32/wes/eventmanifestschema-inputtype-complextype#remarks">here</a>
///     and
///     <a href="https://learn.microsoft.com/en-us/windows/win32/wes/eventmanifestschema-outputtype-complextype#remarks">here</a>
/// </remarks>
[SuppressMessage("ReSharper", "InconsistentNaming")]
[SuppressMessage("ReSharper", "UnusedMember.Global")]
public enum ItemType
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
    ItemString,

    /// <summary>
    ///     A GUID value that is formatted in the registry string form, {xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}.
    /// </summary>
    ItemGuid
}

public readonly struct FunctionParameter
{
    /// <summary>
    ///     The expression (variable) passed to the function parameter.
    /// </summary>
    public required string Expression { get; init; }

    /// <summary>
    ///     The data type of the function parameter.
    /// </summary>
    public required ItemType Type { get; init; }

    /// <summary>
    ///     The index used in the message format string to substitute this type with.
    /// </summary>
    public required int Index { get; init; }

    /// <summary>
    ///     List item values as their string representation.
    /// </summary>
    /// <remarks>Only populated if <see cref="Type" /> is <see cref="ItemType.ItemListByte" />.</remarks>
    public IReadOnlyDictionary<int, string>? ListItems { get; init; }
}