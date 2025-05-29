namespace Nefarius.Utilities.ETW.Deserializer.WPP.TMF;

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