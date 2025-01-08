using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

using Windows.Win32.Foundation;

using FastMember;

namespace Nefarius.Utilities.ETW.Deserializer.WPP;

[SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Local")]
[SuppressMessage("ReSharper", "UnusedMember.Global")]
internal unsafe class WppEventRecord
{
    /// <summary>
    ///     TDH supports a set of known properties for WPP events.
    /// </summary>
    /// <remarks>Source: https://learn.microsoft.com/en-us/windows/win32/etw/using-tdhformatproperty-to-consume-event-data</remarks>
    private static readonly IReadOnlyDictionary<string, Type> WellKnownWppProperties = new Dictionary<string, Type>
    {
        { "Version", typeof(uint) },
        { "TraceGuid", typeof(Guid) },
        { "GuidName", typeof(string) },
        { "GuidTypeName", typeof(string) },
        { "ThreadId", typeof(uint) },
        { "SystemTime", typeof(SYSTEMTIME) },
        { "UserTime", typeof(uint) },
        { "KernelTime", typeof(uint) },
        { "SequenceNum", typeof(uint) },
        { "ProcessId", typeof(uint) },
        { "CpuNumber", typeof(uint) },
        { "Indent", typeof(uint) },
        { "FlagsName", typeof(string) },
        { "LevelName", typeof(string) },
        { "FunctionName", typeof(string) },
        { "ComponentName", typeof(string) },
        { "SubComponentName", typeof(string) },
        { "FormattedString", typeof(string) },
        { "RawSystemTime", typeof(FILETIME) },
        { "ProviderGuid", typeof(Guid) }
    };

#pragma warning disable CS8618, CS9264
    public WppEventRecord(EVENT_RECORD* eventRecord, DecodingContext decodingContext)
#pragma warning restore CS8618, CS9264
    {
        ObjectAccessor? wrapped = ObjectAccessor.Create(this, true);
        
        foreach ((string propertyName, Type propertyType) in WellKnownWppProperties)
        {
            int typeSize = propertyType != typeof(string)
                ? Marshal.SizeOf(propertyType)
                : -1;

            fixed (char* propertyNameBuf = propertyName)
            {
                uint size = 0;
#pragma warning disable CA1416
                WIN32_ERROR ret = (WIN32_ERROR)PInvoke.TdhGetWppProperty(decodingContext.Handle, eventRecord,
                    propertyNameBuf, &size, null);
#pragma warning restore CA1416

                if (ret != WIN32_ERROR.ERROR_SUCCESS)
                {
                    throw new Win32Exception((int)ret);
                }

                if (typeSize != -1 && size > typeSize)
                {
                    throw new InvalidOperationException("Property size mismatch!");
                }

                IntPtr buffer = Marshal.AllocHGlobal((int)size);
                try
                {
#pragma warning disable CA1416
                    ret = (WIN32_ERROR)PInvoke.TdhGetWppProperty(decodingContext.Handle, eventRecord, propertyNameBuf,
                        &size,
                        (byte*)buffer.ToPointer());
#pragma warning restore CA1416

                    if (ret != WIN32_ERROR.ERROR_SUCCESS)
                    {
                        throw new Win32Exception((int)ret);
                    }

                    object? value = propertyType == typeof(string)
                        ? Marshal.PtrToStringUni(buffer)
                        : Marshal.PtrToStructure(buffer, propertyType);

                    if (value is not null)
                    {
                        wrapped[propertyName] = value;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
        }
    }

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
}