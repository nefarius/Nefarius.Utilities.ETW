namespace Nefarius.Utilities.ETW.Deserializer.CustomParsers;

/// <summary>
///     Decoders for the classic <c>EventTrace</c> group events (trace header, build info, debug ID, partition info) that
///     the native path obtains from TDH and the WMI MOF repository, which does not exist off Windows.
/// </summary>
internal static class EventTraceParsers
{
    public const byte HeaderOpcode = 0;

    public const byte DbgIdRsdsOpcode = 64;

    public const byte BuildInfoOpcode = 66;

    public const byte PartitionInfoExtensionV2Opcode = 80;

    public static bool TryCreate(byte opcode, out ICustomParser? parser)
    {
        parser = opcode switch
        {
            HeaderOpcode => new HeaderParser(),
            DbgIdRsdsOpcode => new DbgIdRsdsParser(),
            BuildInfoOpcode => new BuildInfoParser(),
            PartitionInfoExtensionV2Opcode => new PartitionInfoExtensionV2Parser(),
            _ => null
        };

        return parser is not null;
    }

    private static EventMetadata Event(string name, PropertyMetadata[] properties)
    {
        return new EventMetadata(CustomParserGuids.SystemTraceControlGuid, 0, 2, name, properties);
    }

    private static unsafe int PointerBytes(EventRecordReader reader)
    {
        return (reader.NativeEventRecord->EventHeader.Flags & PInvoke.EVENT_HEADER_FLAG_32_BIT_HEADER) != 0 ? 4 : 8;
    }

    private static PropertyMetadata Prop(_TDH_IN_TYPE inType, _TDH_OUT_TYPE outType, string name,
        bool isStruct = false, int children = 0)
    {
        return new PropertyMetadata(inType, outType, name, false, isStruct, children, null);
    }

    private static PropertyMetadata UInt32(string name)
    {
        return Prop(_TDH_IN_TYPE.TDH_INTYPE_UINT32, _TDH_OUT_TYPE.TDH_OUTTYPE_UNSIGNEDINT, name);
    }

    private static PropertyMetadata UInt64(string name)
    {
        return Prop(_TDH_IN_TYPE.TDH_INTYPE_UINT64, _TDH_OUT_TYPE.TDH_OUTTYPE_UNSIGNEDLONG, name);
    }

    private static PropertyMetadata Int64(string name)
    {
        return Prop(_TDH_IN_TYPE.TDH_INTYPE_INT64, _TDH_OUT_TYPE.TDH_OUTTYPE_LONG, name);
    }

    private static PropertyMetadata Pointer(string name)
    {
        return Prop(_TDH_IN_TYPE.TDH_INTYPE_POINTER, _TDH_OUT_TYPE.TDH_OUTTYPE_HEXINT64, name);
    }

    private static PropertyMetadata UnicodeString(string name)
    {
        return Prop(_TDH_IN_TYPE.TDH_INTYPE_UNICODESTRING, _TDH_OUT_TYPE.TDH_OUTTYPE_STRING, name);
    }

    private sealed class HeaderParser : ICustomParser
    {
        private static readonly EventMetadata EventMetadata;

        private static readonly PropertyMetadata BufferSize = UInt32("BufferSize");
        private static readonly PropertyMetadata Version = UInt32("Version");
        private static readonly PropertyMetadata ProviderVersion = UInt32("ProviderVersion");
        private static readonly PropertyMetadata NumberOfProcessors = UInt32("NumberOfProcessors");
        private static readonly PropertyMetadata EndTime = UInt64("EndTime");
        private static readonly PropertyMetadata TimerResolution = UInt32("TimerResolution");
        private static readonly PropertyMetadata MaxFileSize = UInt32("MaxFileSize");
        private static readonly PropertyMetadata LogFileMode = UInt32("LogFileMode");
        private static readonly PropertyMetadata BuffersWritten = UInt32("BuffersWritten");
        private static readonly PropertyMetadata StartBuffers = UInt32("StartBuffers");
        private static readonly PropertyMetadata PointerSize = UInt32("PointerSize");
        private static readonly PropertyMetadata EventsLost = UInt32("EventsLost");
        private static readonly PropertyMetadata CpuSpeedInMHz = UInt32("CPUSpeed");
        private static readonly PropertyMetadata LoggerName = Pointer("LoggerName");
        private static readonly PropertyMetadata LogFileName = Pointer("LogFileName");

        // TDH exposes the timezone block, including its alignment padding, as a byte array.
        private const int TimeZoneBytes = 176;

        private static readonly PropertyMetadata TimeZoneInformation =
            Prop(_TDH_IN_TYPE.TDH_INTYPE_UINT8, _TDH_OUT_TYPE.TDH_OUTTYPE_UNSIGNEDINT, "TimeZoneInformation");

