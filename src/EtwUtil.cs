﻿using System.Diagnostics.CodeAnalysis;
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
    ///     Converts one or more .ETL files to a JSON object.
    /// </summary>
    /// <param name="jsonWriter">The target JSON writer to write to.</param>
    /// <param name="inputFiles">One or more input files.</param>
    /// <param name="reportError">Potential parsing errors.</param>
    /// <param name="customProviderManifest">Optionally called to load custom manifests for providers.</param>
    /// <param name="decodingContext">Optional <see cref="DecodingContext"/> to read WPP events.</param>
    /// <returns>True on success, false otherwise.</returns>
    public static bool ConvertToJson(Utf8JsonWriter jsonWriter, IEnumerable<string> inputFiles,
        Action<string> reportError, Func<Guid, Stream?>? customProviderManifest = null, DecodingContext? decodingContext = null)
    {
        List<string> list = inputFiles.ToList();
        Deserializer<EtwJsonWriter> deserializer = new(new EtwJsonWriter(jsonWriter), customProviderManifest, decodingContext);

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
                    LogFileMode = PInvoke.PROCESS_TRACE_MODE_EVENT_RECORD | PInvoke.PROCESS_TRACE_MODE_RAW_TIMESTAMP
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
                    switch ((WIN32_ERROR)Marshal.GetLastWin32Error())
                    {
                        case WIN32_ERROR.ERROR_INVALID_PARAMETER:
                            reportError("ERROR: For file: " + list[i] +
                                        " Windows returned 0x57 -- The Logfile parameter is NULL.");
                            return false;
                        case WIN32_ERROR.ERROR_BAD_PATHNAME:
                            reportError("ERROR: For file: " + list[i] +
                                        " Windows returned 0xA1 -- The specified path is invalid.");
                            return false;
                        case WIN32_ERROR.ERROR_ACCESS_DENIED:
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