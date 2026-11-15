using System.Runtime.InteropServices;

using Windows.Win32.Foundation;

namespace Nefarius.Utilities.ETW.Etl;

/// <summary>
///     Receives one record at a time from the managed reader. The record and everything it points to are only valid
///     until the callback returns.
/// </summary>
internal unsafe delegate void EtlRecordCallback(EVENT_RECORD* record, in EtlRecordExtras extras);

/// <summary>
///     Managed replacement for <c>OpenTrace</c>/<c>ProcessTrace</c> on ETL files: walks the logger buffers of one or
///     more files, decodes the record headers into <see cref="EVENT_RECORD" />s and delivers the records merged in
///     timestamp order.
/// </summary>
/// <remarks>
///     Supported record header types: SYSTEM_TRACE (32/64, including the compact variants), PERFINFO (32/64), TRACE_MESSAGE (WPP), EVENT_HEADER
///     (32/64, without extended data), FULL_HEADER and INSTANCE (32/64). Anything else is skipped and reported once
///     per header type. Compressed traces are decompressed buffer by buffer when the file is opened.
/// </remarks>
internal static unsafe class EtlReader
{
    private const uint EventHeaderFlagExtendedInfo = 0x0001;
    private const int MaxExtendedItems = 32;
    private const int ExtendedItemSize = 16;
    private const ushort Flag32BitHeader = 0x0020;
    private const ushort Flag64BitHeader = 0x0040;
    private const ushort FlagTraceMessage = 0x0008;
    private const ushort FlagClassicHeader = 0x0100;
    private const ushort FlagNoCpuTime = 0x0010;

    // TRACE_MESSAGE option flags (ntwmi.h)
    private const ushort MessageSequence = 0x01;
    private const ushort MessageGuid = 0x02;
    private const ushort MessageComponentId = 0x04;
    private const ushort MessageTimestamp = 0x08;
    private const ushort MessagePerfTimestamp = 0x10;
    private const ushort MessageSystemInfo = 0x20;
    private const ushort MessagePointer32 = 0x40;
    private const ushort MessagePointer64 = 0x80;

    private static readonly Guid EventTraceGuid = new("68fdd900-4a3e-11d1-84f4-0000f80464e3");

    static EtlReader()
    {
        if (sizeof(EVENT_HEADER) != 80 || sizeof(EVENT_RECORD) != 112 && sizeof(void*) == 8)
        {
            throw new PlatformNotSupportedException("Unexpected EVENT_RECORD layout.");
        }
    }

    /// <summary>
    ///     Opens every file and delivers all records in timestamp order.
    /// </summary>
    /// <returns>
    ///     <see langword="null" /> on success, otherwise the index of the file that could not be opened and the
    ///     reason; no record is delivered in that case.
    /// </returns>
    /// <exception cref="EtlFormatException">A file passed the open checks but later turned out to be malformed.</exception>
    public static (int Index, EtlFormatException Error)? Process(
        IReadOnlyList<string> files,
        bool preserveRawTimestamps,
        EtlRecordCallback callback,
        Action<string>? reportWarning,
        CancellationToken cancellationToken)
    {
        List<EtlFile> opened = new(files.Count);
        try
        {
            for (int i = 0; i < files.Count; i++)
            {
                try
                {
                    opened.Add(EtlFile.Open(files[i]));
                }
                catch (EtlFormatException ex)
                {
                    return (i, ex);
                }
            }

            Merge(opened, preserveRawTimestamps, callback, reportWarning, cancellationToken);
            return null;
        }
        finally
        {
            foreach (EtlFile file in opened)
            {
                file.Dispose();
            }
        }
    }

