﻿using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

using Windows.Win32;
using Windows.Win32.System.Diagnostics.Etw;

using Nefarius.Utilities.ETW.Deserializer.CustomParsers;

namespace Nefarius.Utilities.ETW.Deserializer;

public sealed class Deserializer<T>
    where T : IEtwWriter
{
    private static readonly Type ReaderType = typeof(EventRecordReader);

    private static readonly Type EventMetadataArrayType = typeof(EventMetadata[]);

    private static readonly Type RuntimeMetadataType = typeof(RuntimeEventMetadata);

    private static readonly Regex InvalidCharacters = new("[:\\/*?\"<>|\"-]");

    private static readonly Type WriterType = typeof(T);

    private readonly Func<Guid, Stream?>? _customProviderManifest;

    private readonly Dictionary<TraceEventKey, Action<EventRecordReader, T, EventMetadata[], RuntimeEventMetadata>>
        actionTable = new();

    private readonly List<EventMetadata> eventMetadataTableList = new();

    private readonly Dictionary<Guid, EventSourceManifest> eventSourceManifestCache = new();

    private EventMetadata[] eventMetadataTable;

    private T writer;

    public Deserializer(T writer)
    {
        this.writer = writer;
    }

    public Deserializer(T writer, Func<Guid, Stream?>? customProviderManifest) : this(writer)
    {
        _customProviderManifest = customProviderManifest;
    }

    public void ResetWriter(T writer)
    {
        this.writer = writer;
    }
    
    public bool BufferCallback(IntPtr logfile)
    {
        return true;
    }

    internal unsafe void Deserialize(EVENT_RECORD* eventRecord)
    {
        eventRecord->UserContext = eventRecord->UserData;
        EventRecordReader eventRecordReader = new EventRecordReader(eventRecord);
        RuntimeEventMetadata runtimeMetadata = new RuntimeEventMetadata(eventRecord);

        TraceEventKey key = new TraceEventKey(
            eventRecord->EventHeader.ProviderId,
            (eventRecord->EventHeader.Flags & PInvoke.EVENT_HEADER_FLAG_CLASSIC_HEADER) != 0 ? eventRecord->EventHeader.EventDescriptor.Opcode : eventRecord->EventHeader.EventDescriptor.Id,
            eventRecord->EventHeader.EventDescriptor.Version);

        Action<EventRecordReader, T, EventMetadata[], RuntimeEventMetadata> action;
        if (actionTable.TryGetValue(key, out action))
        {
            action(eventRecordReader, writer, eventMetadataTable, runtimeMetadata);
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
            : EventTraceOperandBuilder.Build(manifest.Schema, eventRecord->EventHeader.EventDescriptor.Id, metadataTableIndex);
    }

    private static unsafe IEventTraceOperand? BuildOperandFromTdh(EVENT_RECORD* eventRecord, int metadataTableIndex)
    {
        uint bufferSize;
        Windows.Win32.System.Diagnostics.Etw.TRACE_EVENT_INFO* buffer = null;

        // Not Found
        if (PInvoke.TdhGetEventInformation(eventRecord, 0, null, buffer, &bufferSize) == 1168)
        {
            return null;
        }

        buffer = (Windows.Win32.System.Diagnostics.Etw.TRACE_EVENT_INFO*)Marshal.AllocHGlobal((int)bufferSize);
        PInvoke.TdhGetEventInformation(eventRecord, 0, null, buffer, &bufferSize);

        TRACE_EVENT_INFO* traceEventInfo = (TRACE_EVENT_INFO*)buffer;
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

        EventSourceManifest? manifest;
        if (!cache.TryGetValue(providerGuid, out manifest))
        {
            manifest = new EventSourceManifest(eventRecord->EventHeader.ProviderId, format, majorVersion, minorVersion, magic,
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
            if (!eventSourceManifestCache.TryGetValue(eventRecord->EventHeader.ProviderId, out EventSourceManifest? manifest))
            {
                manifest = new EventSourceManifest(eventRecord->EventHeader.ProviderId, customProvider);
                eventSourceManifestCache.Add(eventRecord->EventHeader.ProviderId, manifest);
            }

            operand = BuildOperandFromXml(eventRecord, eventSourceManifestCache, eventRecordReader, metadataTableIndex);

            if (operand is not null)
            {
                return operand;
            }
        }

        if (eventRecord->EventHeader.ProviderId == CustomParserGuids.KernelTraceControlMetaDataGuid && eventRecord->EventHeader.EventDescriptor.Opcode == 32)
        {
            isSpecialKernelTraceMetaDataEvent = true;
            return EventTraceOperandBuilder.Build((TRACE_EVENT_INFO*)eventRecord->UserData, metadataTableIndex);
        }


        if ((operand = BuildOperandFromTdh(eventRecord, metadataTableIndex)) == null)
        {
            operand = BuildOperandFromXml(eventRecord, eventSourceManifestCache, eventRecordReader, metadataTableIndex);
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
                    actionTable.Add(key, new KernelTraceControlImageIdParser().Parse);
                    success = true;
                    break;
                case 36:
                    actionTable.Add(key, new KernelTraceControlDbgIdParser().Parse);
                    success = true;
                    break;
                case 64:
                    actionTable.Add(key, new KernelTraceControlImageIdFileVersionParser().Parse);
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
                actionTable.Add(key, new KernelStackWalkEventParser().Parse);
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
        IEventTraceOperand? operand = BuildOperand(eventRecord, eventRecordReader, eventMetadataTableList.Count,
            ref isSpecialKernelTraceMetaDataEvent);
        if (operand == null)
        {
            return;
        }

        eventMetadataTableList.Add(operand.Metadata);
        eventMetadataTable = eventMetadataTableList.ToArray(); // TODO: Need to improve this

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
            actionTable.AddOrUpdate(
                new TraceEventKey(e->ProviderGuid, e->EventGuid == Guid.Empty ? e->EventDescriptor.Id : e->EventDescriptor.Opcode, e->EventDescriptor.Version),
                action);
        }
        else
        {
            actionTable.Add(key, action);
            action(eventRecordReader, writer, eventMetadataTable, runtimeMetadata);
        }
    }
}