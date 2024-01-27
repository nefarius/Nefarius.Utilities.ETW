namespace Nefarius.Utilities.ETW.Deserializer;

public interface IEventTraceOperand
{
    int EventMetadataTableIndex { get; }

    EventMetadata Metadata { get; }

    IEnumerable<IEventTracePropertyOperand> EventPropertyOperands { get; }
}