    private static void Merge(List<EtlFile> files, bool preserveRaw, EtlRecordCallback callback,
        Action<string>? reportWarning, CancellationToken cancellationToken)
    {
        PriorityQueue<BufferCursor, (long, int, long, long)> queue = new();
        HashSet<int> reportedTypes = [];

        void Warn(int headerType, string message)
        {
            if (reportedTypes.Add(headerType))
            {
                reportWarning?.Invoke(message);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        long scanned = 0;
        for (int f = 0; f < files.Count; f++)
        {
            EtlFile file = files[f];
            for (int b = 0; b < file.Buffers.Count; b++)
            {
                if ((++scanned & 63) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                EtlFile.BufferSegment segment = file.Buffers[b];
                BufferCursor? cursor = BufferCursor.TryCreate(file, f, b, segment.Pointer, segment.Length);
                if (cursor is not null && cursor.Advance(Warn))
                {
                    queue.Enqueue(cursor, cursor.Key);
                }
            }
        }

        // The record and extended data scratch space are reused for every event.
        EVENT_RECORD* record = (EVENT_RECORD*)NativeMemory.AllocZeroed((nuint)sizeof(EVENT_RECORD));
        byte* extendedData = (byte*)NativeMemory.AllocZeroed(ExtendedItemSize * MaxExtendedItems);
        try
        {
            long counter = 0;
            while (queue.TryDequeue(out BufferCursor? cursor, out _))
            {
                if ((++counter & 0x3FF) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                EtlRecordExtras extras = cursor.Fill(record, extendedData, preserveRaw);
                if (cursor.DeliverRecord)
                {
                    callback(record, extras);
                }

                if (cursor.Advance(Warn))
                {
                    queue.Enqueue(cursor, cursor.Key);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            NativeMemory.Free(record);
            NativeMemory.Free(extendedData);
        }
    }

    private enum Kind
    {
        System,
        CompactSystem,
        PerfInfo,
        Message,
        EventHeader,
        FullHeader,
        Instance
    }

    private enum RecordDisposition
    {
        Deliver,
        Skip,
        Stop,
        Unknown
    }

    /// <summary>
    ///     Walks the events of a single logger buffer.
    /// </summary>
    private sealed class BufferCursor
    {
        private readonly byte* _end;
        private readonly EtlFile _file;
        private readonly int _fileIndex;
        private readonly long _bufferIndex;
        private readonly byte _processor;
        private readonly byte* _start;

        private byte* _position;
        private int _size;
        private Kind _kind;
        private bool _is64;
        private long _rawTimestamp;
        private long _fileTime;
        private string? _skipDetail;
        private bool _deliver = true;

        /// <summary>
        ///     False when the current record was recognized but must not be handed to the callback.
        /// </summary>
        public bool DeliverRecord => _deliver;

        private BufferCursor(EtlFile file, int fileIndex, long bufferIndex, byte* buffer, byte* end)
        {
            _file = file;
            _fileIndex = fileIndex;
            _bufferIndex = bufferIndex;
            _start = buffer;
            _position = buffer + EtlFile.BufferHeaderSize;
            _end = end;
            _processor = buffer[40];
        }

        public (long, int, long, long) Key => (_fileTime, _fileIndex, _bufferIndex, _position - _start);

        public static BufferCursor? TryCreate(EtlFile file, int fileIndex, long bufferIndex, byte* buffer, int length)
        {
            if (length <= EtlFile.BufferHeaderSize)
            {
                return null;
            }

            uint offset = *(uint*)(buffer + 48);
            if (offset == 0)
            {
                offset = *(uint*)(buffer + 4);
            }

            // Buffers are 8-byte aligned and never extend past the buffer size.
            long usable = Math.Min(Math.Min(offset, (uint)length), file.BufferSize);
            if (usable <= EtlFile.BufferHeaderSize)
            {
                return null;
            }

            return new BufferCursor(file, fileIndex, bufferIndex, buffer, buffer + usable);
        }

        /// <summary>
        ///     Moves to the next decodable event. Returns false when the buffer is exhausted.
        /// </summary>
        public bool Advance(Action<int, string> warn)
        {
            if (_size != 0)
            {
                _position += (_size + 7) & ~7;
                _size = 0;
            }

            while (_position + 8 <= _end)
            {
                uint marker = *(uint*)_position;
                if (marker == 0 || marker == 0xFFFFFFFF)
                {
                    return false;
                }

                switch (Classify(marker, out Kind kind, out bool is64, out int size, out long raw, out int headerType))
                {
                    case RecordDisposition.Deliver:
                        if (raw == long.MinValue)
                        {
                            warn(0xE100,
                                "Skipping an ETL record that has no timestamp; its order across buffers cannot be established.");
                            _position += (size + 7) & ~7;
                            continue;
                        }

                        _kind = kind;
                        _is64 = is64;
                        _size = size;
                        _rawTimestamp = raw;
                        _fileTime = _file.Info.ToFileTime(_rawTimestamp);
                        return true;
                    case RecordDisposition.Skip:
                        warn(headerType, _skipDetail ?? SkipWarning(headerType, marker));
                        _position += (size + 7) & ~7;
                        continue;
                    case RecordDisposition.Unknown:
                        warn(headerType,
                            $"Skipping events with unsupported ETL record header type 0x{headerType:X} " +
                            $"(marker 0x{marker:X8}) in file #{_fileIndex}, buffer {_bufferIndex}, offset {_position - _start}. " +
                            $"The remaining {_end - _position} bytes of this buffer were dropped.");
                        return false;
                    default:
                        return false;
                }
            }

            return false;
        }

        private static string SkipWarning(int headerType, uint marker)
        {
            return $"Skipping ETL record header type 0x{headerType:X} (marker 0x{marker:X8}).";
        }

        /// <summary>
        ///     Reads a timestamp only after the bytes are known to lie inside the buffer.
        /// </summary>
        private bool TryReadTimestamp(byte* record, int offset, int recordSize, int minimumSize, out long raw)
        {
            raw = long.MinValue;
            if (recordSize < minimumSize || record + offset + sizeof(long) > _end || record + recordSize > _end)
            {
                return false;
            }

            raw = *(long*)(record + offset);
            return true;
        }

        private RecordDisposition Classify(uint marker, out Kind kind, out bool is64, out int size, out long raw,
            out int headerType)
        {
            byte* p = _position;
            kind = Kind.System;
            is64 = true;
            size = 0;
            raw = long.MinValue;
            headerType = (int)(marker >> 24);
            _skipDetail = null;

            if ((marker & 0xC0000000) == 0xC0000000)
            {
                headerType = (byte)(marker >> 16);
                switch (headerType)
                {
                    case 1:
                    case 2:
                        kind = Kind.System;
                        is64 = headerType == 2;
                        size = *(ushort*)(p + 4);
                        if (!TryReadTimestamp(p, 16, size, 32, out raw))
                        {
                            return Bounded(size);
                        }

                        return KnownGroup(p[7]);
                    case 3:
                    case 4:
                        // A compact system header is a SYSTEM_TRACE header without the trailing kernel/user time
                        // pair: the payload starts at offset 24 and the event carries no CPU time.
                        kind = Kind.CompactSystem;
                        is64 = headerType == 4;
                        size = *(ushort*)(p + 4);
                        if (!TryReadTimestamp(p, 16, size, 24, out raw))
                        {
                            return Bounded(size);
                        }

                        return KnownGroup(p[7]);
                    case 0x10:
                    case 0x11:
                        kind = Kind.PerfInfo;
                        is64 = headerType == 0x11;
                        size = *(ushort*)(p + 4);
                        if (!TryReadTimestamp(p, 8, size, 16, out raw))
                        {
                            return Bounded(size);
                        }

                        return KnownGroup((byte)(*(ushort*)(p + 6) >> 8));
                    case 18:
                    case 19:
                        kind = Kind.EventHeader;
                        is64 = headerType == 19;
                        size = *(ushort*)p;
                        if (!TryReadTimestamp(p, 16, size, 80, out raw))
                        {
                            return Bounded(size);
                        }

                        // Fill walks the extended-data chain before the payload is exposed.
                        return RecordDisposition.Deliver;
                    case 10:
                    case 20:
                        kind = Kind.FullHeader;
                        is64 = headerType == 20;
                        size = *(ushort*)p;
                        return TryReadTimestamp(p, 16, size, 48, out raw) ? RecordDisposition.Deliver : Bounded(size);
                    case 11:
                    case 21:
                        kind = Kind.Instance;
                        is64 = headerType == 21;
                        size = *(ushort*)p;
                        return TryReadTimestamp(p, 16, size, 72, out raw) ? RecordDisposition.Deliver : Bounded(size);
                    default:
                        return RecordDisposition.Unknown;
                }
            }

            if ((marker & 0x80000000) != 0)
            {
                kind = Kind.Message;
                headerType = (byte)(marker >> 24);
                size = (ushort)marker;
                if (size < 8 || p + size > _end)
                {
                    return RecordDisposition.Stop;
                }

                ushort options = *(ushort*)(p + 6);
                is64 = (options & MessagePointer32) == 0;
                MessageLayout layout = MessageLayout.Parse(p, size, options);
                if (!layout.Valid)
                {
                    return RecordDisposition.Skip;
                }

                if (!layout.HasTimestamp)
                {
                    return RecordDisposition.Deliver;
                }

                if (layout.TimestampOffset < 0 || p + layout.TimestampOffset + sizeof(long) > _end)
                {
                    return RecordDisposition.Stop;
                }

                raw = *(long*)(p + layout.TimestampOffset);
                return RecordDisposition.Deliver;
            }

            return RecordDisposition.Unknown;
        }

        /// <summary>
        ///     A known header whose declared size runs past the buffer cannot be skipped safely.
        /// </summary>
        private RecordDisposition Bounded(int size) =>
            size >= 8 && _position + size <= _end ? RecordDisposition.Skip : RecordDisposition.Stop;

        /// <summary>
        ///     Decodes the chain of extended data items that follows an EVENT_HEADER in the file: an 8 byte item
        ///     header (reserved, type, linkage, size), the data padded to 8 bytes, repeated while the linkage bit
        ///     is set. Fills <paramref name="items" /> in the layout of <c>EVENT_HEADER_EXTENDED_DATA_ITEM</c>.
        /// </summary>
        /// <returns>Bytes consumed, or -1 when the chain does not fit into the record.</returns>
        private static int ReadExtendedData(byte* data, int length, byte* items, EVENT_RECORD* record)
        {
            int offset = 0;
            int count = 0;
            bool more = true;

            while (more)
            {
                if (count == MaxExtendedItems || offset + 8 > length)
                {
                    return -1;
                }

                ushort type = *(ushort*)(data + offset + 2);
                ushort linkage = *(ushort*)(data + offset + 4);
                ushort size = *(ushort*)(data + offset + 6);
                int dataOffset = offset + 8;
                if (dataOffset + size > length || size < MinimumExtendedDataSize(type))
                {
                    return -1;
                }

                byte* item = items + count * ExtendedItemSize;
                *(ulong*)item = 0;
                *(ushort*)(item + 2) = type;
                *(ushort*)(item + 4) = linkage;
                *(ushort*)(item + 6) = size;
                *(byte**)(item + 8) = data + dataOffset;

                count++;
                more = (linkage & 1) != 0;
                offset = (dataOffset + size + 7) & ~7;
                if (offset > length)
                {
                    return -1;
                }
            }

            record->ExtendedDataCount = (ushort)count;
            record->ExtendedData = (EVENT_HEADER_EXTENDED_DATA_ITEM*)items;
            return offset;
        }

        /// <summary>
        ///     Fixed prefix that consumers read before consulting <c>DataSize</c>. A shorter item is malformed.
        /// </summary>
        private static int MinimumExtendedDataSize(ushort type)
        {
            if (type == PInvoke.EVENT_HEADER_EXT_TYPE_RELATED_ACTIVITYID)
            {
                return 16;
            }

            if (type == (ushort)PInvoke.EVENT_HEADER_EXT_TYPE_STACK_TRACE32
                || type == (ushort)PInvoke.EVENT_HEADER_EXT_TYPE_STACK_TRACE64)
            {
                return sizeof(ulong);
            }

            return 0;
        }

        /// <summary>
        ///     Fills the <see cref="EVENT_RECORD" /> for the current event.
        /// </summary>
        public EtlRecordExtras Fill(EVENT_RECORD* record, byte* extendedData, bool preserveRaw)
        {
            _deliver = true;
            byte* p = _position;
            byte* h = (byte*)&record->EventHeader;
            new Span<byte>(h, sizeof(EVENT_HEADER)).Clear();
            *(h + 80) = _processor;
            *(ushort*)(h + 82) = *(ushort*)(_start + 42);
            record->ExtendedDataCount = 0;
            record->ExtendedData = null;

            long stamp = preserveRaw ? _rawTimestamp : _fileTime;
            ushort flags = _is64 ? Flag64BitHeader : Flag32BitHeader;
            uint sequence = 0;
            byte headerVersion = 0;
            byte* payload;
            int payloadLength;

            switch (_kind)
            {
                case Kind.System:
                {
                    TryGetKernelGroupGuid(p[7], out Guid group);
                    byte opcode = p[6];
                    WriteHeader(h, 32, (ushort)(flags | FlagClassicHeader), *(uint*)(p + 8), *(uint*)(p + 12), stamp,
                        group, 0, (byte)*(ushort*)p, 0, opcode);
                    *(ulong*)(h + 56) = *(ulong*)(p + 24);
                    headerVersion = (byte)*(ushort*)p;
                    payload = p + 32;
                    payloadLength = _size - 32;
                    break;
                }
                case Kind.CompactSystem:
                {
                    TryGetKernelGroupGuid(p[7], out Guid group);
                    WriteHeader(h, 24, (ushort)(flags | FlagClassicHeader | FlagNoCpuTime), *(uint*)(p + 8),
                        *(uint*)(p + 12), stamp, group, 0, (byte)*(ushort*)p, 0, p[6]);
                    headerVersion = (byte)*(ushort*)p;
                    payload = p + 24;
                    payloadLength = _size - 24;
                    break;
                }
                case Kind.PerfInfo:
                {
                    ushort hook = *(ushort*)(p + 6);
                    TryGetKernelGroupGuid((byte)(hook >> 8), out Guid group);
                    // A PERFINFO header has no thread or process. ProcessTrace reports both as -1.
                    WriteHeader(h, 16, (ushort)(flags | FlagClassicHeader | FlagNoCpuTime), uint.MaxValue, uint.MaxValue, stamp,
                        group, 0, (byte)*(ushort*)p, 0, (byte)hook);
                    headerVersion = (byte)*(ushort*)p;
                    payload = p + 16;
                    payloadLength = _size - 16;
                    break;
                }
                case Kind.Message:
                {
                    ushort options = *(ushort*)(p + 6);
                    MessageLayout layout = MessageLayout.Parse(p, _size, options);
                    Guid guid = layout.HasGuid ? *(Guid*)(p + layout.GuidOffset) : Guid.Empty;
                    sequence = layout.HasSequence ? *(uint*)(p + 8) : 0;
                    uint thread = layout.HasSystemInfo ? *(uint*)(p + layout.SystemInfoOffset) : 0;
                    uint process = layout.HasSystemInfo ? *(uint*)(p + layout.SystemInfoOffset + 4) : 0;
                    headerVersion = p[3];
                    WriteHeader(h, (ushort)_size, (ushort)(flags | FlagTraceMessage), thread, process, stamp, guid,
                        *(ushort*)(p + 4), 0, 0, 0);
                    if (layout.HasSequence)
                    {
                        // ProcessTrace publishes the message sequence as ActivityId, with the count in Data1.
                        *(uint*)(h + 64) = sequence;
                    }

                    payload = p + layout.DataOffset;
                    payloadLength = _size - layout.DataOffset;
                    break;
                }
                case Kind.EventHeader:
                {
                    Buffer.MemoryCopy(p, h, sizeof(EVENT_HEADER), sizeof(EVENT_HEADER));
                    *(long*)(h + 16) = stamp;
                    ushort headerFlags = *(ushort*)(h + 4);
                    *(ushort*)(h + 4) = (ushort)(headerFlags | flags);
                    headerVersion = *(h + 42);
                    payload = p + 80;
                    payloadLength = _size - 80;
                    if ((headerFlags & EventHeaderFlagExtendedInfo) != 0)
                    {
                        int consumed = ReadExtendedData(payload, payloadLength, extendedData, record);
                        if (consumed < 0)
                        {
                            // The chain is malformed. Leave the flag set and do not deliver a guessed payload.
                            _deliver = false;
                        }
                        else
                        {
                            payload += consumed;
                            payloadLength -= consumed;
                        }
                    }

                    break;
                }
                default:
                {
                    int headerSize = _kind == Kind.Instance ? 72 : 48;
                    Guid guid = *(Guid*)(p + 24);
                    WriteHeader(h, (ushort)headerSize, (ushort)(flags | FlagClassicHeader), *(uint*)(p + 8),
                        *(uint*)(p + 12), stamp, guid, 0, (byte)*(ushort*)(p + 6), p[5], p[4]);
                    *(ulong*)(h + 56) = *(ulong*)(p + 40);
                    headerVersion = (byte)*(ushort*)(p + 6);
                    payload = p + headerSize;
                    payloadLength = _size - headerSize;
                    break;
                }
            }

            if (payloadLength < 0)
            {
                payloadLength = 0;
            }

            record->UserData = payload;
            record->UserContext = payload;
            record->UserDataLength = (ushort)Math.Min(payloadLength, ushort.MaxValue);

            return new EtlRecordExtras
            {
                IsManaged = true,
                SequenceNumber = sequence,
                FileTime = _fileTime,
                HeaderVersion = headerVersion
            };
        }

        // EVENT_HEADER: Size@0 Flags@4 ThreadId@8 ProcessId@12 TimeStamp@16 ProviderId@24
        // EventDescriptor@40 (Id@40 Version@42 Channel@43 Level@44 Opcode@45 Task@46 Keyword@48)
        private static void WriteHeader(byte* h, ushort size, ushort flags, uint thread, uint process, long stamp,
            Guid provider, ushort id, byte version, byte level, byte opcode)
        {
            *(ushort*)h = size;
            *(ushort*)(h + 4) = flags;
            *(uint*)(h + 8) = thread;
            *(uint*)(h + 12) = process;
            *(long*)(h + 16) = stamp;
            *(Guid*)(h + 24) = provider;
            *(ushort*)(h + 40) = id;
            *(h + 42) = version;
            *(h + 44) = level;
            *(h + 45) = opcode;
        }

        private RecordDisposition KnownGroup(byte group)
        {
            if (TryGetKernelGroupGuid(group, out _))
            {
                return RecordDisposition.Deliver;
            }

            _skipDetail = $"Skipping kernel trace group 0x{group:X2}; its provider GUID is not known.";
            return RecordDisposition.Skip;
        }

        /// <summary>
        ///     Provider ids that <c>ProcessTrace</c> substitutes for the high byte of a kernel hook id.
        /// </summary>
        private static bool TryGetKernelGroupGuid(byte group, out Guid guid)
        {
            guid = group switch
            {
                0x00 => EventTraceGuid,
                0x01 => new Guid("3d6fa8d4-fe05-11d0-9dda-00c04fd7ba7c"),
                0x02 => new Guid("3d6fa8d3-fe05-11d0-9dda-00c04fd7ba7c"),
                0x03 => new Guid("3d6fa8d0-fe05-11d0-9dda-00c04fd7ba7c"),
                0x04 => new Guid("90cbdc39-4a3e-11d1-84f4-0000f80464e3"),
                0x05 => new Guid("3d6fa8d1-fe05-11d0-9dda-00c04fd7ba7c"),
                0x06 => new Guid("9a280ac0-c8e0-11d1-84e2-00c04fb998a2"),
                0x07 => new Guid("3282fc76-feed-498e-8aa7-e70f459d430e"),
                0x08 => new Guid("bf3a50c5-a9c9-4988-a005-2df0b7c80f80"),
                0x09 => new Guid("ae53722e-c863-11d2-8659-00c04fa321a1"),
                0x0A => new Guid("13976d09-a327-438c-950b-7f03192815c7"),
                0x0B => new Guid("01853a65-418f-4f36-aefc-dc0f1d2fd235"),
                0x0C => new Guid("99134383-5248-43fc-834b-529454e75df3"),
                0x0D => new Guid("42695762-ea50-497a-9068-5cbbb35e0b95"),
                0x0E => new Guid("0268a8b6-74fd-4302-9dd0-6e8f1795c0cf"),
                0x0F => new Guid("ce1dbfb4-137e-4da6-87b0-3f59aa102cbc"),
                0x10 => new Guid("222962ab-6180-4b88-a825-346b75f2a24a"),
                0x11 => new Guid("89497f50-effe-4440-8cf2-ce6b1cdcaca7"),
                0x12 => new Guid("e43445e0-0903-48c3-b878-ff0fccebdd04"),
                0x13 => new Guid("a9152f00-3f58-4bee-92a1-70c7d079d5dd"),
                0x14 => new Guid("2cb15d1d-5fc1-11d2-abe1-00a0c911f518"),
                0x15 => new Guid("b2d14872-7c5b-463d-8419-ee9bf7d23e04"),
                0x16 => new Guid("7687a439-f752-45b8-b741-321aec0f8df9"),
                0x17 => new Guid("3ac66736-cc59-4cff-8115-8df50e39816b"),
                0x18 => new Guid("def2fe46-7bd6-4b80-bd94-f57fe20d0ce3"),
                0x19 => new Guid("9aec974b-5b8e-4118-9b92-3186d8002ce5"),
                0x1A => new Guid("45d8cccd-539f-4b72-a8b7-5c683142609a"),
                0x1B => new Guid("d837ca92-12b9-44a5-ad6a-3a65b3578aa8"),
                0x1C => new Guid("c861d0e2-a2c1-4d36-9f9c-970bab943a12"),
                0x1D => new Guid("7f2a405c-69b5-4bf9-a1f5-30e8f1afab5e"),
                0x1E => new Guid("2ce9a149-effe-42f0-a635-a1d39e26c8f2"),
                _ => Guid.Empty
            };
            return guid != Guid.Empty || group == 0;
        }
    }

    /// <summary>
    ///     Offsets of the optional fields in a TRACE_MESSAGE record, driven by its option flags.
    /// </summary>
    private readonly struct MessageLayout
    {
        public bool Valid { get; init; }
        public bool HasSequence { get; init; }
        public bool HasGuid { get; init; }
        public int GuidOffset { get; init; }
        public bool HasTimestamp { get; init; }
        public int TimestampOffset { get; init; }
        public bool HasSystemInfo { get; init; }
        public int SystemInfoOffset { get; init; }
        public int DataOffset { get; init; }

        public static MessageLayout Parse(byte* p, int size, ushort options)
        {
            int offset = 8;
            bool sequence = (options & MessageSequence) != 0;
            if (sequence)
            {
                offset += 4;
            }

            bool guid = (options & MessageGuid) != 0;
            int guidOffset = offset;
            if (guid)
            {
                offset += 16;
            }
            else if ((options & MessageComponentId) != 0)
            {
                offset += 4;
            }

            bool timestamp = (options & (MessageTimestamp | MessagePerfTimestamp)) != 0;
            int timestampOffset = offset;
            if (timestamp)
            {
                offset += 8;
            }

            bool systemInfo = (options & MessageSystemInfo) != 0;
            int systemInfoOffset = offset;
            if (systemInfo)
            {
                offset += 8;
            }

            return new MessageLayout
            {
                Valid = offset <= size,
                HasSequence = sequence,
                HasGuid = guid,
                GuidOffset = guidOffset,
                HasTimestamp = timestamp,
                TimestampOffset = timestampOffset,
                HasSystemInfo = systemInfo,
                SystemInfoOffset = systemInfoOffset,
                DataOffset = offset
            };
        }
    }
}
