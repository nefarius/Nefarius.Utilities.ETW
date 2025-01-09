using System.Runtime.InteropServices.ComTypes;

using Nefarius.Utilities.ETW.Deserializer.WPP;

namespace Nefarius.Utilities.ETW.Deserializer.CustomParsers;

internal sealed class WppTraceEventParser : ICustomParser
{
    private static readonly EventMetadata EventMetadata;

    private static readonly PropertyMetadata VersionMetadata;
    private static readonly PropertyMetadata TraceGuidMetadata;
    private static readonly PropertyMetadata GuidNameMetadata;
    private static readonly PropertyMetadata GuidTypeNameMetadata;
    private static readonly PropertyMetadata ThreadIdMetadata;
    private static readonly PropertyMetadata SystemTimeMetadata;
    private static readonly PropertyMetadata UserTimeMetadata;
    private static readonly PropertyMetadata KernelTimeMetadata;
    private static readonly PropertyMetadata SequenceNumMetadata;
    private static readonly PropertyMetadata ProcessIdMetadata;
    private static readonly PropertyMetadata CpuNumberMetadata;
    private static readonly PropertyMetadata IndentMetadata;
    private static readonly PropertyMetadata FlagsNameMetadata;
    private static readonly PropertyMetadata LevelNameMetadata;
    private static readonly PropertyMetadata FunctionNameMetadata;
    private static readonly PropertyMetadata ComponentNameMetadata;
    private static readonly PropertyMetadata SubComponentNameMetadata;
    private static readonly PropertyMetadata FormattedStringMetadata;
    private static readonly PropertyMetadata RawSystemTimeMetadata;
    private static readonly PropertyMetadata ProviderGuidMetadata;

    private readonly DecodingContext _decodingContext;

