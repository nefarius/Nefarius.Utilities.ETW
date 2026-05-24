using System.Runtime.InteropServices;

using Windows.Win32.Foundation;

using Nefarius.Utilities.ETW.Deserializer.CustomParsers;
using Nefarius.Utilities.ETW.Deserializer.WPP;
using Nefarius.Utilities.ETW.Events;

namespace Nefarius.Utilities.ETW.Deserializer;

/// <summary>
///     Lightweight single-pass scanner that collects PDB metadata and image-ID family events from one or more ETL files
///     without invoking the full <see cref="Deserializer{T}" /> machinery (no TDH, no expression-tree compilation).
/// </summary>
internal sealed class MetadataScanner
{
    // Stored as a field so the GC never collects the delegate while ProcessTrace is running.
    private readonly PEVENT_RECORD_CALLBACK _callback;

    private readonly HashSet<PdbMetaData> _discovered = new();

    private readonly EtwMetadataScanOptions _options;

    internal unsafe MetadataScanner(EtwMetadataScanOptions options)
    {
        _options = options;
        _callback = Callback;
    }

    internal IReadOnlyCollection<PdbMetaData> Scan(List<string> inputFiles)
    {
        int count = inputFiles.Count;
        EVENT_TRACE_LOGFILEW[] fileSessions = new EVENT_TRACE_LOGFILEW[count];
        ulong[] handles = new ulong[count];
        WIN32_ERROR[] openErrors = new WIN32_ERROR[count];

        for (int i = 0; i < count; ++i)
        {
            unsafe
            {
                fileSessions[i] = new EVENT_TRACE_LOGFILEW
                {
                    LogFileName = inputFiles[i],
                    EventRecordCallback = _callback,
                    LogFileMode = PInvoke.PROCESS_TRACE_MODE_EVENT_RECORD
                };

                handles[i] = Etw.OpenTrace(ref fileSessions[i]);
                // Capture per-file immediately; subsequent OpenTrace calls would overwrite GetLastWin32Error.
                openErrors[i] = (WIN32_ERROR)Marshal.GetLastWin32Error();
            }
        }

        for (int i = 0; i < handles.Length; ++i)
        {
            unchecked
            {
                if (handles[i] != (ulong)~0)
                {
                    continue;
                }

                WIN32_ERROR lastError = openErrors[i];
                switch (lastError)
                {
                    case WIN32_ERROR.ERROR_INVALID_PARAMETER:
                        _options.ReportError?.Invoke("ERROR: For file: " + inputFiles[i] +
                                                     " Windows returned 0x57 -- The Logfile parameter is NULL.");
                        break;
                    case WIN32_ERROR.ERROR_BAD_PATHNAME:
                        _options.ReportError?.Invoke("ERROR: For file: " + inputFiles[i] +
                                                     " Windows returned 0xA1 -- The specified path is invalid.");
                        break;
                    case WIN32_ERROR.ERROR_ACCESS_DENIED:
                        _options.ReportError?.Invoke("ERROR: For file: " + inputFiles[i] +
                                                     " Windows returned 0x5 -- Access is denied.");
                        break;
                    default:
                        _options.ReportError?.Invoke("ERROR: For file: " + inputFiles[i] +
                                                     $" Windows returned an unknown error: 0x{(uint)lastError:X8} ({lastError}).");
                        break;
                }

                // Close any handles opened before this failure then bail out.
                for (int j = 0; j < i; j++)
                {
                    Etw.CloseTrace(handles[j]);
                }

                return _discovered;
            }
        }

        Etw.ProcessTrace(handles, (uint)handles.Length, IntPtr.Zero, IntPtr.Zero);

        // Keep fileSessions alive until after ProcessTrace returns so the GC does not collect
        // the embedded delegate or log-file name strings while the callback can still fire.
        GC.KeepAlive(fileSessions);

        for (int i = 0; i < count; ++i)
        {
            Etw.CloseTrace(handles[i]);
        }

        return _discovered;
    }

    private unsafe void Callback(EVENT_RECORD* eventRecord)
    {
        Guid providerId = eventRecord->EventHeader.ProviderId;
        byte opcode = eventRecord->EventHeader.EventDescriptor.Opcode;

        // EventRecordReader uses UserContext as the base pointer for length calculations.
        eventRecord->UserContext = eventRecord->UserData;
        EventRecordReader reader = new(eventRecord);

        try
        {
            if (providerId == CustomParserGuids.KernelTraceControlImageIdGuid)
            {
                switch (opcode)
                {
                    case 0:
                        HandleImageId(reader, eventRecord);
                        break;
                    case 36:
                        HandleDbgIdRsds(reader, eventRecord);
                        break;
                    case 64:
                        HandleImageIdFileVersion(reader, eventRecord);
                        break;
                }
            }
            else if (providerId == PInvoke.EventTraceGuid && opcode == 36)
            {
                HandleKernelDbgIdRsds(reader, eventRecord);
            }
        }
        catch
        {
            // Swallow: a malformed or unexpected event payload must not crash the scan.
        }
    }

