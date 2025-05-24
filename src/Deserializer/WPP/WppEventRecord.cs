using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

using Windows.Win32.Foundation;

using FastMember;

using Nefarius.Utilities.ETW.Exceptions;

namespace Nefarius.Utilities.ETW.Deserializer.WPP;

/// <summary>
///     Represents a single WPP event tracing event.
/// </summary>
[SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Local")]
[SuppressMessage("ReSharper", "UnusedMember.Global")]
internal unsafe class WppEventRecord
{
    private readonly EVENT_RECORD* _eventRecord;

#pragma warning disable CS8618, CS9264
    public WppEventRecord(EVENT_RECORD* eventRecord)
#pragma warning restore CS8618, CS9264
    {
        _eventRecord = eventRecord;
    }

    private ushort GuidTypeNameFormatId => _eventRecord->EventHeader.EventDescriptor.Id;

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
            _eventRecord,
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
            _eventRecord,
            0,
            null,
            traceEventInfo,
            &bufferSize
        );

        if (infoRet != WIN32_ERROR.ERROR_SUCCESS)
        {
            throw new TdhGetEventInformationException(infoRet);
        }

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
                _eventRecord,
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
                        _eventRecord,
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

                    if (value is not null)
                    {
                        // set managed property by name
                        self[propertyName] = value;
                    }

                    continue;
                }
                finally
                {
                    Marshal.FreeHGlobal(wppPropBuffer);
                }
            }

            // these properties can be fetched with the way faster API
            byte* primitivePropertyBuffer = (byte*)Marshal.AllocHGlobal((int)propSize);

            try
            {
                WIN32_ERROR getPrimPropRet = (WIN32_ERROR)PInvoke.TdhGetProperty(
                    _eventRecord,
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

                switch (propertyType)
                {
                    case _TDH_IN_TYPE.TDH_INTYPE_UINT32:
                        value = Marshal.PtrToStructure((IntPtr)primitivePropertyBuffer, typeof(uint));
                        break;
                    case _TDH_IN_TYPE.TDH_INTYPE_GUID:
                        value = Marshal.PtrToStructure((IntPtr)primitivePropertyBuffer, typeof(Guid));
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
}