        private static readonly PropertyMetadata BootTime = UInt64("BootTime");
        private static readonly PropertyMetadata PerfFreq = UInt64("PerfFreq");
        private static readonly PropertyMetadata StartTime = UInt64("StartTime");
        private static readonly PropertyMetadata ReservedFlags = UInt32("ReservedFlags");
        private static readonly PropertyMetadata BuffersLost = UInt32("BuffersLost");
        private static readonly PropertyMetadata SessionNameString = UnicodeString("SessionNameString");
        private static readonly PropertyMetadata LogFileNameString = UnicodeString("LogFileNameString");

        static HeaderParser()
        {
            EventMetadata = Event("MSNT_SystemTrace/EventTrace/Header",
            [
                BufferSize, Version, ProviderVersion, NumberOfProcessors, EndTime, TimerResolution, MaxFileSize,
                LogFileMode, BuffersWritten, StartBuffers, PointerSize, EventsLost, CpuSpeedInMHz, LoggerName,
                LogFileName, TimeZoneInformation, BootTime, PerfFreq, StartTime, ReservedFlags, BuffersLost,
                SessionNameString, LogFileNameString
            ]);
        }

        public void Parse<T>(EventRecordReader reader, T writer, EventMetadata[] metadataArray,
            RuntimeEventMetadata runtimeMetadata)
            where T : IEtwWriter
        {
            int pointerBytes = PointerBytes(reader);
            // Fixed prefix through BuffersLost: integers before the pointers, both pointers, the timezone
            // block with its alignment padding, and the trailing timestamps and counters.
            if (reader.RemainingBytes < 56 + pointerBytes * 2 + TimeZoneBytes + 24 + 8)
            {
                return;
            }

            writer.WriteEventBegin(EventMetadata, runtimeMetadata);

            WriteUInt32(writer, BufferSize, reader.ReadUInt32());
            WriteUInt32(writer, Version, reader.ReadUInt32());
            WriteUInt32(writer, ProviderVersion, reader.ReadUInt32());
            WriteUInt32(writer, NumberOfProcessors, reader.ReadUInt32());

            writer.WritePropertyBegin(EndTime);
            writer.WriteUInt64(reader.ReadUInt64());
            writer.WritePropertyEnd();

            WriteUInt32(writer, TimerResolution, reader.ReadUInt32());
            WriteUInt32(writer, MaxFileSize, reader.ReadUInt32());
            WriteUInt32(writer, LogFileMode, reader.ReadUInt32());
            WriteUInt32(writer, BuffersWritten, reader.ReadUInt32());
            WriteUInt32(writer, StartBuffers, reader.ReadUInt32());
            WriteUInt32(writer, PointerSize, reader.ReadUInt32());
            WriteUInt32(writer, EventsLost, reader.ReadUInt32());
            WriteUInt32(writer, CpuSpeedInMHz, reader.ReadUInt32());

            writer.WritePropertyBegin(LoggerName);
            writer.WritePointer(reader.ReadPointer());
            writer.WritePropertyEnd();

            writer.WritePropertyBegin(LogFileName);
            writer.WritePointer(reader.ReadPointer());
            writer.WritePropertyEnd();

            writer.WritePropertyBegin(TimeZoneInformation);
            writer.WriteArrayBegin();
            foreach (byte item in reader.ReadBinary(TimeZoneBytes))
            {
                writer.WriteUInt8(item);
            }

            writer.WriteArrayEnd();
            writer.WritePropertyEnd();

            writer.WritePropertyBegin(BootTime);
            writer.WriteUInt64(reader.ReadUInt64());
            writer.WritePropertyEnd();

            writer.WritePropertyBegin(PerfFreq);
            writer.WriteUInt64(reader.ReadUInt64());
            writer.WritePropertyEnd();

            writer.WritePropertyBegin(StartTime);
            writer.WriteUInt64(reader.ReadUInt64());
            writer.WritePropertyEnd();

            WriteUInt32(writer, ReservedFlags, reader.ReadUInt32());
            WriteUInt32(writer, BuffersLost, reader.ReadUInt32());

            writer.WritePropertyBegin(SessionNameString);
            writer.WriteUnicodeString(reader.ReadUnicodeString());
            writer.WritePropertyEnd();

            writer.WritePropertyBegin(LogFileNameString);
            writer.WriteUnicodeString(reader.ReadUnicodeString());
            writer.WritePropertyEnd();

            writer.WriteEventEnd();
        }

        private static void WriteUInt32<T>(T writer, PropertyMetadata property, uint value) where T : IEtwWriter
        {
            writer.WritePropertyBegin(property);
            writer.WriteUInt32(value);
            writer.WritePropertyEnd();
        }
    }

    private sealed class DbgIdRsdsParser : ICustomParser
    {
        private static readonly PropertyMetadata Guid =
            Prop(_TDH_IN_TYPE.TDH_INTYPE_GUID, _TDH_OUT_TYPE.TDH_OUTTYPE_GUID, "Guid");

