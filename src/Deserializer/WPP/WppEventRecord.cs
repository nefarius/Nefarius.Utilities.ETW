using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text.RegularExpressions;

using Windows.Win32.Foundation;

using FastMember;

using Nefarius.Utilities.ETW.Deserializer.WPP.TMF;
using Nefarius.Utilities.ETW.Exceptions;

namespace Nefarius.Utilities.ETW.Deserializer.WPP;

/// <summary>
///     Represents a single WPP event tracing event.
/// </summary>
[SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Local")]
[SuppressMessage("ReSharper", "UnusedMember.Global")]
internal unsafe partial class WppEventRecord
{
    private readonly EventRecordReader _eventRecordReader;

    public WppEventRecord(EventRecordReader eventRecordReader)
    {
        _eventRecordReader = eventRecordReader;
    }

    private ushort GuidTypeNameFormatId => _eventRecordReader.NativeEventRecord->EventHeader.EventDescriptor.Id;

    private string BuildFormattedString(TraceMessageFormat format)
    {
        if (!format.FunctionParameters.Any())
        {
            return format.MessageFormat;
        }

        Dictionary<int, FunctionParameterValuePair> indexedParameterValues = [];

        foreach (FunctionParameter parameter in format.FunctionParameters)
        {
            object value = parameter.Type switch
            {
                ItemType.ItemListByte => _eventRecordReader.ReadUInt8(),
                ItemType.ItemLong => _eventRecordReader.ReadInt32(),
                ItemType.ItemLongLong => _eventRecordReader.ReadUInt64(),
                ItemType.ItemLongLongXX => _eventRecordReader.ReadUInt64(),
                ItemType.ItemNTSTATUS => _eventRecordReader.ReadUInt32(),
                ItemType.ItemPWString => _eventRecordReader.ReadCountedString(),
                ItemType.ItemPtr => _eventRecordReader.ReadPointer(),
                ItemType.ItemString => _eventRecordReader.ReadAnsiString(),
                ItemType.ItemGuid => _eventRecordReader.ReadGuid(),
                _ => throw new ArgumentOutOfRangeException()
            };

            indexedParameterValues.Add(parameter.Index, new FunctionParameterValuePair(parameter, value));
        }

        string formatString = format.MessageFormat;

        // substitute the placeholders with the real variable values
        string formatted = PlaceholderRegex().Replace(formatString, match =>
        {
            bool isHexPrefixed = match.Groups[1].Success;
            int index = int.Parse(match.Groups[2].Value);
            string formatSpec = match.Groups[3].Value;

            if (!indexedParameterValues.TryGetValue(index, out FunctionParameterValuePair? pair))
            {
                return match.Value; // Leave as is if missing
            }

            // Apply formatting
            try
            {
                switch (formatSpec)
                {
                    // value results in a string
                    case "s":
                        // handle "enum" values like %irql%
                        if (pair.Parameter is { Type: ItemType.ItemListByte, ListItems: not null })
                        {
                            return pair.Parameter.ListItems[(byte)pair.Value];
                        }

                        // handle NTSTATUS translation
                        if (pair.Parameter.Type == ItemType.ItemNTSTATUS)
                        {
                            uint ntStatus = (uint)pair.Value;
                            string ntStatusLabel = NtStatus.Values[ntStatus];
                            return $"{ntStatusLabel} (0x{ntStatus:X8})";
                        }

                        return pair.Value.ToString() ?? throw new InvalidOperationException("Unexpected null value.");
                    // pointer values
                    case "p":
                        return pair.Value switch
                        {
                            IntPtr ptr => $"0x{ptr:X}",
                            long l => $"0x{l:X}",
                            int i => $"0x{i:X}",
                            _ => $"0x{Convert.ToUInt64(pair.Value):X}"
                        };
                    // complex numerical values 
                    default:
                        {
                            Match numberMatch = NumericFormatTokenRegex().Match(formatSpec);

                            if (!numberMatch.Success)
                            {
                                return string.Format($"{{0:{formatSpec}}}", pair.Value);
                            }

                            string pad = numberMatch.Groups["pad"].Success ? "0" : "";
                            string width = numberMatch.Groups["width"].Value;
                            string specifier = numberMatch.Groups["specifier"].Value.ToUpperInvariant();

                            string formatSuffix = $"{pad}{width}";
                            string finalFormat = isHexPrefixed
                                ? $"0x{{0:{specifier}{formatSuffix}}}"
                                : $"{{0:{specifier}{formatSuffix}}}";
                            string result = string.Format(finalFormat, pair.Value);
                            return result;
                        }
                }
            }
            catch
            {
                return $"<format error for %{index}!{formatSpec}!>";
            }
        });

        return formatted;
    }

    /// <summary>
    ///     Decodes well-known properties from a given <see cref="EVENT_RECORD" />.
    /// </summary>
    /// <param name="decodingContext">The <see cref="DecodingContext" /> to use.</param>
    /// <exception cref="Win32Exception">A TDH API call failed.</exception>
    public void Decode(DecodingContext decodingContext)
    {
        ObjectAccessor? self = ObjectAccessor.Create(this, true);

        uint bufferSize = 0;
        WIN32_ERROR infoRet = (WIN32_ERROR)PInvoke.TdhGetEventInformation(
            _eventRecordReader.NativeEventRecord,
            0,
            null,
            null,
            &bufferSize
        );

        if (infoRet != WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER)
        {
            throw new TdhGetEventInformationException(infoRet);
        }

        byte* infoBuffer = stackalloc byte[(int)bufferSize];
        TRACE_EVENT_INFO* traceEventInfo = (TRACE_EVENT_INFO*)infoBuffer;
        infoRet = (WIN32_ERROR)PInvoke.TdhGetEventInformation(
            _eventRecordReader.NativeEventRecord,
            0,
            null,
            traceEventInfo,
            &bufferSize
        );

        if (infoRet != WIN32_ERROR.ERROR_SUCCESS)
        {
            throw new TdhGetEventInformationException(infoRet);
        }

        // we can look for this once we have the "TraceGuid" property value
        TraceMessageFormat? format = null;

        // we expect 20 WPP properties, but this dynamic approach is safer
        for (int propertyIndex = 0; propertyIndex < traceEventInfo->PropertyCount; propertyIndex++)
        {
            EVENT_PROPERTY_INFO propertyInfo = traceEventInfo->EventPropertyInfoArray[propertyIndex];
            _TDH_IN_TYPE propertyType = (_TDH_IN_TYPE)propertyInfo.Anonymous1.customSchemaType.InType;
            string propertyName = new((char*)((byte*)traceEventInfo + propertyInfo.NameOffset));

            PROPERTY_DATA_DESCRIPTOR propertyDescriptor = new()
            {
                // Pointer to a null-terminated Unicode string that contains the case-sensitive property name.
                // You can use the NameOffset member of the EVENT_PROPERTY_INFO structure to get the property name.
                PropertyName = (ulong)((byte*)traceEventInfo + propertyInfo.NameOffset),
                // Zero-based index for accessing elements of a property array.
                // If the property data is not an array or if you want to address the entire array,
                // specify ULONG_MAX (0xFFFFFFFF).
                ArrayIndex = uint.MaxValue
            };

            uint propSize = 0;
            // fetch the size of properties that do not need decoding 
            WIN32_ERROR sizeRet = (WIN32_ERROR)PInvoke.TdhGetPropertySize(
                _eventRecordReader.NativeEventRecord,
                0,
                null,
                1,
                &propertyDescriptor,
                &propSize
            );

            if (sizeRet != WIN32_ERROR.ERROR_SUCCESS)
            {
                throw new TdhGetPropertySizeException(sizeRet);
            }

            object? value = null;

            // we need to decode those with a WPP-specific API
            if (propertyType == _TDH_IN_TYPE.TDH_INTYPE_UNICODESTRING)
            {
                if (format is not null)
                {
                    value = propertyName switch
                    {
                        nameof(GuidName) => format.Provider,
                        nameof(GuidTypeName) => format.Opcode,
                        nameof(FlagsName) => format.Flags,
                        nameof(LevelName) => format.Level,
                        nameof(FunctionName) => format.Function,
                        nameof(FormattedString) =>
                            BuildFormattedString(format)
                                .Replace("%0 ", string.Empty)
                                // TODO: can there be more than these?
                                .Replace("%!FUNC!", format.Function),
                        _ => value
                    };
                }

                if (value is null)
                {
                    // TODO: propSize doesn't equal the rendered string, maybe there is a better way to do this
                    // this doesn't appear to work: https://github.com/microsoft/ETW/blob/a5ed49d12b9ef2af6545de0b25a76b334caad066/EtwEnumerator/samples/EtwEnumeratorDecode.cpp#L178-L184
                    propSize = 4096;
                    IntPtr wppPropBuffer = Marshal.AllocHGlobal((int)propSize);
                    try
                    {
                        retry:
                        // query property content
                        WIN32_ERROR getWppPropRet = (WIN32_ERROR)PInvoke.TdhGetWppProperty(
                            decodingContext.Handle,
                            _eventRecordReader.NativeEventRecord,
                            (char*)propertyDescriptor.PropertyName,
                            &propSize,
                            (byte*)wppPropBuffer.ToPointer()
                        );

                        // crude but hey, works ;)
                        if (getWppPropRet == WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER)
                        {
                            propSize *= 2;
                            Marshal.ReAllocHGlobal(wppPropBuffer, (IntPtr)propSize);
                            goto retry;
                        }

                        if (getWppPropRet != WIN32_ERROR.ERROR_SUCCESS)
                        {
                            throw new TdhGetWppPropertyException(getWppPropRet);
                        }

                        value = Marshal.PtrToStringUni(wppPropBuffer); // ANSI strings not used in WPP
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(wppPropBuffer);
                    }
                }

                if (value is not null)
                {
                    // set managed property by name
                    self[propertyName] = value;
                }

                continue;
            }

            // these properties can be fetched with the way faster API
            byte* primitivePropertyBuffer = (byte*)Marshal.AllocHGlobal((int)propSize);

            try
            {
                WIN32_ERROR getPrimPropRet = (WIN32_ERROR)PInvoke.TdhGetProperty(
                    _eventRecordReader.NativeEventRecord,
                    0,
                    null,
                    1,
                    &propertyDescriptor,
                    propSize,
                    primitivePropertyBuffer
                );

                if (getPrimPropRet != WIN32_ERROR.ERROR_SUCCESS)
                {
                    throw new TdhGetPropertyException(infoRet);
                }

                // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
                switch (propertyType)
                {
                    case _TDH_IN_TYPE.TDH_INTYPE_UINT32:
                        value = Marshal.PtrToStructure((IntPtr)primitivePropertyBuffer, typeof(uint));
                        break;
                    case _TDH_IN_TYPE.TDH_INTYPE_GUID:
                        value = Marshal.PtrToStructure((IntPtr)primitivePropertyBuffer, typeof(Guid));
                        if (propertyName.Equals(nameof(TraceGuid)))
                        {
                            format = decodingContext.GetTraceMessageFormatFor((Guid?)value, GuidTypeNameFormatId);
                        }

                        break;
                    case _TDH_IN_TYPE.TDH_INTYPE_SYSTEMTIME:
                        value = Marshal.PtrToStructure((IntPtr)primitivePropertyBuffer, typeof(SYSTEMTIME));
                        break;
                    case _TDH_IN_TYPE.TDH_INTYPE_FILETIME:
                        value = Marshal.PtrToStructure((IntPtr)primitivePropertyBuffer, typeof(FILETIME));
                        break;
                    // unformatted or unknown properties would get mangled so this must not be used
                    case _TDH_IN_TYPE.TDH_INTYPE_UNICODESTRING:
                    default:
                        throw new InvalidCastException();
                }

                if (value is not null)
                {
                    // set managed property by name
                    self[propertyName] = value;
                }
            }
            finally
            {
                Marshal.FreeHGlobal((IntPtr)primitivePropertyBuffer);
            }
        }
    }

    [GeneratedRegex(@"(0[xX])?%(\d+)!([^!]*)!", RegexOptions.Compiled)]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex(@"^(?<pad>0)?(?<width>\d+)?(?<modifier>I\d+)?(?<specifier>[Xxdu])$")]
    private static partial Regex NumericFormatTokenRegex();

    private record FunctionParameterValuePair(FunctionParameter Parameter, object Value);

    #region WPP Properties

    public uint Version { get; private set; }
    public Guid TraceGuid { get; private set; }
    public string GuidName { get; private set; }
    public string GuidTypeName { get; private set; }
    public uint ThreadId { get; private set; }
    public SYSTEMTIME SystemTime { get; private set; }
    public uint UserTime { get; private set; }
    public uint KernelTime { get; private set; }
    public uint SequenceNum { get; private set; }
    public uint ProcessId { get; private set; }
    public uint CpuNumber { get; private set; }
    public uint Indent { get; private set; }
    public string FlagsName { get; private set; }
    public string LevelName { get; private set; }
    public string FunctionName { get; private set; }
    public string ComponentName { get; private set; }
    public string SubComponentName { get; private set; }
    public string FormattedString { get; private set; }
    public FILETIME RawSystemTime { get; private set; }
    public Guid ProviderGuid { get; private set; }

    #endregion
}