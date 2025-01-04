using Windows.Win32.System.Diagnostics.Etw;

namespace Nefarius.Utilities.ETW.Deserializer.CustomParsers
{
    internal sealed class KernelTraceControlDbgIdParser : ICustomParser
    {
        private static readonly EventMetadata EventMetadata;

        private static readonly PropertyMetadata ImageBase;

        private static readonly PropertyMetadata ProcessId;

        private static readonly PropertyMetadata GuidSig;

        private static readonly PropertyMetadata Age;

        private static readonly PropertyMetadata PdbFileName;

        static KernelTraceControlDbgIdParser()
        {
            ImageBase = new PropertyMetadata(_TDH_IN_TYPE.TDH_INTYPE_INT64, _TDH_OUT_TYPE.TDH_OUTTYPE_HEXINT64, "ImageBase", false, false, 0, null);
            ProcessId = new PropertyMetadata(_TDH_IN_TYPE.TDH_INTYPE_UINT32, _TDH_OUT_TYPE.TDH_OUTTYPE_UNSIGNEDINT, "ProcessID", false, false, 0, null);
            GuidSig = new PropertyMetadata(_TDH_IN_TYPE.TDH_INTYPE_GUID, _TDH_OUT_TYPE.TDH_OUTTYPE_GUID, "GuidSig", false, false, 0, null);
            Age = new PropertyMetadata(_TDH_IN_TYPE.TDH_INTYPE_UINT32, _TDH_OUT_TYPE.TDH_OUTTYPE_UNSIGNEDINT, "Age", false, false, 0, null);
            PdbFileName = new PropertyMetadata(_TDH_IN_TYPE.TDH_INTYPE_UNICODESTRING, _TDH_OUT_TYPE.TDH_OUTTYPE_STRING, "PdbFileName", false, false, 0, null);
            EventMetadata = new EventMetadata(
                new Guid("b3e675d7-2554-4f18-830b-2762732560de"),
                36,
                0,
                "KernelTraceControl/ImageID/DbgID_RSDS",
                new[] { ImageBase, ProcessId, GuidSig, Age, PdbFileName });
        }

        public void Parse<T>(EventRecordReader reader, T writer, EventMetadata[] metadataArray, RuntimeEventMetadata runtimeMetadata)
            where T : IEtwWriter
        {
            writer.WriteEventBegin(EventMetadata, runtimeMetadata);

            writer.WritePropertyBegin(ImageBase);
            writer.WriteUInt64(reader.ReadUInt64());
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
}