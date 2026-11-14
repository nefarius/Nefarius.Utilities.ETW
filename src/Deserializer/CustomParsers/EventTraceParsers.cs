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

    private static PropertyMetadata Int32(string name)
    {
        return Prop(_TDH_IN_TYPE.TDH_INTYPE_INT32, _TDH_OUT_TYPE.TDH_OUTTYPE_INT, name);
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

        private static readonly PropertyMetadata TimeZoneInformation =
            Prop(_TDH_IN_TYPE.TDH_INTYPE_NULL, _TDH_OUT_TYPE.TDH_OUTTYPE_NULL, "TimeZoneInformation", true, 7);

        private static readonly PropertyMetadata Bias = Int32("Bias");
        private static readonly PropertyMetadata StandardName = UnicodeString("StandardName");

        private static readonly PropertyMetadata StandardDate =
            Prop(_TDH_IN_TYPE.TDH_INTYPE_HEXDUMP, _TDH_OUT_TYPE.TDH_OUTTYPE_HEXBINARY, "StandardDate");

        private static readonly PropertyMetadata StandardBias = Int32("StandardBias");
        private static readonly PropertyMetadata DaylightName = UnicodeString("DaylightName");

        private static readonly PropertyMetadata DaylightDate =
            Prop(_TDH_IN_TYPE.TDH_INTYPE_HEXDUMP, _TDH_OUT_TYPE.TDH_OUTTYPE_HEXBINARY, "DaylightDate");

        private static readonly PropertyMetadata DaylightBias = Int32("DaylightBias");
        private static readonly PropertyMetadata BootTime = UInt64("BootTime");
        private static readonly PropertyMetadata PerfFreq = UInt64("PerfFreq");
        private static readonly PropertyMetadata StartTime = UInt64("StartTime");
        private static readonly PropertyMetadata ReservedFlags = UInt32("ReservedFlags");
        private static readonly PropertyMetadata BuffersLost = UInt32("BuffersLost");
        private static readonly PropertyMetadata SessionNameString = UnicodeString("SessionNameString");
        private static readonly PropertyMetadata LogFileNameString = UnicodeString("LogFileNameString");

        static HeaderParser()
        {
            EventMetadata = new EventMetadata(CustomParserGuids.EventTraceGuid, 0, 0, "MSNT_SystemTrace/EventTrace/Header",
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
            writer.WriteStructBegin();

            writer.WritePropertyBegin(Bias);
            writer.WriteInt32(reader.ReadInt32());
            writer.WritePropertyEnd();

            writer.WritePropertyBegin(StandardName);
            writer.WriteUnicodeString(reader.ReadUnicodeString(32));
            writer.WritePropertyEnd();

            writer.WritePropertyBegin(StandardDate);
            writer.WriteHexDump(reader.ReadBinary(16));
            writer.WritePropertyEnd();

            writer.WritePropertyBegin(StandardBias);
            writer.WriteInt32(reader.ReadInt32());
            writer.WritePropertyEnd();

            writer.WritePropertyBegin(DaylightName);
            writer.WriteUnicodeString(reader.ReadUnicodeString(32));
            writer.WritePropertyEnd();

            writer.WritePropertyBegin(DaylightDate);
            writer.WriteHexDump(reader.ReadBinary(16));
            writer.WritePropertyEnd();

            writer.WritePropertyBegin(DaylightBias);
            writer.WriteInt32(reader.ReadInt32());
            writer.WritePropertyEnd();

            writer.WriteStructEnd();
            writer.WritePropertyEnd();

            // alignment padding before the 64-bit fields
            reader.ReadUInt32();

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
        private static readonly PropertyMetadata ImageBase = Pointer("ImageBase");
        private static readonly PropertyMetadata ProcessId = UInt32("ProcessId");

        private static readonly PropertyMetadata GuidSig =
            Prop(_TDH_IN_TYPE.TDH_INTYPE_GUID, _TDH_OUT_TYPE.TDH_OUTTYPE_GUID, "GuidSig");

        private static readonly PropertyMetadata Age = UInt32("Age");

        private static readonly PropertyMetadata PdbFileName =
            Prop(_TDH_IN_TYPE.TDH_INTYPE_ANSISTRING, _TDH_OUT_TYPE.TDH_OUTTYPE_STRING, "PdbFileName");

        private static readonly EventMetadata EventMetadata = new(CustomParserGuids.EventTraceGuid, 0, 0,
            "MSNT_SystemTrace/EventTrace/DbgIdRSDS", [ImageBase, ProcessId, GuidSig, Age, PdbFileName]);

        public void Parse<T>(EventRecordReader reader, T writer, EventMetadata[] metadataArray,
            RuntimeEventMetadata runtimeMetadata)
            where T : IEtwWriter
        {
            writer.WriteEventBegin(EventMetadata, runtimeMetadata);

            writer.WritePropertyBegin(ImageBase);
            writer.WritePointer(reader.ReadPointer());
            writer.WritePropertyEnd();

            writer.WritePropertyBegin(ProcessId);
            writer.WriteUInt32(reader.ReadUInt32());
            writer.WritePropertyEnd();

            writer.WritePropertyBegin(GuidSig);
            writer.WriteGuid(reader.ReadGuid());
            writer.WritePropertyEnd();

            writer.WritePropertyBegin(Age);
            writer.WriteUInt32(reader.ReadUInt32());
            writer.WritePropertyEnd();

            writer.WritePropertyBegin(PdbFileName);
            writer.WriteAnsiString(reader.ReadAnsiString());
            writer.WritePropertyEnd();

            writer.WriteEventEnd();
        }
    }

    private sealed class BuildInfoParser : ICustomParser
    {
        private static readonly PropertyMetadata BuildInfo =
            Prop(_TDH_IN_TYPE.TDH_INTYPE_ANSISTRING, _TDH_OUT_TYPE.TDH_OUTTYPE_STRING, "BuildInfo");

        private static readonly EventMetadata EventMetadata = new(CustomParserGuids.EventTraceGuid, 0, 0,
            "MSNT_SystemTrace/EventTrace/BuildInfo", [BuildInfo]);

        public void Parse<T>(EventRecordReader reader, T writer, EventMetadata[] metadataArray,
            RuntimeEventMetadata runtimeMetadata)
            where T : IEtwWriter
        {
            writer.WriteEventBegin(EventMetadata, runtimeMetadata);

            writer.WritePropertyBegin(BuildInfo);
            writer.WriteAnsiString(reader.ReadAnsiString());
            writer.WritePropertyEnd();

            writer.WriteEventEnd();
        }
    }

    private sealed class PartitionInfoExtensionV2Parser : ICustomParser
    {
        private static readonly PropertyMetadata Data =
            Prop(_TDH_IN_TYPE.TDH_INTYPE_HEXDUMP, _TDH_OUT_TYPE.TDH_OUTTYPE_HEXBINARY, "Data");

        private static readonly EventMetadata EventMetadata = new(CustomParserGuids.EventTraceGuid, 0, 0,
            "MSNT_SystemTrace/EventTrace/PartitionInfoExtensionV2", [Data]);

        public void Parse<T>(EventRecordReader reader, T writer, EventMetadata[] metadataArray,
            RuntimeEventMetadata runtimeMetadata)
            where T : IEtwWriter
        {
            writer.WriteEventBegin(EventMetadata, runtimeMetadata);

            writer.WritePropertyBegin(Data);
            writer.WriteHexDump(reader.ReadBinary(reader.RemainingBytes));
            writer.WritePropertyEnd();

            writer.WriteEventEnd();
        }
    }
}
