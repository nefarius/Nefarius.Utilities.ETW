using System.ComponentModel.Design;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

using Windows.Win32.Foundation;

using Nefarius.Utilities.ETW.Deserializer.CustomParsers;
using Nefarius.Utilities.ETW.Deserializer.WPP.TMF;

using Smx.PDBSharp;
using Smx.PDBSharp.Symbols;

namespace Nefarius.Utilities.ETW.Deserializer;

/// <summary>
///     <see cref="EVENT_RECORD" /> parsing logic.
/// </summary>
/// <typeparam name="T">Implementation of <see cref="IEtwWriter" />.</typeparam>
internal sealed partial class Deserializer<T>
    where T : IEtwWriter
{
    [SuppressMessage("ReSharper", "StaticMemberInGenericType")]
    private static readonly Type ReaderType = typeof(EventRecordReader);

    [SuppressMessage("ReSharper", "StaticMemberInGenericType")]
    private static readonly Type EventMetadataArrayType = typeof(EventMetadata[]);

    [SuppressMessage("ReSharper", "StaticMemberInGenericType")]
    private static readonly Type RuntimeMetadataType = typeof(RuntimeEventMetadata);

    [SuppressMessage("ReSharper", "StaticMemberInGenericType")]
    private static readonly Regex InvalidCharacters = InvalidCharactersRegex();

    private static readonly Type WriterType = typeof(T);

    private readonly Dictionary<TraceEventKey, Action<EventRecordReader, T, EventMetadata[], RuntimeEventMetadata>>
        _actionTable = new();

    private readonly Func<Guid, Stream?>? _customProviderManifest;

    private readonly List<EventMetadata> _eventMetadataTableList = new();

    private readonly Dictionary<Guid, EventSourceManifest> _eventSourceManifestCache = new();

    private EventMetadata[] _eventMetadataTable;

    private T _writer;

    private Deserializer(T writer)
    {
        _writer = writer;
    }

    public Deserializer(T writer, Func<Guid, Stream?>? customProviderManifest) : this(writer)
    {
        _customProviderManifest = customProviderManifest;
    }

    public void ResetWriter(T writer)
    {
        _writer = writer;
    }

    public bool BufferCallback(IntPtr logfile)
    {
        return true;
    }

    private unsafe bool IsStringEvent(EVENT_RECORD* eventRecord)
    {
        return (eventRecord->EventHeader.Flags & PInvoke.EVENT_HEADER_FLAG_STRING_ONLY) != 0;
    }

    private unsafe bool IsWppEvent(EVENT_RECORD* eventRecord)
    {
        return (eventRecord->EventHeader.Flags & PInvoke.EVENT_HEADER_FLAG_TRACE_MESSAGE) != 0;
    }

    /// <summary>
    ///     Gets invoked for each record in the trace file(s).
    /// </summary>
    internal unsafe void Deserialize(EVENT_RECORD* eventRecord)
    {
        eventRecord->UserContext = eventRecord->UserData;
        EventRecordReader eventRecordReader = new(eventRecord);
        RuntimeEventMetadata runtimeMetadata = new(eventRecord);

        if (IsWppEvent(eventRecord))
        {
            //var tmfPath = @"D:\Downloads\tmftest\0e10805c-4632-3a74-c514-84b39bf9e7ba.tmf";
            var tmfPath = @"D:\Downloads\tmftest\1cd9540d-3b93-37c1-6f28-f268613fca07.tmf";

            using var fs = File.OpenText(tmfPath);
            var p = new Parser();
            p.Parse(fs);

        }

        TraceEventKey key = new(
            eventRecord->EventHeader.ProviderId,
            (eventRecord->EventHeader.Flags & PInvoke.EVENT_HEADER_FLAG_CLASSIC_HEADER) != 0
                ? eventRecord->EventHeader.EventDescriptor.Opcode
                : eventRecord->EventHeader.EventDescriptor.Id,
            eventRecord->EventHeader.EventDescriptor.Version);

        if (_actionTable.TryGetValue(key,
                out Action<EventRecordReader, T, EventMetadata[], RuntimeEventMetadata> action))
        {
            action(eventRecordReader, _writer, _eventMetadataTable, runtimeMetadata);
        }
        else
        {
            SlowLookup(eventRecord, eventRecordReader, runtimeMetadata, ref key);
        }
    }

    private static unsafe IEventTraceOperand? BuildOperandFromXml(EVENT_RECORD* eventRecord,
        Dictionary<Guid, EventSourceManifest> cache, EventRecordReader eventRecordReader, int metadataTableIndex)
    {
        Guid providerGuid = eventRecord->EventHeader.ProviderId;
        if (!cache.TryGetValue(providerGuid, out EventSourceManifest? manifest))
        {
            manifest = CreateEventSourceManifest(providerGuid, cache, eventRecord, eventRecordReader);
        }

        if (manifest == null)
        {
            return null;
        }

        return !manifest.IsComplete
            ? null
            : EventTraceOperandBuilder.Build(manifest.Schema, eventRecord->EventHeader.EventDescriptor.Id,
                metadataTableIndex);
    }

    private static unsafe IEventTraceOperand? BuildOperandFromTdh(EVENT_RECORD* eventRecord, int metadataTableIndex)
    {
        uint bufferSize;
        TRACE_EVENT_INFO* buffer = null;

        // Not Found
        if (PInvoke.TdhGetEventInformation(eventRecord, 0, null, buffer, &bufferSize) ==
            (uint)WIN32_ERROR.ERROR_NOT_FOUND)
        {
            return null;
        }

        buffer = (TRACE_EVENT_INFO*)Marshal.AllocHGlobal((int)bufferSize);
        PInvoke.TdhGetEventInformation(eventRecord, 0, null, buffer, &bufferSize);

        TRACE_EVENT_INFO* traceEventInfo = buffer;
        IEventTraceOperand traceEventOperand = EventTraceOperandBuilder.Build(traceEventInfo, metadataTableIndex);

        Marshal.FreeHGlobal((IntPtr)buffer);

        return traceEventOperand;
    }

    private static unsafe IEventTraceOperand BuildUnknownOperand(EVENT_RECORD* eventRecord, int metadataTableIndex)
    {
        return new UnknownOperandBuilder(eventRecord->EventHeader.ProviderId, metadataTableIndex);
    }

    private static unsafe EventSourceManifest? CreateEventSourceManifest(Guid providerGuid,
        Dictionary<Guid, EventSourceManifest> cache, EVENT_RECORD* eventRecord, EventRecordReader eventRecordReader)
    {
        // EventSource Schema events have the following signature:
        // { byte Format, byte MajorVersion, byte MinorVersion, byte Magic, ushort TotalChunks, ushort ChunkNumber } == 8 bytes, followed by the XML schema
        if (eventRecord->UserDataLength <= 8)
        {
            return null;
        }

        byte format = eventRecordReader.ReadUInt8();
        byte majorVersion = eventRecordReader.ReadUInt8();
        byte minorVersion = eventRecordReader.ReadUInt8();
        byte magic = eventRecordReader.ReadUInt8();
        ushort totalChunks = eventRecordReader.ReadUInt16();
        ushort chunkNumber = eventRecordReader.ReadUInt16();

        if (!(format == 1 && magic == 0x5B))
        {
            return null;
        }

        if (!cache.TryGetValue(providerGuid, out EventSourceManifest? manifest))
        {
            manifest = new EventSourceManifest(eventRecord->EventHeader.ProviderId, format, majorVersion, minorVersion,
                magic,
                totalChunks);
            cache.Add(providerGuid, manifest);
        }

        // if manifest is complete, maybe the data changed? ideally version should have changed
        // this is essentially a reset
        if (manifest.IsComplete && chunkNumber == 0)
        {
            cache[providerGuid] = manifest;
        }

        string schemaChunk = eventRecordReader.ReadAnsiString();
        manifest.AddChunk(schemaChunk);

        return manifest;
    }

    private unsafe IEventTraceOperand? BuildOperand(EVENT_RECORD* eventRecord, EventRecordReader eventRecordReader,
        int metadataTableIndex, ref bool isSpecialKernelTraceMetaDataEvent)
    {
        IEventTraceOperand? operand;
        Stream? customProvider = _customProviderManifest?.Invoke(eventRecord->EventHeader.ProviderId);

        if (customProvider is not null)
        {
            if (!_eventSourceManifestCache.TryGetValue(eventRecord->EventHeader.ProviderId,
                    out EventSourceManifest? manifest))
            {
                manifest = new EventSourceManifest(eventRecord->EventHeader.ProviderId, customProvider);
                _eventSourceManifestCache.Add(eventRecord->EventHeader.ProviderId, manifest);
            }

            operand = BuildOperandFromXml(eventRecord, _eventSourceManifestCache, eventRecordReader,
                metadataTableIndex);

            if (operand is not null)
            {
                return operand;
            }
        }

        if (eventRecord->EventHeader.ProviderId == CustomParserGuids.KernelTraceControlMetaDataGuid &&
            eventRecord->EventHeader.EventDescriptor.Opcode == 32)
        {
            isSpecialKernelTraceMetaDataEvent = true;
            return EventTraceOperandBuilder.Build((TRACE_EVENT_INFO*)eventRecord->UserData, metadataTableIndex);
        }


        if ((operand = BuildOperandFromTdh(eventRecord, metadataTableIndex)) == null)
        {
            operand = BuildOperandFromXml(eventRecord, _eventSourceManifestCache, eventRecordReader,
                metadataTableIndex);
        }

        if (operand == null && eventRecord->EventHeader.EventDescriptor.Id != 65534) // don't show manifest events
        {
            operand = BuildUnknownOperand(eventRecord, metadataTableIndex);
        }

        return operand;
    }

    /// <summary>
    ///     Here happens the decision-making on which parser to use for a given provider.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private unsafe bool CustomParserLookup(EVENT_RECORD* eventRecord, ref TraceEventKey key)
    {
        bool success;

        // events added by KernelTraceControl.dll (i.e. Microsoft tools like WPR and PerfView)
        if (eventRecord->EventHeader.ProviderId == CustomParserGuids.KernelTraceControlImageIdGuid)
        {
            switch (eventRecord->EventHeader.EventDescriptor.Opcode)
            {
                case 0:
                    _actionTable.Add(key, new KernelTraceControlImageIdParser().Parse);
                    success = true;
                    break;
                case 36:
                    _actionTable.Add(key, new KernelTraceControlDbgIdParser().Parse);
                    success = true;
                    break;
                case 64:
                    _actionTable.Add(key, new KernelTraceControlImageIdFileVersionParser().Parse);
                    success = true;
                    break;
                default:
                    success = false;
                    break;
            }
        }
        // events by the Kernel Stack Walker (need this because the MOF events always says 32 stacks, but in reality there can be fewer or more
        else if (eventRecord->EventHeader.ProviderId == CustomParserGuids.KernelStackWalkGuid)
        {
            if (eventRecord->EventHeader.EventDescriptor.Opcode == 32)
            {
                _actionTable.Add(key, new KernelStackWalkEventParser().Parse);
                success = true;
            }
            else
            {
                success = false;
            }
        }
        else
        {
            success = false;
        }

        return success;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private unsafe void SlowLookup(EVENT_RECORD* eventRecord, EventRecordReader eventRecordReader,
        RuntimeEventMetadata runtimeMetadata, ref TraceEventKey key)
    {
        if (CustomParserLookup(eventRecord, ref key))
        {
            return;
        }

        bool isSpecialKernelTraceMetaDataEvent = false;
        IEventTraceOperand? operand = BuildOperand(eventRecord, eventRecordReader, _eventMetadataTableList.Count,
            ref isSpecialKernelTraceMetaDataEvent);
        if (operand == null)
        {
            return;
        }

        _eventMetadataTableList.Add(operand.Metadata);
        _eventMetadataTable = _eventMetadataTableList.ToArray(); // TODO: Need to improve this

        ParameterExpression eventRecordReaderParam = Expression.Parameter(ReaderType);
        ParameterExpression eventWriterParam = Expression.Parameter(WriterType);
        ParameterExpression eventMetadataTableParam = Expression.Parameter(EventMetadataArrayType);
        ParameterExpression runtimeMetadataParam = Expression.Parameter(RuntimeMetadataType);

        ParameterExpression[] parameters =
        [
            eventRecordReaderParam, eventWriterParam, eventMetadataTableParam, runtimeMetadataParam
        ];
        string name = Regex.Replace(InvalidCharacters.Replace(operand.Metadata.Name, "_"), @"\s+", "_");
        Expression body = EventTraceOperandExpressionBuilder.Build(operand, eventRecordReaderParam,
            eventWriterParam, eventMetadataTableParam, runtimeMetadataParam);
        LambdaExpression expression =
            Expression.Lambda<Action<EventRecordReader, T, EventMetadata[], RuntimeEventMetadata>>(body,
                "Read_" + name, parameters);
        Action<EventRecordReader, T, EventMetadata[], RuntimeEventMetadata> action =
            (Action<EventRecordReader, T, EventMetadata[], RuntimeEventMetadata>)expression.Compile(false);

        if (isSpecialKernelTraceMetaDataEvent)
        {
            TRACE_EVENT_INFO* e = (TRACE_EVENT_INFO*)eventRecord->UserContext;
            _actionTable.AddOrUpdate(
                new TraceEventKey(e->ProviderGuid,
                    e->EventGuid == Guid.Empty ? e->EventDescriptor.Id : e->EventDescriptor.Opcode,
                    e->EventDescriptor.Version),
                action);
        }
        else
        {
            _actionTable.Add(key, action);
            action(eventRecordReader, _writer, _eventMetadataTable, runtimeMetadata);
        }
    }

    [GeneratedRegex("[:\\/*?\"<>|\"-]")]
    private static partial Regex InvalidCharactersRegex();
}