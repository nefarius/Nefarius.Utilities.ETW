using System.Buffers;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Channels;

using Windows.Win32.Foundation;

using Nefarius.Utilities.ETW.Deserializer;
using Nefarius.Utilities.ETW.Deserializer.WPP;
using Nefarius.Utilities.ETW.Exceptions;

namespace Nefarius.Utilities.ETW;

/// <summary>
///     ETW utility class.
/// </summary>
[SuppressMessage("ReSharper", "UnusedMember.Global")]
public static class EtwUtil
{
    /// <summary>
    ///     Performs a lightweight pre-scan of one or more <c>.ETL</c> files and returns the deduplicated set of
    ///     <see cref="PdbMetaData" /> entries discovered in the trace.
    /// </summary>
    /// <param name="inputFiles">
    ///     One or more input <c>.etl</c> files to scan. Must not be <see langword="null" /> and must not
    ///     contain any <see langword="null" /> or whitespace-only entries.
    /// </param>
    /// <param name="options">
    ///     Optional callback to configure scan behaviour. Use it to subscribe to
    ///     <see cref="EtwMetadataScanOptions.OnDbgIdRsds" /> (<c>KernelTraceControl/ImageID/DbgID_RSDS</c>),
    ///     <see cref="EtwMetadataScanOptions.OnKernelDbgIdRsds" /> (<c>MSNT_SystemTrace/EventTrace/DbgIdRSDS</c>),
    ///     <see cref="EtwMetadataScanOptions.OnImageId" /> (<c>KernelTraceControl/ImageID</c>), and
    ///     <see cref="EtwMetadataScanOptions.OnImageIdFileVersion" /> (<c>KernelTraceControl/ImageID/FileVersion</c>)
    ///     event notifications, or to provide a <see cref="EtwMetadataScanOptions.ReportError" /> handler.
    /// </param>
    /// <returns>
    ///     A deduplicated, read-only collection of every <see cref="PdbMetaData" /> referenced by the trace.
    ///     The caller should use these entries to locate or download the corresponding <c>.pdb</c> files (or find
    ///     matching <c>.tmf</c> files), then pass the resulting <see cref="Deserializer.WPP.DecodingContext" /> to
    ///     <see cref="ConvertToJson" /> via <see cref="EtwJsonConverterOptions.WppDecodingContext" />.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="inputFiles" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">
    ///     One or more entries in <paramref name="inputFiles" /> are <see langword="null" />, empty, or consist only of
    ///     whitespace.
    /// </exception>
    public static IReadOnlyCollection<PdbMetaData> EnumeratePdbReferences(IEnumerable<string> inputFiles,
        Action<EtwMetadataScanOptions>? options = null)
    {
        ArgumentNullException.ThrowIfNull(inputFiles);

        List<string> list = inputFiles.ToList();
        for (int i = 0; i < list.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(list[i]))
            {
                throw new ArgumentException($"Entry at index {i} is null, empty, or whitespace.", nameof(inputFiles));
            }
        }

        EtwMetadataScanOptions opts = new();
        options?.Invoke(opts);

