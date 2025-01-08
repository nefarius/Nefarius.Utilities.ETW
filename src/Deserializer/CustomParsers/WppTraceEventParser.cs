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
    private static readonly PropertyMetadata CpuiNumberMetadata;
    private static readonly PropertyMetadata IndentMetadata;
    private static readonly PropertyMetadata FlagsNameMetadata;
    private static readonly PropertyMetadata LevelNameMetadata;
    private static readonly PropertyMetadata FunctionNameMetadata;
    private static readonly PropertyMetadata ComponentNameMetadata;
    private static readonly PropertyMetadata SubComponentNameMetadata;
    private static readonly PropertyMetadata FormattedStringMetadata;
    private static readonly PropertyMetadata RawSystemTimeMetadata;
    private static readonly PropertyMetadata ProviderGuidMetadata;


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
        CpuiNumberMetadata = new PropertyMetadata(_TDH_IN_TYPE.TDH_INTYPE_UINT32, _TDH_OUT_TYPE.TDH_OUTTYPE_UNSIGNEDINT,
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
            32, // TODO: correct me
            0, // TODO: correct me
            "WPP",
            new[]
            {
                VersionMetadata, TraceGuidMetadata, GuidNameMetadata, GuidTypeNameMetadata, ThreadIdMetadata,
                SystemTimeMetadata, UserTimeMetadata, KernelTimeMetadata, SequenceNumMetadata, ProcessIdMetadata,
                CpuiNumberMetadata, IndentMetadata, FlagsNameMetadata, LevelNameMetadata, FunctionNameMetadata,
                ComponentNameMetadata, SubComponentNameMetadata, FormattedStringMetadata, RawSystemTimeMetadata,
                ProviderGuidMetadata
            }
        );
    }

    public void Parse<T>(EventRecordReader reader, T writer, EventMetadata[] metadataArray,
        RuntimeEventMetadata runtimeMetadata) where T : IEtwWriter
    {
        throw new NotImplementedException();
    }
}