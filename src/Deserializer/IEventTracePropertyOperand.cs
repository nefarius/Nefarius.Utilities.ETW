namespace Nefarius.Utilities.ETW.Deserializer;

internal interface IEventTracePropertyOperand
{
    PropertyMetadata Metadata { get; }

    int PropertyIndex { get; }

    bool IsVariableArray { get; }

    IEventTracePropertyOperand VariableArraySize { get; }

    bool IsVariableLength { get; }

    IEventTracePropertyOperand VariableLengthSize { get; }

    bool IsFixedArray { get; }

    int FixedArraySize { get; }

    /// <summary>
    ///     True when the length of a Unicode string is counted in characters (manifest XML) instead of bytes.
    /// </summary>
    bool LengthIsInCharacters { get; }

    bool IsFixedLength { get; }

    int FixedLengthSize { get; }

    bool IsWbemXMLFragment { get; }

    bool IsReferencedByOtherProperties { get; }

    List<IEventTracePropertyOperand> Children { get; }
}