        MetadataScanner scanner = new(opts);
        return scanner.Scan(list);
    }

    /// <summary>
    ///     Converts one or more .ETL files to a JSON object.
    /// </summary>
    /// <param name="jsonWriter">The target JSON writer to write to.</param>
    /// <param name="inputFiles">One or more input files.</param>
    /// <param name="options">Options to further tweak the parsing operation.</param>
    /// <returns>True on success, false otherwise.</returns>
    public static bool ConvertToJson(Utf8JsonWriter jsonWriter, IEnumerable<string> inputFiles,
        Action<EtwJsonConverterOptions>? options = null)
    {
        EtwJsonConverterOptions opts = new();

        options?.Invoke(opts);

        List<string> list = inputFiles.ToList();
        Deserializer<EtwJsonWriter> deserializer = new(
            new EtwJsonWriter(jsonWriter),
            opts.CustomProviderManifest,
            opts.WppDecodingContext,
            opts.OnWppFormatMissing,
            opts.RewriteWppProviderName
        );

        int count = list.Count;
        EVENT_TRACE_LOGFILEW[] fileSessions = new EVENT_TRACE_LOGFILEW[count];
        ulong[] handles = new ulong[count];

        for (int i = 0; i < count; ++i)
        {
            unsafe
            {
                fileSessions[i] = new EVENT_TRACE_LOGFILEW
                {
                    LogFileName = list[i],
                    EventRecordCallback = deserializer.Deserialize,
                    BufferCallback = deserializer.BufferCallback,
                    LogFileMode = PInvoke.PROCESS_TRACE_MODE_EVENT_RECORD
                };

                if (opts.PreserveRawTimestamps)
                {
                    fileSessions[i].LogFileMode |= PInvoke.PROCESS_TRACE_MODE_RAW_TIMESTAMP;
                }

                handles[i] = Etw.OpenTrace(ref fileSessions[i]);
            }
        }

        for (int i = 0; i < handles.Length; ++i)
        {
            unchecked
            {
                if (handles[i] == (ulong)~0)
                {
                    switch ((WIN32_ERROR)Marshal.GetLastWin32Error())
                    {
                        case WIN32_ERROR.ERROR_INVALID_PARAMETER:
                            opts.ReportError?.Invoke("ERROR: For file: " + list[i] +
                                                     " Windows returned 0x57 -- The Logfile parameter is NULL.");
                            return false;
                        case WIN32_ERROR.ERROR_BAD_PATHNAME:
                            opts.ReportError?.Invoke("ERROR: For file: " + list[i] +
                                                     " Windows returned 0xA1 -- The specified path is invalid.");
                            return false;
                        case WIN32_ERROR.ERROR_ACCESS_DENIED:
                            opts.ReportError?.Invoke("ERROR: For file: " + list[i] +
                                                     " Windows returned 0x5 -- Access is denied.");
                            return false;
                        default:
                            opts.ReportError?.Invoke("ERROR: For file: " + list[i] +
                                                     " Windows returned an unknown error.");
                            return false;
                    }
                }
            }
        }

        jsonWriter.WriteStartObject();
        jsonWriter.WritePropertyName("Events");

        jsonWriter.WriteStartArray();
        Etw.ProcessTrace(handles, (uint)handles.Length, IntPtr.Zero, IntPtr.Zero);
        jsonWriter.WriteEndArray();

        jsonWriter.WriteEndObject();

        GC.KeepAlive(fileSessions);

        for (int i = 0; i < count; ++i)
        {
            Etw.CloseTrace(handles[i]);
        }

        jsonWriter.Flush();
        return true;
    }

    /// <summary>
    ///     Converts a live real-time ETW session to a stream of JSON objects written to
    ///     <paramref name="jsonWriter" />, blocking until the session ends or
    ///     <paramref name="cancellationToken" /> is cancelled.
    /// </summary>
    /// <param name="jsonWriter">The target JSON writer to write to.</param>
    /// <param name="sessionName">
    ///     The name of an already-running real-time ETW session (e.g., created via
    ///     <see cref="EtwRealtimeSession.Create" /> or <c>logman start</c>).
    /// </param>
    /// <param name="options">Options to further tweak the parsing operation.</param>
    /// <param name="cancellationToken">
    ///     Token that, when cancelled, stops trace processing and returns from this method.
    /// </param>
    /// <returns>
    ///     <see langword="true" /> when the session ended normally or was cancelled;
    ///     <see langword="false" /> if the session could not be opened.
    /// </returns>
    /// <remarks>
    ///     WPP decoding in real-time mode requires a pre-built
    ///     <see cref="EtwJsonConverterOptions.WppDecodingContext" /> supplied via
    ///     <paramref name="options" /> — the file-based
    ///     <see cref="EnumeratePdbReferences" /> pre-scan cannot be applied to live sessions.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="jsonWriter" /> or <paramref name="sessionName" /> is <see langword="null" />.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     <paramref name="sessionName" /> is empty or whitespace.
    /// </exception>
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public static bool ConvertRealtimeToJson(
        Utf8JsonWriter jsonWriter,
        string sessionName,
        Action<EtwJsonConverterOptions>? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jsonWriter);
        ArgumentNullException.ThrowIfNull(sessionName);
        if (string.IsNullOrWhiteSpace(sessionName))
        {
            throw new ArgumentException("Session name must not be empty or whitespace.", nameof(sessionName));
        }

        EtwJsonConverterOptions opts = new();
        options?.Invoke(opts);

        Deserializer<EtwJsonWriter> deserializer = new(
            new EtwJsonWriter(jsonWriter),
            opts.CustomProviderManifest,
            opts.WppDecodingContext,
            opts.OnWppFormatMissing,
            opts.RewriteWppProviderName
        );

        EVENT_TRACE_LOGFILEW session;
        ulong handle;

        unsafe
        {
            session = new EVENT_TRACE_LOGFILEW
            {
                LoggerName = sessionName,
                EventRecordCallback = deserializer.Deserialize,
                BufferCallback = _ => !cancellationToken.IsCancellationRequested,
                LogFileMode = PInvoke.PROCESS_TRACE_MODE_EVENT_RECORD | PInvoke.PROCESS_TRACE_MODE_REAL_TIME
            };

            if (opts.PreserveRawTimestamps)
            {
                session.LogFileMode |= PInvoke.PROCESS_TRACE_MODE_RAW_TIMESTAMP;
            }

            handle = Etw.OpenTrace(ref session);
        }

        unchecked
        {
            if (handle == (ulong)~0)
            {
                WIN32_ERROR error = (WIN32_ERROR)Marshal.GetLastWin32Error();
                opts.ReportError?.Invoke(
                    $"ERROR: Session '{sessionName}': OpenTrace failed with {error} (0x{(uint)error:X8}).");
                return false;
            }
        }

        // Atomically closed flag prevents double-close between the cancellation
        // callback and the finally block below.
        int handleClosed = 0;

        void CloseHandleSafe()
        {
            if (Interlocked.Exchange(ref handleClosed, 1) == 0)
            {
                Etw.CloseTrace(handle);
            }
        }

        using CancellationTokenRegistration reg = cancellationToken.Register(CloseHandleSafe);

        try
        {
            jsonWriter.WriteStartObject();
            jsonWriter.WritePropertyName("Events");
            jsonWriter.WriteStartArray();

            Etw.ProcessTrace([handle], 1, IntPtr.Zero, IntPtr.Zero);

            jsonWriter.WriteEndArray();
            jsonWriter.WriteEndObject();

            GC.KeepAlive(session);
        }
        finally
        {
            CloseHandleSafe();
        }

        jsonWriter.Flush();
        return true;
    }

    /// <summary>
    ///     Converts one or more .ETL files to a stream of UTF-8 JSON objects, yielding each decoded event
    ///     as a <see cref="ReadOnlyMemory{T}">ReadOnlyMemory&lt;byte&gt;</see> as it is produced.
    /// </summary>
    /// <param name="inputFiles">One or more input files.</param>
    /// <param name="options">Options to further tweak the parsing operation.</param>
    /// <param name="cancellationToken">Token that, when cancelled, stops trace processing and completes the enumeration.</param>
    /// <returns>
    ///     An <see cref="IAsyncEnumerable{T}" /> of raw UTF-8 JSON buffers, one per event. Each buffer contains a
    ///     self-contained JSON object — <c>{"Event":{"Timestamp":…,"Properties":[{…}]}}</c> — with no outer
    ///     array wrapper. The caller may concatenate or wrap the items as needed.
    /// </returns>
    /// <remarks>
    ///     <para>
    ///         Each <see cref="ReadOnlyMemory{T}">ReadOnlyMemory&lt;byte&gt;</see> is backed by a buffer rented from
    ///         <see cref="ArrayPool{T}.Shared" />. The buffer is valid only until the next iteration step; the library
    ///         returns it to the pool when <c>MoveNextAsync</c> is called (i.e. at the start of the next
    ///         <see langword="await foreach" /> loop body). Do not retain references to the memory across iterations.
    ///     </para>
    ///     <para>
    ///         Trace processing runs on a dedicated background thread so the thread-pool is not blocked by the
    ///         long-running native <c>ProcessTrace</c> call. A bounded channel with a fixed capacity couples the
    ///         producer to the consumer, applying natural backpressure when the consumer is slower than the trace.
    ///     </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="inputFiles" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">
    ///     One or more entries in <paramref name="inputFiles" /> are <see langword="null" />, empty, or consist only of
    ///     whitespace.
    /// </exception>
    /// <exception cref="EtwOpenTraceException">
    ///     One of the input files could not be opened by the ETW API.
    /// </exception>
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public static IAsyncEnumerable<ReadOnlyMemory<byte>> EnumerateEventsAsync(
        IEnumerable<string> inputFiles,
        Action<EtwJsonConverterOptions>? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputFiles);

        List<string> list = inputFiles.ToList();
        for (int i = 0; i < list.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(list[i]))
            {
                throw new ArgumentException($"Entry at index {i} is null, empty, or whitespace.", nameof(inputFiles));
            }
        }

        EtwJsonConverterOptions opts = new();
        options?.Invoke(opts);

        // Defer all resource creation (channel, linked CTS, deserializer, worker thread) into
        // the async iterator so that nothing is allocated or started until MoveNextAsync is
        // first called.  If the returned IAsyncEnumerable is never iterated, no resources are
        // consumed and no worker thread is started.
        return StreamEventsAsync(list, realtimeSessionName: null, opts, cancellationToken);
    }

    /// <summary>
    ///     Streams decoded events from a live real-time ETW session as UTF-8 JSON objects,
    ///     yielding each event as a <see cref="ReadOnlyMemory{T}">ReadOnlyMemory&lt;byte&gt;</see>
    ///     as it is produced.
    /// </summary>
    /// <param name="sessionName">
    ///     The name of an already-running real-time ETW session (e.g., created via
    ///     <see cref="EtwRealtimeSession.Create" /> or <c>logman start</c>).
    /// </param>
    /// <param name="options">Options to further tweak the parsing operation.</param>
    /// <param name="cancellationToken">
    ///     Token that, when cancelled, stops trace processing and completes the enumeration.
    /// </param>
    /// <returns>
    ///     An <see cref="IAsyncEnumerable{T}" /> of raw UTF-8 JSON buffers, one per event. Each buffer
    ///     is a self-contained JSON object — <c>{"Event":{"Timestamp":…,"Properties":[{…}]}}</c> —
    ///     with no outer array wrapper. The caller may concatenate or wrap items as needed.
    /// </returns>
    /// <remarks>
    ///     <para>
    ///         Each <see cref="ReadOnlyMemory{T}">ReadOnlyMemory&lt;byte&gt;</see> is backed by a buffer rented from
    ///         <see cref="ArrayPool{T}.Shared" />. The buffer is valid only until the next iteration step; the library
    ///         returns it to the pool when <c>MoveNextAsync</c> is called. Do not retain references to the memory
    ///         across iterations.
    ///     </para>
    ///     <para>
    ///         Trace processing runs on a dedicated background thread. A bounded channel couples the producer to the
    ///         consumer, applying natural backpressure. When cancelled, the trace handle is closed from the
    ///         cancellation callback, causing <c>ProcessTrace</c> to return promptly rather than waiting for the
    ///         next flush-timer tick.
    ///     </para>
    ///     <para>
    ///         WPP decoding in real-time mode requires a pre-built
    ///         <see cref="EtwJsonConverterOptions.WppDecodingContext" /> supplied via
    ///         <paramref name="options" /> — the file-based <see cref="EnumeratePdbReferences" /> pre-scan cannot be
    ///         applied to live sessions.
    ///     </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="sessionName" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException"><paramref name="sessionName" /> is empty or whitespace.</exception>
    /// <exception cref="EtwOpenTraceException">The session could not be opened by the ETW API.</exception>
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public static IAsyncEnumerable<ReadOnlyMemory<byte>> EnumerateRealtimeEventsAsync(
        string sessionName,
        Action<EtwJsonConverterOptions>? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionName);
        if (string.IsNullOrWhiteSpace(sessionName))
        {
            throw new ArgumentException("Session name must not be empty or whitespace.", nameof(sessionName));
        }

        EtwJsonConverterOptions opts = new();
        options?.Invoke(opts);

        return StreamEventsAsync(files: null, realtimeSessionName: sessionName, opts, cancellationToken);
    }

    /// <summary>
    ///     Stops a real-time ETW session by name, ignoring the error if no session with that name exists.
    ///     Use this at application startup to clean up sessions left behind by a previous crash.
    /// </summary>
    /// <param name="sessionName">Name of the orphaned session to stop.</param>
    /// <exception cref="ArgumentNullException"><paramref name="sessionName" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException"><paramref name="sessionName" /> is empty or whitespace.</exception>
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public static void StopOrphanSession(string sessionName)
    {
        ArgumentNullException.ThrowIfNull(sessionName);
        if (string.IsNullOrWhiteSpace(sessionName))
        {
            throw new ArgumentException("Session name must not be empty or whitespace.", nameof(sessionName));
        }

        unsafe
        {
            int propsSize = Marshal.SizeOf<EVENT_TRACE_PROPERTIES>();
            int nameBytes = (sessionName.Length + 1) * sizeof(char);
            int totalSize = propsSize + nameBytes;

            EVENT_TRACE_PROPERTIES* props = (EVENT_TRACE_PROPERTIES*)NativeMemory.AllocZeroed((nuint)totalSize);
            try
            {
                props->Wnode.BufferSize = (uint)totalSize;
                props->LoggerNameOffset = (uint)propsSize;

                char* dest = (char*)((byte*)props + propsSize);
                for (int i = 0; i < sessionName.Length; i++)
                {
                    dest[i] = sessionName[i];
                }

                dest[sessionName.Length] = '\0';

                // Pass default(CONTROLTRACE_HANDLE) — ControlTrace uses the session name when the
                // handle is zero.  Ignore return value: ERROR_WMI_INSTANCE_NOT_FOUND means the
                // session does not exist, which is the expected case for orphan cleanup.
                ref EVENT_TRACE_PROPERTIES propsRef = ref Unsafe.AsRef<EVENT_TRACE_PROPERTIES>(props);
                PInvoke.ControlTrace(
                    default,
                    sessionName,
                    ref propsRef,
                    (EVENT_TRACE_CONTROL)Etw.EVENT_TRACE_CONTROL_STOP);
            }
            finally
            {
                NativeMemory.Free(props);
            }
        }
    }

    /// <summary>
    ///     Returns the names of all currently running ETW trace sessions.
    /// </summary>
    /// <returns>
    ///     A read-only list of session names in the order reported by <c>QueryAllTracesW</c>.
    ///     An empty list means the system has no active trace sessions.
    /// </returns>
    /// <remarks>
    ///     The Windows default maximum is 64 concurrent sessions.  When the registry key
    ///     <c>EtwMaxLoggers</c> raises the limit above 64, this method retries once with
    ///     capacity 256 before giving up.
    /// </remarks>
    /// <exception cref="Win32Exception">
    ///     <c>QueryAllTracesW</c> returned a non-zero, non-<c>ERROR_MORE_DATA</c> error code, or
    ///     the system reports more than 256 concurrent sessions and the buffer cannot be grown further.
    /// </exception>
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public static IReadOnlyList<string> EnumerateSessionNames()
    {
        // ETW session names are limited to 1 024 characters including the null terminator.
        const int MaxNameChars = 1024;

        // Try 64 first (system default); retry with 256 if QueryAllTracesW reports more sessions.
        for (int capacity = 64; capacity <= 256; capacity *= 2)
        {
            unsafe
            {
                int propsSize = Marshal.SizeOf<EVENT_TRACE_PROPERTIES>();
                int nameBytes = (MaxNameChars + 1) * sizeof(char);
                int slotSize  = propsSize + nameBytes;

                // Allocate the property blocks and the pointer array in native heap.
                byte* block = (byte*)NativeMemory.AllocZeroed((nuint)(capacity * slotSize));
                EVENT_TRACE_PROPERTIES** ptrArray =
                    (EVENT_TRACE_PROPERTIES**)NativeMemory.AllocZeroed((nuint)(capacity * sizeof(void*)));
                try
                {
                    for (int i = 0; i < capacity; i++)
                    {
                        EVENT_TRACE_PROPERTIES* slot = (EVENT_TRACE_PROPERTIES*)(block + i * slotSize);
                        slot->Wnode.BufferSize = (uint)slotSize;
                        slot->LoggerNameOffset = (uint)propsSize;
                        ptrArray[i]            = slot;
                    }

                    uint error = Etw.QueryAllTraces(ptrArray, (uint)capacity, out uint loggerCount);

                    // Any error other than ERROR_MORE_DATA (234) is a real API failure.
                    if (error != 0 && error != 234u)
                    {
                        throw new Win32Exception((int)error,
                            $"QueryAllTracesW failed with Win32 error 0x{error:X8}.");
                    }

                    uint toRead = Math.Min(loggerCount, (uint)capacity);
                    List<string> names = new((int)toRead);
                    for (uint i = 0; i < toRead; i++)
                    {
                        EVENT_TRACE_PROPERTIES* slot = ptrArray[i];
                        char* namePtr = (char*)((byte*)slot + slot->LoggerNameOffset);
                        names.Add(new string(namePtr));
                    }

                    if (error == 234u && loggerCount > (uint)capacity)
                    {
                        // Outer loop will retry with doubled capacity.
                        continue;
                    }

                    return names;
                }
                finally
                {
                    NativeMemory.Free(ptrArray);
                    NativeMemory.Free(block);
                }
            }
        }

        // Reached only when QueryAllTracesW still returns ERROR_MORE_DATA after 256 slots —
        // an extraordinary situation (>256 concurrent ETW sessions).
        throw new Win32Exception(234,
            "QueryAllTracesW: the system reports more than 256 concurrent ETW sessions; " +
            "cannot enumerate all of them.");
    }

    // -----------------------------------------------------------------------
    // Private streaming infrastructure
    // -----------------------------------------------------------------------

    /// <summary>
    ///     Worker that runs on a dedicated background thread and drives <c>ProcessTrace</c>.
    ///     Supports both file-based and real-time sources:
    ///     <list type="bullet">
    ///         <item>When <paramref name="realtimeSessionName" /> is non-null, opens the named session for real-time consumption.</item>
    ///         <item>Otherwise, opens the files in <paramref name="files" />.</item>
    ///     </list>
    ///     For real-time sessions, cancellation is honoured by closing the trace handle, which causes
    ///     <c>ProcessTrace</c> to return promptly rather than waiting for the next flush-timer tick.
    /// </summary>
    private static void RunWorker(
        List<string>? files,
        string? realtimeSessionName,
        EtwJsonConverterOptions opts,
        Deserializer<EtwJsonChannelWriter> deserializer,
        ChannelWriter<PooledEventBuffer> writer,
        CancellationToken cancellationToken)
    {
        bool isRealtime = realtimeSessionName is not null;
        int sessionCount = isRealtime ? 1 : files!.Count;

        EVENT_TRACE_LOGFILEW[] sessions = new EVENT_TRACE_LOGFILEW[sessionCount];
        ulong[] handles = new ulong[sessionCount];
        int[] openTraceErrors = new int[sessionCount];

        // For real-time mode only: closed flag prevents double-close between the
        // cancellation callback and the finally block.
        int handleClosed = 0;

        try
        {
            for (int i = 0; i < sessionCount; ++i)
            {
                unsafe
                {
                    if (isRealtime)
                    {
                        sessions[i] = new EVENT_TRACE_LOGFILEW
                        {
                            LoggerName = realtimeSessionName,
                            EventRecordCallback = deserializer.Deserialize,
                            BufferCallback = _ => !cancellationToken.IsCancellationRequested,
                            LogFileMode = PInvoke.PROCESS_TRACE_MODE_EVENT_RECORD |
                                          PInvoke.PROCESS_TRACE_MODE_REAL_TIME
                        };

                        if (opts.PreserveRawTimestamps)
                        {
                            sessions[i].LogFileMode |= PInvoke.PROCESS_TRACE_MODE_RAW_TIMESTAMP;
                        }
                    }
                    else
                    {
                        sessions[i] = new EVENT_TRACE_LOGFILEW
                        {
                            LogFileName = files![i],
                            EventRecordCallback = deserializer.Deserialize,
                            BufferCallback = _ => !cancellationToken.IsCancellationRequested,
                            LogFileMode = PInvoke.PROCESS_TRACE_MODE_EVENT_RECORD
                        };

                        if (opts.PreserveRawTimestamps)
                        {
                            sessions[i].LogFileMode |= PInvoke.PROCESS_TRACE_MODE_RAW_TIMESTAMP;
                        }
                    }

                    handles[i] = Etw.OpenTrace(ref sessions[i]);
                    // Capture immediately — any subsequent Win32 call overwrites the TLS slot.
                    openTraceErrors[i] = Marshal.GetLastWin32Error();
                }
            }

            for (int i = 0; i < sessionCount; ++i)
            {
                unchecked
                {
                    if (handles[i] == (ulong)~0)
                    {
                        WIN32_ERROR error = (WIN32_ERROR)openTraceErrors[i];
                        string source = isRealtime ? realtimeSessionName! : files![i];
                        EtwOpenTraceException ex = new(error, source);
                        opts.ReportError?.Invoke("ERROR: " + ex.Message);
                        writer.TryComplete(ex);
                        return;
                    }
                }
            }

            // For real-time sessions, closing the handle from the cancellation callback causes
            // ProcessTrace to return promptly without waiting for the next flush-timer tick.
            using CancellationTokenRegistration reg = isRealtime
                ? cancellationToken.Register(() =>
                {
                    if (Interlocked.Exchange(ref handleClosed, 1) == 0)
                    {
                        Etw.CloseTrace(handles[0]);
                    }
                })
                : default;

            Etw.ProcessTrace(handles, (uint)handles.Length, IntPtr.Zero, IntPtr.Zero);
            GC.KeepAlive(sessions);
        }
        catch (Exception ex)
        {
            writer.TryComplete(ex);
            return;
        }
        finally
        {
            if (isRealtime)
            {
                // Safe close: guard against double-close if cancellation already fired.
                if (Interlocked.Exchange(ref handleClosed, 1) == 0)
                {
                    unchecked
                    {
                        if (handles[0] != 0 && handles[0] != (ulong)~0)
                        {
                            Etw.CloseTrace(handles[0]);
                        }
                    }
                }
            }
            else
            {
                for (int i = 0; i < sessionCount; ++i)
                {
                    if (handles[i] != 0)
                    {
                        unchecked
                        {
                            if (handles[i] != (ulong)~0)
                            {
                                Etw.CloseTrace(handles[i]);
                            }
                        }
                    }
                }
            }
        }

        writer.TryComplete();
    }

    private static async IAsyncEnumerable<ReadOnlyMemory<byte>> StreamEventsAsync(
        List<string>? files,
        string? realtimeSessionName,
        EtwJsonConverterOptions opts,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // All resources are created here — inside the iterator body — so they are only
        // allocated when MoveNextAsync is first called.  If the IAsyncEnumerable is never
        // iterated the worker thread is never started and no ETW handles are opened.

        const int channelCapacity = 256;
        Channel<PooledEventBuffer> channel = Channel.CreateBounded<PooledEventBuffer>(
            new BoundedChannelOptions(channelCapacity)
            {
                SingleWriter = true,
                SingleReader = true,
                FullMode = BoundedChannelFullMode.Wait
            });

        // Linked source: cancelled by either the caller's token or enumerator disposal
        // (break / early exit without explicit cancellation) so the worker is never
        // permanently blocked on a saturated channel.
        CancellationTokenSource linkedCts =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        EtwJsonChannelWriter channelWriter = new(channel.Writer, linkedCts.Token);
        Deserializer<EtwJsonChannelWriter> deserializer = new(
            channelWriter,
            opts.CustomProviderManifest,
            opts.WppDecodingContext,
            opts.OnWppFormatMissing,
            opts.RewriteWppProviderName
        );

        string workerName = realtimeSessionName is not null
            ? $"EtwUtil.EnumerateRealtimeEventsAsync worker [{realtimeSessionName}]"
            : "EtwUtil.EnumerateEventsAsync worker";

        Thread workerThread = new(() =>
            RunWorker(files, realtimeSessionName, opts, deserializer, channel.Writer, linkedCts.Token))
        {
            IsBackground = true,
            Name = workerName
        };
        workerThread.Start();

        PooledEventBuffer? previous = null;

        try
        {
            await foreach (PooledEventBuffer item in channel.Reader.ReadAllAsync(cancellationToken)
                               .ConfigureAwait(false))
            {
                // Return previous rental before yielding the next item.
                if (previous.HasValue)
                {
                    ArrayPool<byte>.Shared.Return(previous.Value.Rented);
                }

                previous = item;
                yield return item.Memory;
            }
        }
        finally
        {
            // Return the last rental, if any.
            if (previous.HasValue)
            {
                ArrayPool<byte>.Shared.Return(previous.Value.Rented);
            }

            // Signal the worker to stop if it is still blocked waiting on a full channel
            // (e.g., consumer broke out early without cancelling the caller's token).
            // This ensures ETW handles are always released promptly.
            linkedCts.Cancel();

            // Wait for the worker thread to finish so handles are closed before we return.
            workerThread.Join(millisecondsTimeout: 5000);
            linkedCts.Dispose();
        }
    }
}
