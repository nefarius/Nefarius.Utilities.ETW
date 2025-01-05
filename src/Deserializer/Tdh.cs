using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
// ReSharper disable InconsistentNaming
// ReSharper disable RedundantNameQualifier

namespace Nefarius.Utilities.ETW.Deserializer
{
    using ULONG = System.UInt32;
    using ULONGLONG = System.UInt64;
    using USHORT = System.UInt16;
    using GUID = System.Guid;
    using UCHAR = System.Byte;

    [StructLayout(LayoutKind.Explicit)]
    internal struct EVENT_MAP_ENTRY
    {
        [FieldOffset(0)]
        public ULONG OutputOffset;

        [FieldOffset(4)]
        public ULONG Value; // For ULONG value (valuemap and bitmap).

        [FieldOffset(4)]
        public ULONG InputOffset; // For String value (patternmap or valuemap in WBEM).
    }

    [Flags]
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    internal enum MAP_FLAGS
    {
        EVENTMAP_INFO_FLAG_MANIFEST_VALUEMAP = 0x1,
        EVENTMAP_INFO_FLAG_MANIFEST_BITMAP = 0x2,
        EVENTMAP_INFO_FLAG_MANIFEST_PATTERNMAP = 0x4,
        EVENTMAP_INFO_FLAG_WBEM_VALUEMAP = 0x8,
        EVENTMAP_INFO_FLAG_WBEM_BITMAP = 0x10,
        EVENTMAP_INFO_FLAG_WBEM_FLAG = 0x20,
        EVENTMAP_INFO_FLAG_WBEM_NO_MAP = 0x40
    }

    /*
    [Flags]
    internal enum PROPERTY_FLAGS
    {
        PropertyStruct = 0x1, // Type is struct.
        PropertyParamLength = 0x2, // Length field is index of param with length.
        PropertyParamCount = 0x4, // Count file is index of param with count.
        PropertyWBEMXmlFragment = 0x8, // WBEM extension flag for property.
        PropertyParamFixedLength = 0x10, // Length of the parameter is fixed.
        PropertyParamFixedCount = 0x20, // Count of the parameter is fixed.
        PropertyHasTags = 0x40 // The Tags field has been initialized.
    }
    */

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    internal enum TEMPLATE_FLAGS
    {
        TEMPLATE_EVENT_DATA = 1, // Used when custom xml is not specified.
        TEMPLATE_USER_DATA = 2 // Used when custom xml is specified.
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PROPERTY_DATA_DESCRIPTOR
    {
        public ULONGLONG PropertyName; // Pointer to property name.
        public ULONG ArrayIndex; // Array Index.
        public ULONG Reserved;
    }
}