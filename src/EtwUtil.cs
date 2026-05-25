using System.Buffers;
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
            opts.WppDecodingContext
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

        // Bounded channel: limits how far ahead the producer can run relative to the consumer.
        const int channelCapacity = 256;
        Channel<PooledEventBuffer> channel = Channel.CreateBounded<PooledEventBuffer>(
            new BoundedChannelOptions(channelCapacity)
            {
                SingleWriter = true,
                SingleReader = true,
                FullMode = BoundedChannelFullMode.Wait
            });

        EtwJsonChannelWriter channelWriter = new(channel.Writer, cancellationToken);
        Deserializer<EtwJsonChannelWriter> deserializer = new(
            channelWriter,
            opts.CustomProviderManifest,
            opts.WppDecodingContext
        );

        Thread workerThread = new(() => RunWorker(list, opts, deserializer, channel.Writer, cancellationToken))
        {
            IsBackground = true,
            Name = "EtwUtil.EnumerateEventsAsync worker"
        };
        workerThread.Start();

        return StreamEventsAsync(channel.Reader, workerThread, cancellationToken);
    }

    private static void RunWorker(
        List<string> list,
        EtwJsonConverterOptions opts,
        Deserializer<EtwJsonChannelWriter> deserializer,
        ChannelWriter<PooledEventBuffer> writer,
        CancellationToken cancellationToken)
    {
        int count = list.Count;
        EVENT_TRACE_LOGFILEW[] fileSessions = new EVENT_TRACE_LOGFILEW[count];
        ulong[] handles = new ulong[count];

        try
        {
            for (int i = 0; i < count; ++i)
            {
                unsafe
                {
                    fileSessions[i] = new EVENT_TRACE_LOGFILEW
                    {
                        LogFileName = list[i],
                        EventRecordCallback = deserializer.Deserialize,
                        BufferCallback = _ => !cancellationToken.IsCancellationRequested,
                        LogFileMode = PInvoke.PROCESS_TRACE_MODE_EVENT_RECORD
                    };

                    if (opts.PreserveRawTimestamps)
                    {
                        fileSessions[i].LogFileMode |= PInvoke.PROCESS_TRACE_MODE_RAW_TIMESTAMP;
                    }

                    handles[i] = Etw.OpenTrace(ref fileSessions[i]);
                }
            }

            for (int i = 0; i < count; ++i)
            {
                unchecked
                {
                    if (handles[i] == (ulong)~0)
                    {
                        WIN32_ERROR error = (WIN32_ERROR)Marshal.GetLastWin32Error();
                        EtwOpenTraceException ex = new(error, list[i]);
                        opts.ReportError?.Invoke("ERROR: " + ex.Message);
                        writer.TryComplete(ex);
                        return;
                    }
                }
            }

            Etw.ProcessTrace(handles, (uint)handles.Length, IntPtr.Zero, IntPtr.Zero);
            GC.KeepAlive(fileSessions);
        }
        catch (Exception ex)
        {
            writer.TryComplete(ex);
            return;
        }
        finally
        {
            for (int i = 0; i < count; ++i)
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

        writer.TryComplete();
    }

    private static async IAsyncEnumerable<ReadOnlyMemory<byte>> StreamEventsAsync(
        ChannelReader<PooledEventBuffer> reader,
        Thread workerThread,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        PooledEventBuffer? previous = null;

        try
        {
            await foreach (PooledEventBuffer item in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
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

            // Wait for the worker thread to finish so handles are closed before we return.
            workerThread.Join(millisecondsTimeout: 5000);
        }
    }
}