using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

using Windows.Win32.Foundation;

namespace Nefarius.Utilities.ETW.Deserializer.WPP;

internal readonly unsafe struct WppEventRecord
{
    private readonly EVENT_RECORD* _record;
    private readonly DecodingContext _decodingContext;

    public uint Version { get; init; }
    public Guid TraceGuid { get; init; }
    public string GuidName { get; init; }
    public string GuidTypeName { get; init; }
    public uint ThreadId { get; init; }
    public SYSTEMTIME SystemTime { get; init; }
    public uint UserTime { get; init; }
    public uint KernelTime { get; init; }
    public uint SequenceNum { get; init; }
    public uint ProcessId { get; init; }
    public uint CpuNumber { get; init; }
    public uint Indent { get; init; }
    public string FlagsName { get; init; }
    public string LevelName { get; init; }
    public string FunctionName { get; init; }
    public string ComponentName { get; init; }
    public string SubComponentName { get; init; }
    public string FormattedString { get; init; }
    public FILETIME RawSystemTime { get; init; }
    public Guid ProviderGuid { get; init; }

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

                    if (value is not null)
                    {
                        SetPropertyByName(this, propertyName, value);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
        }
    }

    private static void SetPropertyByName(object obj, string propertyName, object value)
    {
        ArgumentNullException.ThrowIfNull(obj);

        if (string.IsNullOrEmpty(propertyName))
        {
            throw new ArgumentNullException(nameof(propertyName));
        }

        PropertyInfo? propertyInfo =
            obj.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (propertyInfo == null)
        {
            throw new ArgumentException($"Property '{propertyName}' not found on type '{obj.GetType().Name}'.");
        }

        if (!propertyInfo.CanWrite)
        {
            throw new InvalidOperationException($"Property '{propertyName}' is read-only.");
        }

        object convertedValue = Convert.ChangeType(value, propertyInfo.PropertyType);
        propertyInfo.SetValue(obj, convertedValue);
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