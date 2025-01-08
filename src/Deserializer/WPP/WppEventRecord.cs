using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

using Windows.Win32.Foundation;

namespace Nefarius.Utilities.ETW.Deserializer.WPP;

internal readonly unsafe struct WppEventRecord
{
    private readonly EVENT_RECORD* _record;
    private readonly DecodingContext _decodingContext;

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

    public WppEventRecord(EVENT_RECORD* eventRecord, DecodingContext decodingContext)
    {
        _record = eventRecord;
        _decodingContext = decodingContext;

        foreach ((string propertyName, Type propertyType) in WellKnownWppProperties)
        {
            int typeSize = Marshal.SizeOf(propertyType);

            fixed (char* propertyNameBuf = propertyName)
            {
                uint size = 0;
                uint ret = PInvoke.TdhGetWppProperty(decodingContext.Handle, eventRecord, propertyNameBuf, &size, null);

                if (size > typeSize)
                {
                    throw new InvalidOperationException("Property size mismatch!");
                }

                IntPtr buffer = Marshal.AllocHGlobal((int)size);
                try
                {
                    ret = PInvoke.TdhGetWppProperty(decodingContext.Handle, eventRecord, propertyNameBuf, &size,
                        (byte*)buffer.ToPointer());


                    object? value = propertyType == typeof(string)
                        ? Marshal.PtrToStringUni(buffer)
                        : Marshal.PtrToStructure(buffer, propertyType);
                    
                    
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
        }
    }

    public void Decode()
    {
        uint size = 0;
        uint ret = PInvoke.TdhGetWppMessage(_decodingContext.Handle, _record, &size, null);

        byte* messageBuffer = stackalloc byte[(int)size];

        ret = PInvoke.TdhGetWppMessage(_decodingContext.Handle, _record, &size, messageBuffer);

        string message = new((char*)messageBuffer);
    }
}