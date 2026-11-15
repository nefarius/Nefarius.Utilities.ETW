using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

using Windows.Win32.Foundation;

namespace Nefarius.Utilities.ETW.Etl;

/// <summary>
///     Session information recovered from the <c>TRACE_LOGFILE_HEADER</c> carried by the first event of an ETL file.
/// </summary>
internal sealed class EtlTraceInfo
{
    // EVENT_TRACE_COMPRESSED_MODE
    private const uint CompressedMode = 0x04000000;

    public uint BufferSize { get; init; }
    public uint Version { get; init; }
    public uint ProviderVersion { get; init; }
    public uint NumberOfProcessors { get; init; }
    public long EndTime { get; init; }
    public uint TimerResolution { get; init; }
    public uint LogFileMode { get; init; }
    public uint PointerSize { get; init; }
    public uint CpuSpeedInMHz { get; init; }
    public long BootTime { get; init; }
    public long PerfFreq { get; init; }
    public long StartTime { get; init; }

    /// <summary>
    ///     Raw timestamp of the logfile-header event. Used to calibrate QPC time when <see cref="BootTime" /> is zero.
    /// </summary>
    public long HeaderRawTimestamp { get; init; }

    /// <summary>
    ///     1 = performance counter, 2 = system time (FILETIME), 3 = CPU cycle counter.
    /// </summary>
    public uint ClockType { get; init; }

    public bool IsCompressed => (LogFileMode & CompressedMode) != 0;

    /// <summary>
    ///     Converts a raw event timestamp into a FILETIME (UTC).
    /// </summary>
    public long ToFileTime(long raw)
    {
        switch (ClockType)
        {
            case 2:
                return raw;
            case 3 when CpuSpeedInMHz > 0:
                return BootTime + (long)((Int128)raw * 10 / CpuSpeedInMHz);
            default:
                if (PerfFreq <= 0)
                {
                    return raw;
                }

                // ProcessTrace anchors the performance counter on the logfile header. StartTime is that
                // event's FILETIME and HeaderRawTimestamp is its counter value. BootTime is the machine boot
                // FILETIME, not the counter origin: using it shifts every event by a constant.
                long origin = StartTime - (long)((Int128)HeaderRawTimestamp * 10_000_000 / PerfFreq);
                return origin + (long)((Int128)raw * 10_000_000 / PerfFreq);
        }
    }
}

/// <summary>
///     A memory mapped ETL file.
/// </summary>
internal sealed unsafe class EtlFile : IDisposable
{
    internal const int BufferHeaderSize = 72;

    // The logfile header event is a SYSTEM_TRACE_HEADER (32 bytes) followed by the TRACE_LOGFILE_HEADER.
    private const int SystemHeaderSize = 32;

    private readonly MemoryMappedFile _file;
    private readonly MemoryMappedViewAccessor _view;
    private byte* _base;

    private EtlFile(MemoryMappedFile file, MemoryMappedViewAccessor view, byte* basePointer, long length,
        EtlTraceInfo info)
    {
        _file = file;
        _view = view;
        _base = basePointer;
        Length = length;
        Info = info;
    }

    public long Length { get; }

    public EtlTraceInfo Info { get; }

    public uint BufferSize => Info.BufferSize;

    /// <summary>
    ///     The logger buffers of the file in file order. Buffers of compressed files are decompressed up front into
    ///     memory owned by this instance.
    /// </summary>
    public IReadOnlyList<BufferSegment> Buffers => _buffers;

    private readonly List<BufferSegment> _buffers = [];
    private readonly List<nint> _owned = [];

    /// <summary>
    ///     Upper bound for the memory taken by decompressed buffers of one file.
    /// </summary>
    private const long MaxDecompressedBytes = 16L << 30;

    internal readonly struct BufferSegment(byte* pointer, int length)
    {
        public byte* Pointer { get; } = pointer;
        public int Length { get; } = length;
    }

