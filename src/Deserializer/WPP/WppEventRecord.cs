using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

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
internal unsafe partial class WppEventRecord(EventRecordReader eventRecordReader)
{
    /// <summary>
    ///     Equivalent to <see cref="TraceMessageFormat.Id" />.
    /// </summary>
    private ushort GuidTypeNameFormatId => eventRecordReader.NativeEventRecord->EventHeader.EventDescriptor.Id;

    private string SubstituteFunctionParameters(TraceMessageFormat format)
    {
        Dictionary<int, (FunctionParameter Parameter, object Value)> indexedParameterValues = [];

        foreach (FunctionParameter parameter in format.FunctionParameters)
        {
            object value = ItemReader.Readers.TryGetValue(parameter.Type, out Func<EventRecordReader, object>? reader)
                ? reader(eventRecordReader)
                : throw new NotImplementedException($"Type of item {parameter.Type} not implemented.");

            indexedParameterValues.Add(parameter.Index, (parameter, value));
        }

        // Always go through WppFormatter.Substitute so that context markers (%!FUNC!, %!LEVEL!, etc.)
        // and the %0 STDPREFIX sentinel are resolved even when there are no positional parameters.
        return WppFormatter.Substitute(format, indexedParameterValues);
    }

    private string? ReadUnicodeStringProperty(PROPERTY_DATA_DESCRIPTOR* descriptor, uint size)
    {
        byte* propertyBuffer = (byte*)Marshal.AllocHGlobal((int)size);

        try
        {
            WIN32_ERROR ret = (WIN32_ERROR)PInvoke.TdhGetProperty(
                eventRecordReader.NativeEventRecord,
                0,
                null,
                1,
                descriptor,
                size,
                propertyBuffer
            );

            if (ret != WIN32_ERROR.ERROR_SUCCESS)
            {
                throw new TdhGetPropertyException(ret);
            }

            return Marshal.PtrToStringUni((IntPtr)propertyBuffer);
        }
        finally
        {
            Marshal.FreeHGlobal((IntPtr)propertyBuffer);
        }
    }

    /// <summary>
    ///     Decodes well-known WPP properties from a given <see cref="EVENT_RECORD" />.
    /// </summary>
    /// <param name="decodingContext">The <see cref="DecodingContext" /> to use.</param>
    /// <exception cref="TdhGetEventInformationException">Failed to get event information.</exception>
    /// <exception cref="TdhGetPropertySizeException">Failed to query property size.</exception>
    /// <exception cref="TdhGetPropertyException">Failed to get property content.</exception>
    public void Decode(DecodingContext decodingContext)
    {
        ObjectAccessor? self = ObjectAccessor.Create(this, true);

        uint bufferSize = 0;
        WIN32_ERROR infoRet = (WIN32_ERROR)PInvoke.TdhGetEventInformation(
            eventRecordReader.NativeEventRecord,
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
            eventRecordReader.NativeEventRecord,
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
                eventRecordReader.NativeEventRecord,
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

            // we need to decode those using available TMF objects
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
                        nameof(FormattedString) => SubstituteFunctionParameters(format),
                        // TODO: what even is this one?
                        // Not listed here: https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/trace-message-prefix
                        // I guess it is %!STDPREFIX!
                        // more info: https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/how-do-i-add-a-prefix-and-suffix-to-a-trace-message-#configuration-block-syntax
                        //.Replace("%0 ", string.Empty)
                        // TODO: can there be more than these?
                        // see: https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/what-are-the-wpp-extended-format-specification-strings-#software-tracing
                        //.Replace("%!FUNC!", format.Function),
                        nameof(ComponentName) => ReadUnicodeStringProperty(&propertyDescriptor, propSize),
                        nameof(SubComponentName) => ReadUnicodeStringProperty(&propertyDescriptor, propSize),
                        _ => throw new NotImplementedException($"Unknown property \"{propertyName}\" encountered.")
                    };
                }
                // fallback value to inform the caller that we couldn't decode 
                else if (propertyName.Equals(nameof(FormattedString)))
                {
                    value = new StringBuilder().Append("GUID=")
                        .Append(TraceGuid.ToString().ToUpperInvariant())
                        .Append(", ID=")
                        .Append(GuidTypeNameFormatId)
                        .Append(", Version=")
                        .Append(Version)
                        .Append(" - No format information found.")
                        .ToString();
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
                    eventRecordReader.NativeEventRecord,
                    0,
                    null,
                    1,
                    &propertyDescriptor,
                    propSize,
                    primitivePropertyBuffer
                );

                if (getPrimPropRet != WIN32_ERROR.ERROR_SUCCESS)
                {
                    throw new TdhGetPropertyException(getPrimPropRet);
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
                    default:
                        throw new NotImplementedException($"Property type {propertyType} not implemented.");
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