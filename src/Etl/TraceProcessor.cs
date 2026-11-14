using System.Runtime.InteropServices;

using Windows.Win32.Foundation;

namespace Nefarius.Utilities.ETW.Etl;

/// <summary>
///     Why a trace file could not be opened.
/// </summary>
internal readonly record struct TraceOpenFailure(int Index, WIN32_ERROR Error, string Message);

/// <summary>
///     Runs ETL files through either the native ETW APIs or the managed reader, delivering
///     <see cref="EVENT_RECORD" />s to one callback.
/// </summary>
internal static unsafe class TraceProcessor
{
    public static EtwBackend Resolve(EtwBackend requested)
    {
        switch (requested)
        {
            case EtwBackend.Auto:
                return OperatingSystem.IsWindows() ? EtwBackend.Native : EtwBackend.Managed;
            case EtwBackend.Native when !OperatingSystem.IsWindows():
                throw new PlatformNotSupportedException(
                    "The native ETW backend is only available on Windows. Use EtwBackend.Managed or EtwBackend.Auto.");
            default:
                return requested;
        }
    }

    /// <summary>
    ///     Processes all files and returns <see langword="null" /> on success.
    /// </summary>
    /// <exception cref="EtlFormatException">The managed reader found a malformed file after opening it.</exception>
    public static TraceOpenFailure? ProcessFiles(
        IReadOnlyList<string> files,
        EtwBackend backend,
        bool preserveRawTimestamps,
        CancellationToken cancellationToken,
        Action<string>? reportWarning,
        EtlRecordCallback callback)
    {
        if (Resolve(backend) == EtwBackend.Managed)
        {
            (int Index, EtlFormatException Error)? failure = EtlReader.Process(files, preserveRawTimestamps, callback,
                reportWarning, cancellationToken);
            return failure is { } f ? new TraceOpenFailure(f.Index, f.Error.Error, f.Error.Message) : null;
        }

        return ProcessNative(files, preserveRawTimestamps, cancellationToken, callback);
    }

    private static TraceOpenFailure? ProcessNative(IReadOnlyList<string> files, bool preserveRaw,
        CancellationToken cancellationToken, EtlRecordCallback callback)
    {
        int count = files.Count;
        EVENT_TRACE_LOGFILEW[] sessions = new EVENT_TRACE_LOGFILEW[count];
        ulong[] handles = new ulong[count];
        int[] errors = new int[count];

        PEVENT_RECORD_CALLBACK native = record => callback(record, default);

        try
        {
            for (int i = 0; i < count; ++i)
            {
                sessions[i] = new EVENT_TRACE_LOGFILEW
                {
                    LogFileName = files[i],
                    EventRecordCallback = native,
                    BufferCallback = _ => !cancellationToken.IsCancellationRequested,
                    LogFileMode = PInvoke.PROCESS_TRACE_MODE_EVENT_RECORD
                };

                if (preserveRaw)
                {
                    sessions[i].LogFileMode |= PInvoke.PROCESS_TRACE_MODE_RAW_TIMESTAMP;
                }

                handles[i] = Etw.OpenTrace(ref sessions[i]);
                // Capture immediately: any later Win32 call overwrites the thread's last error.
                errors[i] = Marshal.GetLastWin32Error();
            }

            for (int i = 0; i < count; ++i)
            {
                if (handles[i] != unchecked((ulong)~0))
                {
                    continue;
                }

                WIN32_ERROR error = (WIN32_ERROR)errors[i];
                return new TraceOpenFailure(i, error, DescribeNativeOpenError(files[i], error));
            }

            Etw.ProcessTrace(handles, (uint)handles.Length, IntPtr.Zero, IntPtr.Zero);
            GC.KeepAlive(sessions);
            return null;
        }
        finally
        {
            for (int i = 0; i < count; ++i)
            {
                if (handles[i] != 0 && handles[i] != unchecked((ulong)~0))
                {
                    Etw.CloseTrace(handles[i]);
                }
            }
        }
    }

    internal static string DescribeNativeOpenError(string file, WIN32_ERROR error) =>
        error switch
        {
            WIN32_ERROR.ERROR_INVALID_PARAMETER =>
                $"ERROR: For file: {file} Windows returned 0x57 -- The Logfile parameter is NULL.",
            WIN32_ERROR.ERROR_BAD_PATHNAME =>
                $"ERROR: For file: {file} Windows returned 0xA1 -- The specified path is invalid.",
            WIN32_ERROR.ERROR_ACCESS_DENIED =>
                $"ERROR: For file: {file} Windows returned 0x5 -- Access is denied.",
            _ => $"ERROR: For file: {file} Windows returned an unknown error: 0x{(uint)error:X8} ({error})."
        };
}
