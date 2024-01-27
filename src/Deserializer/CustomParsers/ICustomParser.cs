using System.Diagnostics.CodeAnalysis;

namespace Nefarius.Utilities.ETW.Deserializer.CustomParsers;

[SuppressMessage("ReSharper", "UnusedMemberInSuper.Global")]
internal interface ICustomParser
{
    void Parse<T>(EventRecordReader reader, T writer, EventMetadata[] metadataArray,
        RuntimeEventMetadata runtimeMetadata)
        where T : IEtwWriter;
}