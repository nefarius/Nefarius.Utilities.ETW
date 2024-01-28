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

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    internal enum MAP_VALUETYPE
    {
        EVENTMAP_ENTRY_VALUETYPE_ULONG,
        EVENTMAP_ENTRY_VALUETYPE_STRING
    }

    [StructLayout(LayoutKind.Explicit)]
    internal unsafe struct EVENT_MAP_INFO
    {
        [FieldOffset(0)]
        public ULONG NameOffset;

        [FieldOffset(4)]
        public MAP_FLAGS Flag;

        [FieldOffset(8)]
        public ULONG EntryCount;

        [FieldOffset(12)]
        public MAP_VALUETYPE MapEntryValueType;

        [FieldOffset(12)]
        public ULONG FormatStringOffset;

        [FieldOffset(16)]
        public EVENT_MAP_ENTRY* MapEntryArray;
    }

    /// <summary>
    ///     The following table lists values defined in the Winmeta.xml file.
    /// </summary>
    /// <remarks>https://learn.microsoft.com/en-us/windows/win32/wes/eventmanifestschema-inputtype-complextype#remarks</remarks>
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public enum TDH_IN_TYPE
    {
        TDH_INTYPE_NULL,

        /// <summary>
        ///     A string of 16-bit characters. By default, assumed to have been encoded using UTF-16LE.
        /// </summary>
        TDH_INTYPE_UNICODESTRING,

        /// <summary>
        ///     A string of 8-bit characters. By default or when used with the xs:string output type, the string is assumed to have
        ///     been encoded using the event provider s ANSI code page. When used with the win:Xml, win:Json, or win:Utf8 output
        ///     types, the string is assumed to have been encoded using UTF-8.
        /// </summary>
        TDH_INTYPE_ANSISTRING,

        /// <summary>
        ///     A signed 8-bit integer. When used with the xs:string output type, this will be treated as a character.
        /// </summary>
        TDH_INTYPE_INT8,

        /// <summary>
        ///     An unsigned 8-bit integer. When used with the xs:string output type, this will be treated as a character.
        /// </summary>
        TDH_INTYPE_UINT8,

        /// <summary>
        ///     A signed 16-bit integer.
        /// </summary>
        TDH_INTYPE_INT16,

        /// <summary>
        ///     An unsigned 16-bit integer. When used with the win:Port output type, the data is treated as big-endian (network
        ///     byte order). When used with the xs:string output type, this will be treated as a character.
        /// </summary>
        TDH_INTYPE_UINT16,

        /// <summary>
        ///     A signed 32-bit integer.
        /// </summary>
        TDH_INTYPE_INT32,

        /// <summary>
        ///     An unsigned 32-bit integer.
        /// </summary>
        TDH_INTYPE_UINT32,

        /// <summary>
        ///     A signed 64-bit integer.
        /// </summary>
        TDH_INTYPE_INT64,

        /// <summary>
        ///     An unsigned 64-bit integer.
        /// </summary>
        TDH_INTYPE_UINT64,

        /// <summary>
        ///     An IEEE 4-byte floating-point number.
        /// </summary>
        TDH_INTYPE_FLOAT,

        /// <summary>
        ///     An IEEE 8-byte floating-point number.
        /// </summary>
        TDH_INTYPE_DOUBLE,

        /// <summary>
        ///     A 32-bit value where 0 is false and 1 is true.
        /// </summary>
        TDH_INTYPE_BOOLEAN,

        /// <summary>
        ///     Binary data of variable size. The size must be specified in the data definition as a constant or a reference to
        ///     another (integer) data item.For an IP V6 address, the data should be an IN6_ADDR structure. For a socket address,
        ///     the data should be a SOCKADDR_STORAGE structure. The AF_INET, AF_INET6, and AF_LINK address families are supported.
        ///     Starting with mc.exe version 10.0.14251 or later, binary data can use output type win:Pkcs7WithTypeInfo. This data
        ///     should be a PKCS#7 message (e.g. encrypted and/or signed data). The PKCS#7 message may optionally be followed by
        ///     TraceLogging type information that indicates the type of the inner content. If present, the TraceLogging type
        ///     information should immediately follow the PKCS#7 message (i.e. the type information is not included within the
        ///     PKCS#7 content). To specify the input type of the inner content, append one byte with a value from the TlgIn_t
        ///     enumeration (defined in TraceLoggingProvider.h). To specify the input and output types of the inner content, append
        ///     one byte with a value from the TlgIn_t enumeration and with the byte s high bit set, and append a second byte with
        ///     a value from the TlgOut_t enumeration.
        /// </summary>
        TDH_INTYPE_BINARY,

        /// <summary>
        ///     A GUID structure. On output, the GUID is rendered in the registry string form,
        ///     {xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}.
        /// </summary>
        TDH_INTYPE_GUID,

        /// <summary>
        ///     An unsigned 32-bit or 64-bit pointer value. The size depends on the architecture of the computer logging the event.
        /// </summary>
        TDH_INTYPE_POINTER,

        /// <summary>
        ///     A FILETIME structure, 8-bytes.
        /// </summary>
        TDH_INTYPE_FILETIME,

        /// <summary>
        ///     A SYSTEMTIME structure, 16-bytes.
        /// </summary>
        TDH_INTYPE_SYSTEMTIME,

        /// <summary>
        ///     A security identifier (SID) structure that uniquely identifies a user or group. On output, the SID is rendered in
        ///     string form using the ConvertSidToStringSid function.
        /// </summary>
        TDH_INTYPE_SID,

        /// <summary>
        ///     A hexadecimal representation of an unsigned 32-bit integer
        /// </summary>
        TDH_INTYPE_HEXINT32,

        /// <summary>
        ///     A hexadecimal representation of an unsigned 64-bit integer.
        /// </summary>
        TDH_INTYPE_HEXINT64, // End of winmeta intypes.
        TDH_INTYPE_COUNTEDSTRING = 300, // Start of TDH intypes for WBEM.
        TDH_INTYPE_COUNTEDANSISTRING,
        TDH_INTYPE_REVERSEDCOUNTEDSTRING,
        TDH_INTYPE_REVERSEDCOUNTEDANSISTRING,
        TDH_INTYPE_NONNULLTERMINATEDSTRING,
        TDH_INTYPE_NONNULLTERMINATEDANSISTRING,
        TDH_INTYPE_UNICODECHAR,
        TDH_INTYPE_ANSICHAR,
        TDH_INTYPE_SIZET,
        TDH_INTYPE_HEXDUMP,
        TDH_INTYPE_WBEMSID
    }

    /// <summary>
    ///     The following lists the recognized output types that you can specify in your manifest.
    /// </summary>
    /// <remarks>https://learn.microsoft.com/en-us/windows/win32/wes/eventmanifestschema-outputtype-complextype#remarks</remarks>
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public enum TDH_OUT_TYPE
    {
        TDH_OUTTYPE_NULL,

        /// <summary>
        ///     Text data. This type is valid for the UnicodeString and AnsiString input types. Starting with the mc.exe shipped
        ///     with the Windows Server 2016 SDK or later (mc.exe version 10.0.14251 or later), this type is also valid for the
        ///     Int8, UInt8, and UInt16 input types, in which case it the data is interpreted as a single character.
        /// </summary>
        TDH_OUTTYPE_STRING,

        /// <summary>
        ///     An XML date/time. This is the default format for all dates. The date is formatted using the cultural markers
        ///     embedded in the string (for example, Left-to-Right or Right-to-Left). For information on formatting dates and
        ///     times, see Retrieving Time and Date Information on MSDN. This type is valid for the FILETIME and SYSTEMTIME input
        ///     types. Prior to the version of the MC compiler that ships with the Windows 7 version of the Windows SDK: The date
        ///     is not rendered using the cultural markers embedded in the string (for example, Left-to-Right or Right-to-Left).
        /// </summary>
        TDH_OUTTYPE_DATETIME,

        /// <summary>
        ///     A signed 8-bit integer that is formatted as a decimal integer.
        /// </summary>
        TDH_OUTTYPE_BYTE,

        /// <summary>
        ///     An unsigned 8-bit integer that is formatted as a decimal integer.
        /// </summary>
        TDH_OUTTYPE_UNSIGNEDBYTE,

        /// <summary>
        ///     A signed 16-bit integer that is formatted as a decimal integer.
        /// </summary>
        TDH_OUTTYPE_SHORT,

        /// <summary>
        ///     An unsigned 16-bit integer that is formatted as a decimal integer.
        /// </summary>
        TDH_OUTTYPE_UNSIGNEDSHORT,

        /// <summary>
        ///     A signed 32-bit integer that is formatted as a decimal integer.
        /// </summary>
        TDH_OUTTYPE_INT,

        /// <summary>
        ///     An unsigned 32-bit integer that is formatted as a decimal integer.
        /// </summary>
        TDH_OUTTYPE_UNSIGNEDINT,

        /// <summary>
        ///     A signed 64-bit integer that is formatted as a decimal integer.
        /// </summary>
        TDH_OUTTYPE_LONG,

        /// <summary>
        ///     An unsigned 64-bit integer that is formatted as a decimal integer.
        /// </summary>
        TDH_OUTTYPE_UNSIGNEDLONG,

        /// <summary>
        ///     A 4-byte floating-point number.
        /// </summary>
        TDH_OUTTYPE_FLOAT,

        /// <summary>
        ///     An 8-byte floating-point number.
        /// </summary>
        TDH_OUTTYPE_DOUBLE,

        /// <summary>
        ///     A Boolean value. This type is valid for the Boolean input type, indicating a 32-bit Boolean value corresponding to
        ///     the Win32 BOOL type. Starting with the mc.exe shipped with the Windows Server 2016 SDK or later (mc.exe version
        ///     10.0.14251 or later), this type is also valid for the UInt8 input type, indicating an 8-bit Boolean value
        ///     corresponding to the C++ bool and Win32 BOOLEAN types.
        /// </summary>
        TDH_OUTTYPE_BOOLEAN,

        /// <summary>
        ///     A GUID value that is formatted in the registry string form, {xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}.
        /// </summary>
        TDH_OUTTYPE_GUID,

        /// <summary>
        ///     A sequence of hexadecimal digits. Each byte of the formatted data is padded with leading zeros.
        /// </summary>
        TDH_OUTTYPE_HEXBINARY,

        /// <summary>
        ///     A hexadecimal number that is preceded by "0x". The formatted value is not padded with leading zeros.
        /// </summary>
        TDH_OUTTYPE_HEXINT8,

        /// <summary>
        ///     A hexadecimal number that is preceded by "0x". The formatted value is not padded with leading zeros.
        /// </summary>
        TDH_OUTTYPE_HEXINT16,

        /// <summary>
        ///     A hexadecimal number that is preceded by "0x". The formatted value is not padded with leading zeros.
        /// </summary>
        TDH_OUTTYPE_HEXINT32,

        /// <summary>
        ///     A hexadecimal number that is preceded by "0x". The formatted value is not padded with leading zeros.
        /// </summary>
        TDH_OUTTYPE_HEXINT64,

        /// <summary>
        ///     A signed 32-bit integer that represents a process ID. The value is formatted as a decimal integer.
        /// </summary>
        TDH_OUTTYPE_PID,

        /// <summary>
        ///     A signed 32-bit integer that represents a thread ID. The value is formatted as a decimal integer.
        /// </summary>
        TDH_OUTTYPE_TID,

        /// <summary>
        ///     A signed 16-bit integer that represents an IP address port. Pass the value to the <c>ntohs</c> function and format the
        ///     result as a decimal integer.
        /// </summary>
        TDH_OUTTYPE_PORT,

        /// <summary>
        ///     An IPv4 IP address. This type is valid for the UInt32 input type. The value must be in network byte order; each
        ///     byte of the UInt32 represents one of the four parts of the IP address (p1.p2.p3.p4). The low-order byte contains
        ///     the value for p1, the next byte contains the value for p2, and so on. The address is formatted in dot notation. To
        ///     convert an unsigned integer that contains an IPv4 address to a string, call the RtlIpv4AddressToString or inet_ntoa
        ///     function.
        /// </summary>
        TDH_OUTTYPE_IPV4,

        /// <summary>
        ///     An IPv6 IP address. This type is valid for the win:Binary input type. The address is formatted as a string. To
        ///     format the address, call the RtlIpv6AddressToString function.
        /// </summary>
        TDH_OUTTYPE_IPV6,

        /// <summary>
        ///     A socket address that is interpreted as a SOCKADDR_STORAGE structure. The address family determines how the address
        ///     is formatted. For the AF_INET and AF_INET6 families, the address is formatted as &lt;IP_Address&gt;:&lt;Port&gt;
        ///     for all other families the address is formatted as a hex dump. For AF_INET and AF_INET6, the event data is a
        ///     128-bit binary value. For AF_LINK, the event data is a 112-bit binary value.
        /// </summary>
        TDH_OUTTYPE_SOCKETADDRESS,

        /// <summary>
        ///     Represents the CIM date/time. For specifying a time stamp or an interval. If it specifies a time stamp, it
        ///     preserves the time zone offset. Not supported.
        /// </summary>
        TDH_OUTTYPE_CIMDATETIME,

        /// <summary>
        ///     A time stamp in 100 nanosecond units that is the relative time from the beginning of the trace to when the event is
        ///     written. The time stamp is rendered as a decimal integer. This type is valid for the UInt32 or UInt64 input type.
        /// </summary>
        TDH_OUTTYPE_ETWTIME,

        /// <summary>
        ///     An XML document or document fragment. This type is valid for the UnicodeString and AnsiString input types. When
        ///     decoded on a system running Windows Server 2016 or later, when used with the AnsiString input type, the string will
        ///     be treated as UTF-8 unless the XML document starts with a processing instruction specifying an alternate encoding.
        /// </summary>
        TDH_OUTTYPE_XML,

        /// <summary>
        ///     An error code. This type is valid for the UInt32 input type. The code is rendered as a hexadecimal number that is
        ///     preceded by "0x". Do not use, instead use the more specific error code types, such as Win32Error or HResult.
        /// </summary>
        TDH_OUTTYPE_ERRORCODE,

        /// <summary>
        ///     A Win32 error code. This type is valid for the UInt32 input type. The service retrieves and renders the message
        ///     string associated with the Win32 error code if it exists; otherwise, the service renders a string in the form,
        ///     "Unknown Win32 Error code: 0x" with the Win32 error code appended as hexadecimal number.
        /// </summary>
        TDH_OUTTYPE_WIN32ERROR,

        /// <summary>
        ///     An NTSTATUS error code. This type is valid for the UInt32 input type. The service retrieves and renders the message
        ///     string associated with the NT status code if it exists; otherwise, the service renders a string in the form,
        ///     "Unknown NTSTATUS Error code: 0x" with the NT status code appended as hexadecimal number.
        /// </summary>
        TDH_OUTTYPE_NTSTATUS,

        /// <summary>
        ///     An HRESULT error code. This type is valid for the Int32 input type. The service retrieves and renders the message
        ///     string associated with the HRESULT error code if it exists; otherwise, the service renders a string in the form,
        ///     "Unknown HResult Error code: 0x" with the HRESULT error code appended as hexadecimal number.
        /// </summary>
        TDH_OUTTYPE_HRESULT, // End of winmeta outtypes.

        /// <summary>
        ///     An XML date/time. This type is valid for the FILETIME and SYSTEMTIME input types. The date is not rendered using
        ///     the cultural markers embedded in the string (for example, Left-to-Right or Right-to-Left). For information on
        ///     formatting dates and times, see Retrieving Time and Date Information on MSDN.Prior to MC version 1.12.7051 and
        ///     Windows 7: Not available
        /// </summary>
        TDH_OUTTYPE_CULTURE_INSENSITIVE_DATETIME, // Culture neutral datetime string.

        /// <summary>
        ///     A JSON string. This type is valid for the UnicodeString and AnsiString input types. When used with the AnsiString
        ///     input type, the string will be treated as UTF-8.
        /// </summary>
        TDH_OUTTYPE_JSON,

        /// <summary>
        ///     A UTF-8 string. This type is valid for the AnsiString input type. When this output type is used, the string will be
        ///     treated as UTF-8.
        /// </summary>
        TDH_OUTTYPE_UTF8,
        TDH_OUTTYPE_REDUCEDSTRING = 300, // Start of TDH outtypes for WBEM.
        TDH_OUTTYPE_NOPRINT
    }

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

    [StructLayout(LayoutKind.Explicit)]
    internal struct EVENT_PROPERTY_INFO
    {
        [FieldOffset(0)]
        public PROPERTY_FLAGS Flags;

        [FieldOffset(4)]
        public ULONG NameOffset;

        [FieldOffset(8)]
        public nonStructType NonStructType;

        [FieldOffset(8)]
        public structType StructType;

        [FieldOffset(16)]
        public USHORT count;

        [FieldOffset(16)]
        public USHORT countPropertyIndex;

        [FieldOffset(18)]
        public USHORT length;

        [FieldOffset(18)]
        public USHORT lengthPropertyIndex;

        [FieldOffset(18)]
        public ULONG Reserved;

        [StructLayout(LayoutKind.Sequential)]
        internal struct nonStructType
        {
            public USHORT InType;
            public USHORT OutType;
            public ULONG MapNameOffset;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct structType
        {
            public USHORT StructStartIndex;
            public USHORT NumOfStructMembers;
            public ULONG padding;
        }
    }

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    internal enum DECODING_SOURCE
    {
        DecodingSourceXMLFile,
        DecodingSourceWbem,
        DecodingSourceWPP,
        DecodingSourceTlg,
        DecodingSourceMax
    }

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    internal enum TEMPLATE_FLAGS
    {
        TEMPLATE_EVENT_DATA = 1, // Used when custom xml is not specified.
        TEMPLATE_USER_DATA = 2 // Used when custom xml is specified.
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct TRACE_EVENT_INFO
    {
        public GUID ProviderGuid;
        public GUID EventGuid;
        public USHORT Id;
        public UCHAR Version;
        public UCHAR Channel;
        public UCHAR Level;
        public UCHAR Opcode;
        public USHORT Task;
        public ULONGLONG Keyword;
        public DECODING_SOURCE DecodingSource;
        public ULONG ProviderNameOffset;
        public ULONG LevelNameOffset;
        public ULONG ChannelNameOffset;
        public ULONG KeywordsNameOffset;
        public ULONG TaskNameOffset;
        public ULONG OpcodeNameOffset;
        public ULONG EventMessageOffset;
        public ULONG ProviderMessageOffset;
        public ULONG BinaryXMLOffset;
        public ULONG BinaryXMLSize;
        public ULONG ActivityIDNameOffset;
        public ULONG RelatedActivityIDNameOffset;
        public ULONG PropertyCount;
        public ULONG TopLevelPropertyCount;
        public TEMPLATE_FLAGS Flags;
        public EVENT_PROPERTY_INFO* EventPropertyInfoArray;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PROPERTY_DATA_DESCRIPTOR
    {
        public ULONGLONG PropertyName; // Pointer to property name.
        public ULONG ArrayIndex; // Array Index.
        public ULONG Reserved;
    }

    internal static class Tdh
    {
        [DllImport("tdh.dll", EntryPoint = "TdhGetEventInformation")]
        internal static extern unsafe int GetEventInformation(EVENT_RECORD* pEvent, uint TdhContextCount, IntPtr pTdhContext, byte* pBuffer, out uint pBufferSize);

        [DllImport("tdh.dll", EntryPoint = "TdhGetEventMapInformation", CharSet = CharSet.Unicode)]
        internal static extern unsafe int GetEventMapInformation(EVENT_RECORD* pEvent, string pMapName, byte* pBuffer, out uint pBufferSize);
    }
}