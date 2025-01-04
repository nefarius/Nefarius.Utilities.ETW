using Windows.Win32.System.Diagnostics.Etw;

namespace Nefarius.Utilities.ETW.Deserializer.CustomParsers
{
    internal sealed class KernelTraceControlImageIdFileVersionParser : ICustomParser
    {
        private static readonly EventMetadata EventMetadata;

        private static readonly PropertyMetadata ImageSize;

        private static readonly PropertyMetadata TimeDateStamp;

        private static readonly PropertyMetadata OrigFileName;

        private static readonly PropertyMetadata FileDescription;

        private static readonly PropertyMetadata FileVersion;

        private static readonly PropertyMetadata BinFileVersion;

        private static readonly PropertyMetadata VerLanguage;

        private static readonly PropertyMetadata ProductName;

        private static readonly PropertyMetadata CompanyName;

        private static readonly PropertyMetadata ProductVersion;

        private static readonly PropertyMetadata FileId;

        private static readonly PropertyMetadata ProgramId;

        static KernelTraceControlImageIdFileVersionParser()
        {
            ImageSize = new PropertyMetadata(_TDH_IN_TYPE.TDH_INTYPE_INT32, _TDH_OUT_TYPE.TDH_OUTTYPE_UNSIGNEDINT, "ImageSize", false, false, 0, null);
            TimeDateStamp = new PropertyMetadata(_TDH_IN_TYPE.TDH_INTYPE_INT32, _TDH_OUT_TYPE.TDH_OUTTYPE_UNSIGNEDINT, "TimeDateStamp", false, false, 0, null);
            OrigFileName = new PropertyMetadata(_TDH_IN_TYPE.TDH_INTYPE_UNICODESTRING, _TDH_OUT_TYPE.TDH_OUTTYPE_STRING, "OrigFileName", false, false, 0, null);
            FileDescription = new PropertyMetadata(_TDH_IN_TYPE.TDH_INTYPE_UNICODESTRING, _TDH_OUT_TYPE.TDH_OUTTYPE_STRING, "FileDescription", false, false, 0, null);
            FileVersion = new PropertyMetadata(_TDH_IN_TYPE.TDH_INTYPE_UNICODESTRING, _TDH_OUT_TYPE.TDH_OUTTYPE_STRING, "FileVersion", false, false, 0, null);
            BinFileVersion = new PropertyMetadata(_TDH_IN_TYPE.TDH_INTYPE_UNICODESTRING, _TDH_OUT_TYPE.TDH_OUTTYPE_STRING, "BinFileVersion", false, false, 0, null);
            VerLanguage = new PropertyMetadata(_TDH_IN_TYPE.TDH_INTYPE_UNICODESTRING, _TDH_OUT_TYPE.TDH_OUTTYPE_STRING, "VerLanguage", false, false, 0, null);
            ProductName = new PropertyMetadata(_TDH_IN_TYPE.TDH_INTYPE_UNICODESTRING, _TDH_OUT_TYPE.TDH_OUTTYPE_STRING, "ProductName", false, false, 0, null);
            CompanyName = new PropertyMetadata(_TDH_IN_TYPE.TDH_INTYPE_UNICODESTRING, _TDH_OUT_TYPE.TDH_OUTTYPE_STRING, "CompanyName", false, false, 0, null);
            ProductVersion = new PropertyMetadata(_TDH_IN_TYPE.TDH_INTYPE_UNICODESTRING, _TDH_OUT_TYPE.TDH_OUTTYPE_STRING, "ProductVersion", false, false, 0, null);
            FileId = new PropertyMetadata(_TDH_IN_TYPE.TDH_INTYPE_UNICODESTRING, _TDH_OUT_TYPE.TDH_OUTTYPE_STRING, "FileId", false, false, 0, null);
            ProgramId = new PropertyMetadata(_TDH_IN_TYPE.TDH_INTYPE_UNICODESTRING, _TDH_OUT_TYPE.TDH_OUTTYPE_STRING, "ProgramId", false, false, 0, null);
            EventMetadata = new EventMetadata(
                new Guid("b3e675d7-2554-4f18-830b-2762732560de"),
                64,
                0,
                "KernelTraceControl/ImageID/FileVersion",
                new[] { ImageSize, TimeDateStamp, OrigFileName, FileDescription, FileVersion, BinFileVersion, VerLanguage, ProductName, CompanyName, ProductVersion, FileId, ProgramId });
        }

        public void Parse<T>(EventRecordReader reader, T writer, EventMetadata[] metadataArray, RuntimeEventMetadata runtimeMetadata)
            where T : IEtwWriter
        {
            writer.WriteEventBegin(EventMetadata, runtimeMetadata);

            writer.WritePropertyBegin(ImageSize);
            writer.WriteUInt64(reader.ReadUInt32());
            writer.WritePropertyEnd();

            writer.WritePropertyBegin(TimeDateStamp);
            writer.WriteUInt64(reader.ReadUInt32());
            writer.WritePropertyEnd();

            writer.WritePropertyBegin(OrigFileName);
            writer.WriteUnicodeString(reader.ReadUnicodeString());
            writer.WritePropertyEnd();

            writer.WritePropertyBegin(FileDescription);
            writer.WriteUnicodeString(reader.ReadUnicodeString());
            writer.WritePropertyEnd();

            writer.WritePropertyBegin(FileVersion);
            writer.WriteUnicodeString(reader.ReadUnicodeString());
            writer.WritePropertyEnd();

            writer.WritePropertyBegin(BinFileVersion);
            writer.WriteUnicodeString(reader.ReadUnicodeString());
            writer.WritePropertyEnd();

            writer.WritePropertyBegin(VerLanguage);
            writer.WriteUnicodeString(reader.ReadUnicodeString());
            writer.WritePropertyEnd();

            writer.WritePropertyBegin(ProductName);
            writer.WriteUnicodeString(reader.ReadUnicodeString());
            writer.WritePropertyEnd();

            writer.WritePropertyBegin(CompanyName);
            writer.WriteUnicodeString(reader.ReadUnicodeString());
            writer.WritePropertyEnd();

            writer.WritePropertyBegin(ProductVersion);
            writer.WriteUnicodeString(reader.ReadUnicodeString());
            writer.WritePropertyEnd();

            writer.WritePropertyBegin(FileId);
            writer.WriteUnicodeString(reader.ReadUnicodeString());
            writer.WritePropertyEnd();

            writer.WritePropertyBegin(ProgramId);
            writer.WriteUnicodeString(reader.ReadUnicodeString());
            writer.WritePropertyEnd();

            writer.WriteEventEnd();
        }
    }
}