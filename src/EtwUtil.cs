using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text.Json;

using ETWDeserializer;
using ETWDeserializer.CustomParsers;

namespace Nefarius.Utilities.ETW;

/// <summary>
///     ETW utility class.
/// </summary>
[SuppressMessage("ReSharper", "UnusedMember.Global")]
public static class EtwUtil
{
    /// <summary>
    ///     Converts one or more .ETL files to a JSON object.
    /// </summary>
    /// <param name="jsonWriter">The target JSON writer to write to.</param>
    /// <param name="inputFiles">One or more input files.</param>
    /// <param name="reportError">Potential parsing errors.</param>
    /// <returns>True on success, false otherwise.</returns>
    public static bool ConvertToJson(Utf8JsonWriter jsonWriter, IEnumerable<string> inputFiles,
        Action<string> reportError)
    {
        List<string> list = inputFiles.ToList();
        Deserializer<EtwJsonWriter> deserializer = new Deserializer<EtwJsonWriter>(new EtwJsonWriter(jsonWriter),
            provider =>
            {
                return provider != CustomParserGuids.ViGEmRuntimeGuid ? null : File.OpenRead(@"D:\Development\git.nefarius.at\ViGEm Framework\library\ViGEmRuntimeETW.man");
            });

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
                    LogFileMode = Etw.PROCESS_TRACE_MODE_EVENT_RECORD | Etw.PROCESS_TRACE_MODE_RAW_TIMESTAMP
                };

                handles[i] = Etw.OpenTrace(ref fileSessions[i]);
            }
        }

        for (int i = 0; i < handles.Length; ++i)
        {
            unchecked
            {
                if (handles[i] == (ulong)~0)
                {
                    switch (Marshal.GetLastWin32Error())
                    {
                        case 0x57:
                            reportError("ERROR: For file: " + list[i] +
                                        " Windows returned 0x57 -- The Logfile parameter is NULL.");
                            return false;
                        case 0xA1:
                            reportError("ERROR: For file: " + list[i] +
                                        " Windows returned 0xA1 -- The specified path is invalid.");
                            return false;
                        case 0x5:
                            reportError("ERROR: For file: " + list[i] + " Windows returned 0x5 -- Access is denied.");
                            return false;
                        default:
                            reportError("ERROR: For file: " + list[i] + " Windows returned an unknown error.");
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