    private void IndexBuffers(string path)
    {
        long bufferSize = BufferSize;
        if (!Info.IsCompressed)
        {
            for (long start = 0; start < Length; start += bufferSize)
            {
                _buffers.Add(new BufferSegment(_base + start, (int)Math.Min(bufferSize, Length - start)));
            }

            return;
        }

        // The first buffer carries the logfile header and is stored as is. Every following buffer is a 72 byte
        // buffer header whose first field holds the size of the whole record (header and data), followed by the
        // Xpress LZ77 compressed buffer contents. Records are not aligned.
        if (Length < bufferSize)
        {
            throw new EtlFormatException(WIN32_ERROR.ERROR_INVALID_DATA,
                $"'{path}' ends inside the first buffer of a compressed file.");
        }

        _buffers.Add(new BufferSegment(_base, (int)bufferSize));

        long position = bufferSize;
        long decompressed = 0;
        while (position < Length)
        {
            long remaining = Length - position;
            if (remaining < BufferHeaderSize)
            {
                throw new EtlFormatException(WIN32_ERROR.ERROR_INVALID_DATA,
                    $"'{path}' ends with a truncated compressed buffer header at offset {position}.");
            }

            uint recordSize = *(uint*)(_base + position);
            if (recordSize <= BufferHeaderSize || recordSize > remaining)
            {
                throw new EtlFormatException(WIN32_ERROR.ERROR_INVALID_DATA,
                    $"'{path}' has a compressed buffer of size {recordSize} at offset {position} that does not fit " +
                    $"into the file.");
            }

            decompressed += bufferSize;
            if (decompressed > MaxDecompressedBytes)
            {
                throw new EtlFormatException(WIN32_ERROR.ERROR_NOT_ENOUGH_MEMORY,
                    $"'{path}' decompresses to more than {MaxDecompressedBytes >> 30} GiB.");
            }

            byte* buffer;
            try
            {
                buffer = (byte*)NativeMemory.AllocZeroed((nuint)bufferSize);
            }
            catch (OutOfMemoryException ex)
            {
                throw new EtlFormatException(WIN32_ERROR.ERROR_NOT_ENOUGH_MEMORY,
                    $"Not enough memory to decompress '{path}'.", ex);
            }

            _owned.Add((nint)buffer);

            Buffer.MemoryCopy(_base + position, buffer, BufferHeaderSize, BufferHeaderSize);
            *(uint*)buffer = (uint)bufferSize;

            ReadOnlySpan<byte> compressed = new(_base + position + BufferHeaderSize, (int)recordSize - BufferHeaderSize);
            Span<byte> target = new(buffer + BufferHeaderSize, (int)bufferSize - BufferHeaderSize);
            if (!XpressLz77.TryDecompress(compressed, target, out int written) || written != target.Length)
            {
                throw new EtlFormatException(WIN32_ERROR.ERROR_INVALID_DATA,
                    $"'{path}' has a corrupt compressed buffer at offset {position}.");
            }

            _buffers.Add(new BufferSegment(buffer, (int)bufferSize));
            position += recordSize;
        }
    }

    private void FreeOwnedBuffers()
    {
        foreach (nint owned in _owned)
        {
            NativeMemory.Free((void*)owned);
        }

        _owned.Clear();
        _buffers.Clear();
    }

    public void Dispose()
    {
        FreeOwnedBuffers();

        if (_base is not null)
        {
            _view.SafeMemoryMappedViewHandle.ReleasePointer();
            _base = null;
        }

        _view.Dispose();
        _file.Dispose();
    }