    static WppTraceEventParser()
    {
        VersionMetadata = new PropertyMetadata(_TDH_IN_TYPE.TDH_INTYPE_UINT32, _TDH_OUT_TYPE.TDH_OUTTYPE_UNSIGNEDINT,
            nameof(WppEventRecord.Version), false, false, 0, null);
        TraceGuidMetadata = new PropertyMetadata(_TDH_IN_TYPE.TDH_INTYPE_GUID, _TDH_OUT_TYPE.TDH_OUTTYPE_GUID,
            nameof(WppEventRecord.TraceGuid), false, true, 0, null);
        GuidNameMetadata = new PropertyMetadata(_TDH_IN_TYPE.TDH_INTYPE_UNICODESTRING, _TDH_OUT_TYPE.TDH_OUTTYPE_STRING,
            nameof(WppEventRecord.GuidName), false, false, 0, null);
        GuidTypeNameMetadata = new PropertyMetadata(_TDH_IN_TYPE.TDH_INTYPE_UNICODESTRING,
            _TDH_OUT_TYPE.TDH_OUTTYPE_STRING,
            nameof(WppEventRecord.GuidTypeName), false, false, 0, null);
        ThreadIdMetadata = new PropertyMetadata(_TDH_IN_TYPE.TDH_INTYPE_UINT32, _TDH_OUT_TYPE.TDH_OUTTYPE_UNSIGNEDINT,
            nameof(WppEventRecord.ThreadId), false, false, 0, null);
        SystemTimeMetadata = new PropertyMetadata(_TDH_IN_TYPE.TDH_INTYPE_SYSTEMTIME,
            _TDH_OUT_TYPE.TDH_OUTTYPE_DATETIME,
            nameof(WppEventRecord.SystemTime), false, false, 0, null);
        UserTimeMetadata = new PropertyMetadata(_TDH_IN_TYPE.TDH_INTYPE_UINT32, _TDH_OUT_TYPE.TDH_OUTTYPE_UNSIGNEDINT,
            nameof(WppEventRecord.UserTime), false, false, 0, null);
        KernelTimeMetadata = new PropertyMetadata(_TDH_IN_TYPE.TDH_INTYPE_UINT32, _TDH_OUT_TYPE.TDH_OUTTYPE_UNSIGNEDINT,
            nameof(WppEventRecord.KernelTime), false, false, 0, null);
        SequenceNumMetadata = new PropertyMetadata(_TDH_IN_TYPE.TDH_INTYPE_UINT32,
            _TDH_OUT_TYPE.TDH_OUTTYPE_UNSIGNEDINT,
            nameof(WppEventRecord.SequenceNum), false, false, 0, null);
        UserTimeMetadata = new PropertyMetadata(_TDH_IN_TYPE.TDH_INTYPE_UINT32, _TDH_OUT_TYPE.TDH_OUTTYPE_UNSIGNEDINT,
            nameof(WppEventRecord.UserTime), false, false, 0, null);
        ProcessIdMetadata = new PropertyMetadata(_TDH_IN_TYPE.TDH_INTYPE_UINT32, _TDH_OUT_TYPE.TDH_OUTTYPE_UNSIGNEDINT,
            nameof(WppEventRecord.ProcessId), false, false, 0, null);
        CpuNumberMetadata = new PropertyMetadata(_TDH_IN_TYPE.TDH_INTYPE_UINT32, _TDH_OUT_TYPE.TDH_OUTTYPE_UNSIGNEDINT,
            nameof(WppEventRecord.CpuNumber), false, false, 0, null);
        IndentMetadata = new PropertyMetadata(_TDH_IN_TYPE.TDH_INTYPE_UINT32, _TDH_OUT_TYPE.TDH_OUTTYPE_UNSIGNEDINT,
            nameof(WppEventRecord.Indent), false, false, 0, null);
        FlagsNameMetadata = new PropertyMetadata(_TDH_IN_TYPE.TDH_INTYPE_UNICODESTRING,
            _TDH_OUT_TYPE.TDH_OUTTYPE_STRING,
            nameof(WppEventRecord.FlagsName), false, false, 0, null);
        LevelNameMetadata = new PropertyMetadata(_TDH_IN_TYPE.TDH_INTYPE_UNICODESTRING,
            _TDH_OUT_TYPE.TDH_OUTTYPE_STRING,
            nameof(WppEventRecord.LevelName), false, false, 0, null);
        FunctionNameMetadata = new PropertyMetadata(_TDH_IN_TYPE.TDH_INTYPE_UNICODESTRING,
            _TDH_OUT_TYPE.TDH_OUTTYPE_STRING,
            nameof(WppEventRecord.FunctionName), false, false, 0, null);
        ComponentNameMetadata = new PropertyMetadata(_TDH_IN_TYPE.TDH_INTYPE_UNICODESTRING,
            _TDH_OUT_TYPE.TDH_OUTTYPE_STRING,
            nameof(WppEventRecord.ComponentName), false, false, 0, null);
        SubComponentNameMetadata = new PropertyMetadata(_TDH_IN_TYPE.TDH_INTYPE_UNICODESTRING,
            _TDH_OUT_TYPE.TDH_OUTTYPE_STRING,
            nameof(WppEventRecord.SubComponentName), false, false, 0, null);
        FormattedStringMetadata = new PropertyMetadata(_TDH_IN_TYPE.TDH_INTYPE_UNICODESTRING,
            _TDH_OUT_TYPE.TDH_OUTTYPE_STRING,
            nameof(WppEventRecord.FormattedString), false, false, 0, null);
        RawSystemTimeMetadata = new PropertyMetadata(_TDH_IN_TYPE.TDH_INTYPE_FILETIME,
            _TDH_OUT_TYPE.TDH_OUTTYPE_DATETIME,
            nameof(WppEventRecord.RawSystemTime), false, false, 0, null);
        ProviderGuidMetadata = new PropertyMetadata(_TDH_IN_TYPE.TDH_INTYPE_GUID, _TDH_OUT_TYPE.TDH_OUTTYPE_GUID,
            nameof(WppEventRecord.ProviderGuid), false, false, 0, null);

        EventMetadata = new EventMetadata(
            PInvoke.EventTraceGuid,
            1, // TODO: correct me
            0,
            "WPP",
            new[]
            {
                VersionMetadata, TraceGuidMetadata, GuidNameMetadata, GuidTypeNameMetadata, ThreadIdMetadata,
                SystemTimeMetadata, UserTimeMetadata, KernelTimeMetadata, SequenceNumMetadata, ProcessIdMetadata,
                CpuNumberMetadata, IndentMetadata, FlagsNameMetadata, LevelNameMetadata, FunctionNameMetadata,
                ComponentNameMetadata, SubComponentNameMetadata, FormattedStringMetadata, RawSystemTimeMetadata,
                ProviderGuidMetadata
            }
        );
    }

    public WppTraceEventParser(DecodingContext decodingContext)
    {
        ArgumentNullException.ThrowIfNull(decodingContext);
        _decodingContext = decodingContext;
    }