        private static readonly PropertyMetadata Age = UInt32("Age");

        private static readonly PropertyMetadata PdbName =
            Prop(_TDH_IN_TYPE.TDH_INTYPE_ANSISTRING, _TDH_OUT_TYPE.TDH_OUTTYPE_STRING, "PdbName");

        private static readonly EventMetadata EventMetadata =
            Event("MSNT_SystemTrace/EventTrace/DbgIdRSDS", [Guid, Age, PdbName]);

        public void Parse<T>(EventRecordReader reader, T writer, EventMetadata[] metadataArray,
            RuntimeEventMetadata runtimeMetadata)
            where T : IEtwWriter
        {
            // Guid and Age. The file name is a bounded terminator scan.
            if (reader.RemainingBytes < 16 + 4)
            {
                return;
            }

            writer.WriteEventBegin(EventMetadata, runtimeMetadata);

            writer.WritePropertyBegin(Guid);
            writer.WriteGuid(reader.ReadGuid());
            writer.WritePropertyEnd();

            writer.WritePropertyBegin(Age);
            writer.WriteUInt32(reader.ReadUInt32());
            writer.WritePropertyEnd();

            writer.WritePropertyBegin(PdbName);
            writer.WriteAnsiString(reader.ReadAnsiString());
            writer.WritePropertyEnd();

            writer.WriteEventEnd();
        }
    }

    private sealed class BuildInfoParser : ICustomParser
    {
        private static readonly PropertyMetadata BuildString =
            Prop(_TDH_IN_TYPE.TDH_INTYPE_ANSISTRING, _TDH_OUT_TYPE.TDH_OUTTYPE_STRING, "BuildString");

        private static readonly EventMetadata EventMetadata =
            Event("MSNT_SystemTrace/EventTrace/BuildInfo", [BuildString]);

        public void Parse<T>(EventRecordReader reader, T writer, EventMetadata[] metadataArray,
            RuntimeEventMetadata runtimeMetadata)
            where T : IEtwWriter
        {
            writer.WriteEventBegin(EventMetadata, runtimeMetadata);

            writer.WritePropertyBegin(BuildString);
            writer.WriteAnsiString(reader.ReadAnsiString());
            writer.WritePropertyEnd();

            writer.WriteEventEnd();
        }
    }

    private sealed class PartitionInfoExtensionV2Parser : ICustomParser
    {
        private static readonly PropertyMetadata EventVersion =
            Prop(_TDH_IN_TYPE.TDH_INTYPE_UINT16, _TDH_OUT_TYPE.TDH_OUTTYPE_UNSIGNEDINT, "EventVersion");

        private static readonly PropertyMetadata Reserved =
            Prop(_TDH_IN_TYPE.TDH_INTYPE_UINT16, _TDH_OUT_TYPE.TDH_OUTTYPE_UNSIGNEDINT, "Reserved");

        private static readonly PropertyMetadata PartitionType = UInt32("PartitionType");
        private static readonly PropertyMetadata QpcOffsetFromRoot = Int64("QpcOffsetFromRoot");
        private static readonly PropertyMetadata PartitionId = UnicodeString("PartitionId");
        private static readonly PropertyMetadata ParentId = UnicodeString("ParentId");

        private static readonly EventMetadata EventMetadata = Event(
            "MSNT_SystemTrace/EventTrace/PartitionInfoExtensionV2",
            [EventVersion, Reserved, PartitionType, QpcOffsetFromRoot, PartitionId, ParentId]);

        public void Parse<T>(EventRecordReader reader, T writer, EventMetadata[] metadataArray,
            RuntimeEventMetadata runtimeMetadata)
            where T : IEtwWriter
        {
            if (reader.RemainingBytes < 2 + 2 + 4 + 8)
            {
                return;
            }

            writer.WriteEventBegin(EventMetadata, runtimeMetadata);

            writer.WritePropertyBegin(EventVersion);
            writer.WriteUInt16(reader.ReadUInt16());
            writer.WritePropertyEnd();

            writer.WritePropertyBegin(Reserved);
            writer.WriteUInt16(reader.ReadUInt16());
            writer.WritePropertyEnd();

            writer.WritePropertyBegin(PartitionType);
            writer.WriteUInt32(reader.ReadUInt32());
            writer.WritePropertyEnd();

            writer.WritePropertyBegin(QpcOffsetFromRoot);
            writer.WriteInt64(reader.ReadInt64());
            writer.WritePropertyEnd();

            writer.WritePropertyBegin(PartitionId);
            writer.WriteUnicodeString(reader.ReadUnicodeString());
            writer.WritePropertyEnd();

            writer.WritePropertyBegin(ParentId);
            writer.WriteUnicodeString(reader.ReadUnicodeString());
            writer.WritePropertyEnd();

            writer.WriteEventEnd();
        }
    }
}
