using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text.Json;

using Windows.Win32.Foundation;

using Nefarius.Utilities.ETW.Deserializer;
using Nefarius.Utilities.ETW.Deserializer.WPP;

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
}