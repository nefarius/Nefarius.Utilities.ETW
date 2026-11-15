using System.Buffers.Binary;
using System.Runtime.InteropServices;

using Nefarius.Utilities.ETW.Etl;

namespace Nefarius.Utilities.ETW.Relay;

/// <summary>
///     Owns the native memory a relayed record is rebuilt in. The memory is private to this instance and reused for
///     every record, so the pointers inside the rebuilt <c>EVENT_RECORD</c> never come from the sender.
/// </summary>
/// <remarks>
///     The record, its extended data item array, the payload and all extended data live in one allocation. The
///     payload is followed by zeroed slack so a decoder that reads slightly past it stays inside owned memory.
/// </remarks>
internal sealed unsafe class RelayRecordBuffer : IDisposable
{
    private const int ExtendedItemSize = 16;
    private const int PayloadSlack = 64;
    private const int MinimumCapacity = 4096;

    private byte* _memory;
    private nuint _capacity;

    public void Dispose()
    {
        if (_memory != null)
        {
            NativeMemory.Free(_memory);
            _memory = null;
            _capacity = 0;
        }
    }

    /// <summary>
    ///     Rebuilds <paramref name="record" /> as a native <c>EVENT_RECORD</c>. The result is valid until the next call
    ///     to <see cref="Fill" /> or <see cref="Dispose" />.
    /// </summary>
    public EVENT_RECORD* Fill(RawEtwRecord record, out EtlRecordExtras extras)
    {
        int recordSize = Align8(sizeof(EVENT_RECORD));
        int itemCount = record.ExtendedData.Count;

        long needed = recordSize + (long)itemCount * ExtendedItemSize + Align8(record.Payload.Length + PayloadSlack);
        foreach (RawEtwExtendedData ext in record.ExtendedData)
        {
            needed += Align8(ext.Data.Length);
        }

        if (needed > int.MaxValue)
        {
            throw new RawEtwProtocolException(RawEtwProtocolError.InvalidLength, "Record is too large.");
        }

        EnsureCapacity((nuint)needed);
        new Span<byte>(_memory, (int)needed).Clear();

        EVENT_RECORD* native = (EVENT_RECORD*)_memory;
        byte* header = (byte*)&native->EventHeader;

        // EVENT_HEADER layout: see EtlReader. Offsets are fixed by the Windows ABI.
        *(ushort*)header = record.Size;
        *(ushort*)(header + 2) = record.HeaderType;
        // TraceLogging and the event reader select 4- or 8-byte pointers from this flag, not from PointerSize.
        *(ushort*)(header + 4) = ArchitectureFlags(record);
        *(ushort*)(header + 6) = record.EventProperty;
        *(uint*)(header + 8) = record.ThreadId;
        *(uint*)(header + 12) = record.ProcessId;
        *(long*)(header + 16) = record.TimeStamp;
        *(Guid*)(header + 24) = record.ProviderId;
        *(ushort*)(header + 40) = record.Id;
        header[42] = record.Version;
        header[43] = record.Channel;
        header[44] = record.Level;
        header[45] = record.Opcode;
        *(ushort*)(header + 46) = record.Task;
        *(ulong*)(header + 48) = record.Keyword;
        *(ulong*)(header + 56) = record.ProcessorTime;
        *(Guid*)(header + 64) = record.ActivityId;

        // ETW_BUFFER_CONTEXT follows the header; its first byte is the processor number.
        *(uint*)(header + 80) = record.BufferContext;

        byte* cursor = _memory + recordSize;
        EVENT_HEADER_EXTENDED_DATA_ITEM* items = itemCount == 0 ? null : (EVENT_HEADER_EXTENDED_DATA_ITEM*)cursor;
        cursor += (long)itemCount * ExtendedItemSize;

        byte* payload = cursor;
        record.Payload.CopyTo(new Span<byte>(payload, record.Payload.Length));
        cursor += Align8(record.Payload.Length + PayloadSlack);

        for (int i = 0; i < itemCount; i++)
        {
            RawEtwExtendedData ext = record.ExtendedData[i];
            byte* item = (byte*)items + (long)i * ExtendedItemSize;
            *(ushort*)item = ext.Reserved1;
            *(ushort*)(item + 2) = ext.ExtType;
            *(ushort*)(item + 4) = ext.Linkage;
            *(ushort*)(item + 6) = (ushort)ext.Data.Length;
            *(ulong*)(item + 8) = (ulong)cursor;
            ext.Data.CopyTo(new Span<byte>(cursor, ext.Data.Length));
            cursor += Align8(ext.Data.Length);
        }

        native->ExtendedDataCount = (ushort)itemCount;
        native->ExtendedData = items;
        native->UserDataLength = (ushort)record.Payload.Length;
        native->UserData = payload;
        native->UserContext = payload;

        bool isWpp = (record.Flags & PInvoke.EVENT_HEADER_FLAG_TRACE_MESSAGE) != 0;
        uint sequence = record.WppSequenceValid ? record.WppSequenceNumber : isWpp ? ActivityData1(record.ActivityId) : 0;

        // The relayed record has no live ETW session behind it, so decoding stays off TDH: its results depend on
        // providers registered on the receiving machine, and TDH is not hardened against synthetic input.
        extras = new EtlRecordExtras
        {
            IsManaged = true,
            SequenceNumber = sequence,
            FileTime = record.TimeStamp,
            HeaderVersion = record.Version
        };

        return native;
    }

