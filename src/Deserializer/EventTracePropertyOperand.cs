namespace Nefarius.Utilities.ETW.Deserializer;

internal sealed class EventTracePropertyOperand : IEventTracePropertyOperand
{
    public EventTracePropertyOperand(PropertyMetadata metadata, int propertyIndex, bool isVariableArray,
        bool isFixedArray, bool isVariableLength, bool isFixedLength, bool isWbemXml)
    {
        Metadata = metadata;
        PropertyIndex = propertyIndex;
        IsVariableArray = isVariableArray;
        IsFixedArray = isFixedArray;
        IsVariableLength = isVariableLength;
        IsFixedLength = isFixedLength;
        IsWbemXMLFragment = isWbemXml;
        Children = new List<IEventTracePropertyOperand>();
    }

    public PropertyMetadata Metadata { get; }

    public int PropertyIndex { get; }

    public bool IsVariableArray { get; }

    public IEventTracePropertyOperand VariableArraySize { get; private set; }

    public bool IsVariableLength { get; }

    public IEventTracePropertyOperand VariableLengthSize { get; private set; }

    public bool IsFixedArray { get; }

    public int FixedArraySize { get; private set; }

    public bool IsFixedLength { get; }

    public int FixedLengthSize { get; private set; }

    public bool IsWbemXMLFragment { get; }

    public bool IsReferencedByOtherProperties { get; set; }

    public List<IEventTracePropertyOperand> Children { get; }

    public void SetFixedArraySize(int fixedArraySize)
    {
        FixedArraySize = fixedArraySize;
    }

    public void SetVariableArraySize(IEventTracePropertyOperand reference)
    {
        VariableArraySize = reference;
    }

    public void SetFixedLengthSize(int fixedLengthSize)
    {
        FixedLengthSize = fixedLengthSize;
    }

    public void SetVariableLengthSize(IEventTracePropertyOperand reference)
    {
        VariableLengthSize = reference;
    }
}