    public static EtlFile Open(string path)
    {
        MemoryMappedFile? file = null;
        MemoryMappedViewAccessor? view = null;
        try
        {
            long length = new FileInfo(path).Length;
            if (length < EtlFile.BufferHeaderSize + SystemHeaderSize)
            {
                throw new EtlFormatException(WIN32_ERROR.ERROR_INVALID_DATA,
                    $"'{path}' is too small to be an ETL file.");
            }

            file = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
            MemoryMappedViewAccessor mappedView = file.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            view = mappedView;

            byte* pointer = null;
            mappedView.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
            try
            {
                EtlTraceInfo info = ReadTraceInfo(path, pointer, length);
                EtlFile result = new(file, mappedView, pointer, length, info);
                try
                {
                    result.IndexBuffers(path);
                }
                catch
                {
                    result.FreeOwnedBuffers();
                    throw;
                }

                file = null;
                view = null;
                return result;
            }
            catch
            {
                mappedView.SafeMemoryMappedViewHandle.ReleasePointer();
                throw;
            }
        }
        catch (EtlFormatException)
        {
            throw;
        }
        catch (FileNotFoundException ex)
        {
            throw new EtlFormatException(WIN32_ERROR.ERROR_FILE_NOT_FOUND, $"File '{path}' was not found.", ex);
        }
        catch (DirectoryNotFoundException ex)
        {
            throw new EtlFormatException(WIN32_ERROR.ERROR_PATH_NOT_FOUND, $"Path '{path}' was not found.", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new EtlFormatException(WIN32_ERROR.ERROR_ACCESS_DENIED, $"Access to '{path}' is denied.", ex);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new EtlFormatException(WIN32_ERROR.ERROR_BAD_PATHNAME, $"Path '{path}' is invalid.", ex);
        }
        catch (IOException ex)
        {
            throw new EtlFormatException(WIN32_ERROR.ERROR_READ_FAULT, $"'{path}' could not be read: {ex.Message}",
                ex);
        }
        finally
        {
            view?.Dispose();
            file?.Dispose();
        }
    }

    private static EtlTraceInfo ReadTraceInfo(string path, byte* data, long length)
    {
        uint bufferSize = *(uint*)data;
        if (bufferSize < 1024 || bufferSize > 0x10000000 || (bufferSize & 7) != 0)
        {
            throw new EtlFormatException(WIN32_ERROR.ERROR_INVALID_DATA,
                $"'{path}' is not an ETL file (implausible buffer size {bufferSize}).");
        }

        uint offset = *(uint*)(data + 48);
        if (offset == 0)
        {
            offset = *(uint*)(data + 4);
        }

        offset = (uint)Math.Min(Math.Min(offset, bufferSize), length);

        // The logfile header is the first event in the first buffer.
        byte* p = data + BufferHeaderSize;
        byte* end = data + offset;
        while (p + SystemHeaderSize <= end)
        {
            uint marker = *(uint*)p;
            byte headerType = (byte)(marker >> 16);
            if ((marker & 0xC0000000) != 0xC0000000 || headerType is not (1 or 2))
            {
                break;
            }

            ushort size = *(ushort*)(p + 4);
            if (size < SystemHeaderSize)
            {
                break;
            }

            // Packet.Group is 0 for the EventTrace group, Packet.Type is 0 for the header event.
            if (*(p + 6) == 0 && *(p + 7) == 0)
            {
                if (p + size > end)
                {
                    throw new EtlFormatException(WIN32_ERROR.ERROR_INVALID_DATA,
                        $"'{path}' has a logfile header event that extends past the buffer.");
                }

                return ParseLogfileHeader(path, p + SystemHeaderSize, size - SystemHeaderSize, bufferSize,
                    *(long*)(p + 16));
            }

            p += (size + 7) & ~7;
        }

        throw new EtlFormatException(WIN32_ERROR.ERROR_INVALID_DATA,
            $"'{path}' does not start with an ETW logfile header event.");
    }

    private static EtlTraceInfo ParseLogfileHeader(string path, byte* h, int size, uint bufferSize, long headerRaw)
    {
        // TRACE_LOGFILE_HEADER: 56 bytes of fixed fields, two pointers (LoggerName, LogFileName),
        // TIME_ZONE_INFORMATION (172 bytes), padding to 8, then BootTime, PerfFreq, StartTime, ReservedFlags.
        // The pointer size is at offset 44, so a shorter payload must not be read.
        if (size < 48)
        {
            throw new EtlFormatException(WIN32_ERROR.ERROR_INVALID_DATA,
                $"'{path}' has a truncated logfile header.");
        }

        uint pointerSize = *(uint*)(h + 44);
        if (pointerSize is not (4 or 8))
        {
            pointerSize = 8;
        }

        int timeZone = 56 + 2 * (int)pointerSize;
        int boot = (timeZone + 172 + 7) & ~7;
        if (size < boot + 8 + 8 + 8 + 4)
        {
            throw new EtlFormatException(WIN32_ERROR.ERROR_INVALID_DATA,
                $"'{path}' has a truncated logfile header.");
        }

        uint headerBufferSize = *(uint*)h;
        if (headerBufferSize != bufferSize)
        {
            throw new EtlFormatException(WIN32_ERROR.ERROR_INVALID_DATA,
                $"'{path}' has logfile header buffer size {headerBufferSize}, which does not match the buffer header ({bufferSize}).");
        }

        EtlTraceInfo info = new()
        {
            BufferSize = bufferSize,
            Version = *(uint*)(h + 4),
            ProviderVersion = *(uint*)(h + 8),
            NumberOfProcessors = *(uint*)(h + 12),
            EndTime = *(long*)(h + 16),
            TimerResolution = *(uint*)(h + 24),
            LogFileMode = *(uint*)(h + 32),
            PointerSize = pointerSize,
            CpuSpeedInMHz = *(uint*)(h + 52),
            BootTime = *(long*)(h + boot),
            PerfFreq = *(long*)(h + boot + 8),
            StartTime = *(long*)(h + boot + 16),
            HeaderRawTimestamp = headerRaw,
            ClockType = *(uint*)(h + boot + 24)
        };

        return info;
    }
}
