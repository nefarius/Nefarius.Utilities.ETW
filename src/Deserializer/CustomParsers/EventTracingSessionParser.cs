namespace Nefarius.Utilities.ETW.Deserializer.CustomParsers;

internal sealed class EventTracingSessionParser : ICustomParser
{
    public void Parse<T>(EventRecordReader reader, T writer, EventMetadata[] metadataArray,
        RuntimeEventMetadata runtimeMetadata) where T : IEtwWriter
    {
        throw new NotImplementedException();
    }
}