    public unsafe void Parse<T>(EventRecordReader reader, T writer, EventMetadata[] metadataArray,
        RuntimeEventMetadata runtimeMetadata) where T : IEtwWriter
    {
        // we cannot use EventRecordReader for this since the properties are not user data
        WppEventRecord decodedRecord = new(reader.NativeEventRecord);
        // this does the heavy lifting of retrieving properties with the decoding context 
        decodedRecord.Decode(_decodingContext);

        writer.WriteEventBegin(EventMetadata, runtimeMetadata);

        writer.WritePropertyBegin(VersionMetadata);
        writer.WriteUInt32(decodedRecord.Version);
        writer.WritePropertyEnd();

        writer.WritePropertyBegin(TraceGuidMetadata);
        writer.WriteGuid(decodedRecord.TraceGuid);
        writer.WritePropertyEnd();

        writer.WritePropertyBegin(GuidNameMetadata);
        writer.WriteUnicodeString(decodedRecord.GuidName);
        writer.WritePropertyEnd();

        writer.WritePropertyBegin(GuidTypeNameMetadata);
        writer.WriteUnicodeString(decodedRecord.GuidTypeName);
        writer.WritePropertyEnd();

        writer.WritePropertyBegin(ThreadIdMetadata);
        writer.WriteUInt32(decodedRecord.ThreadId);
        writer.WritePropertyEnd();

        writer.WritePropertyBegin(SystemTimeMetadata);
        PInvoke.SystemTimeToFileTime(decodedRecord.SystemTime, out FILETIME systemTimeAsFile);
        long liSystemTime = ((long)systemTimeAsFile.dwHighDateTime << 32) |
                            (uint)systemTimeAsFile.dwLowDateTime;
        // TODO: bugged, fix me!
        writer.WriteFileTime(DateTime.FromFileTimeUtc(liSystemTime));
        writer.WritePropertyEnd();

        writer.WritePropertyBegin(UserTimeMetadata);
        writer.WriteUInt32(decodedRecord.UserTime);
        writer.WritePropertyEnd();

        writer.WritePropertyBegin(KernelTimeMetadata);
        writer.WriteUInt32(decodedRecord.KernelTime);
        writer.WritePropertyEnd();

        writer.WritePropertyBegin(SequenceNumMetadata);
        writer.WriteUInt32(decodedRecord.SequenceNum);
        writer.WritePropertyEnd();

        writer.WritePropertyBegin(ProcessIdMetadata);
        writer.WriteUInt32(decodedRecord.ProcessId);
        writer.WritePropertyEnd();

        writer.WritePropertyBegin(CpuNumberMetadata);
        writer.WriteUInt32(decodedRecord.CpuNumber);
        writer.WritePropertyEnd();

        writer.WritePropertyBegin(IndentMetadata);
        writer.WriteUInt32(decodedRecord.Indent);
        writer.WritePropertyEnd();

        writer.WritePropertyBegin(FlagsNameMetadata);
        writer.WriteUnicodeString(decodedRecord.FlagsName);
        writer.WritePropertyEnd();

        writer.WritePropertyBegin(LevelNameMetadata);
        writer.WriteUnicodeString(decodedRecord.LevelName);
        writer.WritePropertyEnd();

        writer.WritePropertyBegin(FunctionNameMetadata);
        writer.WriteUnicodeString(decodedRecord.FunctionName);
        writer.WritePropertyEnd();

        writer.WritePropertyBegin(ComponentNameMetadata);
        writer.WriteUnicodeString(decodedRecord.ComponentName);
        writer.WritePropertyEnd();

        writer.WritePropertyBegin(SubComponentNameMetadata);
        writer.WriteUnicodeString(decodedRecord.SubComponentName);
        writer.WritePropertyEnd();

        writer.WritePropertyBegin(FormattedStringMetadata);
        writer.WriteUnicodeString(decodedRecord.FormattedString);
        writer.WritePropertyEnd();

        writer.WritePropertyBegin(RawSystemTimeMetadata);
        long liRawSystemTime = ((long)decodedRecord.RawSystemTime.dwHighDateTime << 32) |
                               (uint)decodedRecord.RawSystemTime.dwLowDateTime;
        // TODO: bugged, fix me!
        writer.WriteFileTime(DateTime.FromFileTimeUtc(liRawSystemTime));
        writer.WritePropertyEnd();

        writer.WritePropertyBegin(ProviderGuidMetadata);
        writer.WriteGuid(decodedRecord.ProviderGuid);
        writer.WritePropertyEnd();

        writer.WriteEventEnd();
    }
}