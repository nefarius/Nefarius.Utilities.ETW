using System.Runtime.InteropServices;

namespace Nefarius.Utilities.ETW.Deserializer
{
    using ULONG = System.UInt32;
    using ULONG64 = System.UInt64;
    using ULONGLONG = System.UInt64;
    using LARGE_INTEGER = System.Int64;
    using USHORT = System.UInt16;
    using GUID = System.Guid;
    using UCHAR = System.Byte;

    [StructLayout(LayoutKind.Sequential)]
    public struct EVENT_HEADER_EXTENDED_DATA_ITEM
    {
        public USHORT Reserved1;
        public USHORT ExtType;
        public USHORT Reserved2;
        public USHORT DataSize;
        public ULONGLONG DataPtr;
    }

    internal static class Etw
    {
        internal const USHORT EVENT_HEADER_EXT_TYPE_RELATED_ACTIVITYID = 0x0001;
        internal const USHORT EVENT_HEADER_EXT_TYPE_SID = 0x0002;
        internal const USHORT EVENT_HEADER_EXT_TYPE_TS_ID = 0x0003;
        internal const USHORT EVENT_HEADER_EXT_TYPE_INSTANCE_INFO = 0x0004;
        internal const USHORT EVENT_HEADER_EXT_TYPE_STACK_TRACE32 = 0x0005;
        internal const USHORT EVENT_HEADER_EXT_TYPE_STACK_TRACE64 = 0x0006;
        internal const USHORT EVENT_HEADER_EXT_TYPE_PEBS_INDEX = 0x0007;
        internal const USHORT EVENT_HEADER_EXT_TYPE_PMC_COUNTERS = 0x0008;

        internal const USHORT EVENT_HEADER_FLAG_STRING_ONLY = 0x0004;
        internal const USHORT EVENT_HEADER_FLAG_TRACE_MESSAGE = 0x0008;
        internal const USHORT EVENT_HEADER_FLAG_32_BIT_HEADER = 0x0020;
        internal const USHORT EVENT_HEADER_FLAG_64_BIT_HEADER = 0x0040;
        internal const USHORT EVENT_HEADER_FLAG_CLASSIC_HEADER = 0x0100;
    }
}