    private unsafe void HandleDbgIdRsds(EventRecordReader reader, EVENT_RECORD* eventRecord)
    {
        ulong imageBase = reader.ReadUInt64();
        uint processId = reader.ReadUInt32();
        Guid guidSig = reader.ReadGuid();
        uint age = reader.ReadUInt32();
        string pdbFileName = reader.ReadAnsiString();

        DbgIdRsdsEventInfo info = new()
        {
            Timestamp = eventRecord->EventHeader.TimeStamp,
            ProcessId = eventRecord->EventHeader.ProcessId,
            ThreadId = eventRecord->EventHeader.ThreadId,
            ImageBase = imageBase,
            GuidSig = guidSig,
            Age = age,
            PdbFileName = pdbFileName
        };

        _options.OnDbgIdRsds?.Invoke(info);

        if (!string.IsNullOrEmpty(pdbFileName))
        {
            _discovered.Add(info.ToPdbMetaData());
        }
    }

    private unsafe void HandleImageId(EventRecordReader reader, EVENT_RECORD* eventRecord)
    {
        if (_options.OnImageId is null)
        {
            return;
        }

        ulong imageBase = reader.ReadPointer();
        uint imageSize = reader.ReadUInt32();
        reader.ReadPointer(); // skip extra pointer present in the layout
        uint timeDateStamp = reader.ReadUInt32();
        string originalFileName = reader.ReadUnicodeString();

        _options.OnImageId.Invoke(new ImageIdEventInfo
        {
            Timestamp = eventRecord->EventHeader.TimeStamp,
            ProcessId = eventRecord->EventHeader.ProcessId,
            ThreadId = eventRecord->EventHeader.ThreadId,
            ImageBase = imageBase,
            ImageSize = imageSize,
            TimeDateStamp = timeDateStamp,
            OriginalFileName = originalFileName
        });
    }

    private unsafe void HandleImageIdFileVersion(EventRecordReader reader, EVENT_RECORD* eventRecord)
    {
        if (_options.OnImageIdFileVersion is null)
        {
            return;
        }

        uint imageSize = reader.ReadUInt32();
        uint timeDateStamp = reader.ReadUInt32();
        string origFileName = reader.ReadUnicodeString();
        string fileDescription = reader.ReadUnicodeString();
        string fileVersion = reader.ReadUnicodeString();
        string binFileVersion = reader.ReadUnicodeString();
        string verLanguage = reader.ReadUnicodeString();
        string productName = reader.ReadUnicodeString();
        string companyName = reader.ReadUnicodeString();
        string productVersion = reader.ReadUnicodeString();
        string fileId = reader.ReadUnicodeString();
        string programId = reader.ReadUnicodeString();

        _options.OnImageIdFileVersion.Invoke(new ImageIdFileVersionEventInfo
        {
            Timestamp = eventRecord->EventHeader.TimeStamp,
            ProcessId = eventRecord->EventHeader.ProcessId,
            ThreadId = eventRecord->EventHeader.ThreadId,
            ImageSize = imageSize,
            TimeDateStamp = timeDateStamp,
            OrigFileName = origFileName,
            FileDescription = fileDescription,
            FileVersion = fileVersion,
            BinFileVersion = binFileVersion,
            VerLanguage = verLanguage,
            ProductName = productName,
            CompanyName = companyName,
            ProductVersion = productVersion,
            FileId = fileId,
            ProgramId = programId
        });
    }

    private unsafe void HandleKernelDbgIdRsds(EventRecordReader reader, EVENT_RECORD* eventRecord)
    {
        Guid pdbGuid = reader.ReadGuid();
        uint age = reader.ReadUInt32();
        string pdbName = reader.ReadAnsiString();

        KernelDbgIdRsdsEventInfo info = new()
        {
            Timestamp = eventRecord->EventHeader.TimeStamp,
            ProcessId = eventRecord->EventHeader.ProcessId,
            ThreadId = eventRecord->EventHeader.ThreadId,
            Guid = pdbGuid,
            Age = age,
            PdbName = pdbName
        };

        _options.OnKernelDbgIdRsds?.Invoke(info);

        if (!string.IsNullOrEmpty(pdbName))
        {
            _discovered.Add(info.ToPdbMetaData());
        }
    }
}