    /// <summary>
    ///     Copies a native record into its relay representation. The inverse of <see cref="Fill" />.
    /// </summary>
    public static RawEtwRecord ToRelayRecord(EVENT_RECORD* native, in EtlRecordExtras extras)
    {
        byte* header = (byte*)&native->EventHeader;
        RawEtwRecord record = new()
        {
            Size = *(ushort*)header,
            HeaderType = *(ushort*)(header + 2),
            Flags = *(ushort*)(header + 4),
            EventProperty = *(ushort*)(header + 6),
            ThreadId = *(uint*)(header + 8),
            ProcessId = *(uint*)(header + 12),
            TimeStamp = *(long*)(header + 16),
            ProviderId = *(Guid*)(header + 24),
            Id = *(ushort*)(header + 40),
            Version = header[42],
            Channel = header[43],
            Level = header[44],
            Opcode = header[45],
            Task = *(ushort*)(header + 46),
            Keyword = *(ulong*)(header + 48),
            ProcessorTime = *(ulong*)(header + 56),
            ActivityId = *(Guid*)(header + 64),
            BufferContext = *(uint*)(header + 80),
            PointerSize = (byte)((*(ushort*)(header + 4) & 0x0020) != 0 ? 4 : 8),
            WppSequenceValid = extras.SequenceNumber != 0,
            WppSequenceNumber = extras.SequenceNumber,
            Payload = new ReadOnlySpan<byte>(native->UserData, native->UserDataLength).ToArray()
        };

        for (int i = 0; i < native->ExtendedDataCount; i++)
        {
            byte* item = (byte*)native->ExtendedData + (long)i * ExtendedItemSize;
            ushort size = *(ushort*)(item + 6);
            record.ExtendedData.Add(new RawEtwExtendedData
            {
                Reserved1 = *(ushort*)item,
                ExtType = *(ushort*)(item + 2),
                Linkage = *(ushort*)(item + 4),
                Data = new ReadOnlySpan<byte>((void*)*(ulong*)(item + 8), size).ToArray()
            });
        }

        return record;
    }

    private static uint ActivityData1(Guid activity)
    {
        Span<byte> bytes = stackalloc byte[16];
        activity.TryWriteBytes(bytes);
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes);
    }

    private void EnsureCapacity(nuint needed)
    {
        if (_capacity >= needed)
        {
            return;
        }

        // Allocate first. If it throws, the buffer already in use stays valid.
        nuint capacity = Math.Max(needed, MinimumCapacity);
        byte* memory = (byte*)NativeMemory.AllocZeroed(capacity);
        if (_memory != null)
        {
            NativeMemory.Free(_memory);
        }

        _memory = memory;
        _capacity = capacity;
    }

    /// <summary>
    ///     Header flags with the 32-bit-header bit forced to agree with <see cref="RawEtwRecord.PointerSize" />.
    /// </summary>
    private static ushort ArchitectureFlags(RawEtwRecord record)
    {
        const ushort flag32BitHeader = 0x0020;
        ushort flags = record.Flags;
        if (record.PointerSize == 4)
        {
            flags |= flag32BitHeader;
        }
        else if (record.PointerSize == 8)
        {
            flags &= unchecked((ushort)~flag32BitHeader);
        }

        return flags;
    }

    private static int Align8(int value) => (value + 7) & ~7;
}
