using System.Buffers.Binary;
using System.Text;

namespace Nefarius.Utilities.ETW.Relay;

/// <summary>
///     Encodes and decodes the raw ETW relay wire format (version 1).
/// </summary>
/// <remarks>
///     <para>
///         All integers are little-endian. GUIDs are stored in RFC 4122 byte order, never as the in-memory Windows
///         layout. Every frame is a multiple of 8 bytes long and starts with a 32-byte prefix:
///     </para>
///     <code>
///     0   4  magic "NER1"
///     4   1  major version (1)
///     5   1  minor version (0)
///     6   2  frame type
///     8   4  total frame length, prefix included
///     12  4  frame flags (0 in version 1)
///     16  8  producer sequence number
///     24  2  length of the type-specific header that follows the prefix
///     26  2  number of extended data items (event frames only)
///     28  4  CRC32C of the whole frame computed with this field zeroed, or 0 when absent
///     </code>
///     <para>See the repository README for the type-specific layouts.</para>
/// </remarks>
public static class RawEtwFrameCodec
{
    /// <summary>Length of the frame prefix in bytes.</summary>
    public const int PrefixLength = 32;

    /// <summary>Length of the event frame header in bytes.</summary>
    public const int EventHeaderLength = 96;

    /// <summary>Length of the hello frame header in bytes.</summary>
    public const int HelloHeaderLength = 32;

    /// <summary>Length of the loss frame header in bytes.</summary>
    public const int LossHeaderLength = 16;

    /// <summary>The supported major protocol version.</summary>
    public const byte MajorVersion = 1;

    /// <summary>The minor protocol version written by this implementation.</summary>
    public const byte MinorVersion = 0;

    private const int ExtendedItemHeaderLength = 8;

    private static ReadOnlySpan<byte> Magic => "NER1"u8;

    /// <summary>
    ///     Serializes a frame.
    /// </summary>
    /// <param name="frame">The frame to serialize.</param>
    /// <param name="withCrc">Whether to include a CRC32C.</param>
    /// <returns>The complete wire representation.</returns>
    public static byte[] Encode(RawEtwFrame frame, bool withCrc = true)
    {
        ArgumentNullException.ThrowIfNull(frame);

        int headerLength;
        int bodyLength;
        ushort extCount = 0;
        byte[]? producer = null;

        switch (frame.Type)
        {
            case RawEtwFrameType.Hello:
                RawEtwHello hello = frame.Hello ?? throw new ArgumentException("Hello frame has no Hello.", nameof(frame));
                producer = Encoding.UTF8.GetBytes(hello.Producer);
                headerLength = HelloHeaderLength;
                bodyLength = Align8(producer.Length);
                break;
            case RawEtwFrameType.Event:
                RawEtwRecord record = frame.Record ?? throw new ArgumentException("Event frame has no Record.", nameof(frame));
                if (record.Payload.Length > ushort.MaxValue)
                {
                    throw new ArgumentException("Payload exceeds 65535 bytes.", nameof(frame));
                }

                if (record.ExtendedData.Count > ushort.MaxValue)
                {
                    throw new ArgumentException("Too many extended data items.", nameof(frame));
                }

                extCount = (ushort)record.ExtendedData.Count;
                headerLength = EventHeaderLength;
                long size = Align8(record.Payload.Length);
                foreach (RawEtwExtendedData ext in record.ExtendedData)
                {
                    if (ext.Data.Length > ushort.MaxValue)
                    {
                        throw new ArgumentException("Extended data item exceeds 65535 bytes.", nameof(frame));
                    }

                    size += ExtendedItemHeaderLength + Align8(ext.Data.Length);
                }

                bodyLength = checked((int)size);
                break;
            case RawEtwFrameType.Loss:
                _ = frame.Loss ?? throw new ArgumentException("Loss frame has no Loss.", nameof(frame));
                headerLength = LossHeaderLength;
                bodyLength = 0;
                break;
            case RawEtwFrameType.End:
                headerLength = 0;
                bodyLength = 0;
                break;
            default:
                throw new ArgumentException($"Frame type {(ushort)frame.Type} cannot be encoded.", nameof(frame));
        }

        int total = checked(PrefixLength + headerLength + bodyLength);
        byte[] buffer = new byte[total];
        Span<byte> span = buffer;

        Magic.CopyTo(span);
        span[4] = MajorVersion;
        span[5] = MinorVersion;
        BinaryPrimitives.WriteUInt16LittleEndian(span[6..], (ushort)frame.Type);
        BinaryPrimitives.WriteUInt32LittleEndian(span[8..], (uint)total);
        BinaryPrimitives.WriteUInt64LittleEndian(span[16..], frame.Sequence);
        BinaryPrimitives.WriteUInt16LittleEndian(span[24..], (ushort)headerLength);
        BinaryPrimitives.WriteUInt16LittleEndian(span[26..], extCount);

        Span<byte> header = span.Slice(PrefixLength, headerLength);
        Span<byte> body = span[(PrefixLength + headerLength)..];

        switch (frame.Type)
        {
            case RawEtwFrameType.Hello:
                RawEtwHello h = frame.Hello!;
                WriteGuid(header, h.SessionId);
                BinaryPrimitives.WriteUInt32LittleEndian(header[16..], h.Capabilities);
                BinaryPrimitives.WriteUInt32LittleEndian(header[20..], h.MaxFrameLength);
                producer!.CopyTo(body);
                break;
            case RawEtwFrameType.Event:
                WriteEvent(header, body, frame.Record!);
                break;
            case RawEtwFrameType.Loss:
                RawEtwLoss loss = frame.Loss!;
                BinaryPrimitives.WriteUInt64LittleEndian(header, loss.LostEvents);
                BinaryPrimitives.WriteUInt32LittleEndian(header[8..], loss.Reason);
                break;
        }

        if (withCrc)
        {
            uint crc = ComputeCrc(span);
            BinaryPrimitives.WriteUInt32LittleEndian(span[28..], crc);
        }

        return buffer;
    }

    /// <summary>
    ///     Decodes and validates one complete frame.
    /// </summary>
    /// <param name="frame">Exactly one frame.</param>
    /// <param name="limits">The limits to enforce; <see cref="RawEtwRelayLimits.Default" /> when <see langword="null" />.</param>
    /// <exception cref="RawEtwProtocolException">The frame is malformed or exceeds a limit.</exception>
    public static RawEtwFrame Decode(ReadOnlySpan<byte> frame, RawEtwRelayLimits? limits = null)
    {
        limits ??= RawEtwRelayLimits.Default;

        if (frame.Length < PrefixLength)
        {
            throw new RawEtwProtocolException(RawEtwProtocolError.Truncated, "Frame is shorter than the prefix.");
        }

        uint total = ValidatePrefix(frame[..PrefixLength], limits);
        if (frame.Length != total)
        {
            throw new RawEtwProtocolException(
                frame.Length < total ? RawEtwProtocolError.Truncated : RawEtwProtocolError.InvalidLength,
                $"Frame holds {frame.Length} bytes but declares {total}.");
        }

        uint storedCrc = BinaryPrimitives.ReadUInt32LittleEndian(frame[28..]);
        if (storedCrc == 0)
        {
            if (limits.RequireCrc)
            {
                throw new RawEtwProtocolException(RawEtwProtocolError.ChecksumMismatch, "Frame has no CRC32C.");
            }
        }
        else if (ComputeCrc(frame) != storedCrc)
        {
            throw new RawEtwProtocolException(RawEtwProtocolError.ChecksumMismatch, "CRC32C mismatch.");
        }

        RawEtwFrameType type = (RawEtwFrameType)BinaryPrimitives.ReadUInt16LittleEndian(frame[6..]);
        ulong sequence = BinaryPrimitives.ReadUInt64LittleEndian(frame[16..]);
        int headerLength = BinaryPrimitives.ReadUInt16LittleEndian(frame[24..]);
        int extCount = BinaryPrimitives.ReadUInt16LittleEndian(frame[26..]);

        if (headerLength % 8 != 0 || PrefixLength + headerLength > frame.Length)
        {
            throw new RawEtwProtocolException(RawEtwProtocolError.InvalidLength, "Invalid header length.");
        }

        ReadOnlySpan<byte> header = frame.Slice(PrefixLength, headerLength);
        ReadOnlySpan<byte> body = frame[(PrefixLength + headerLength)..];

        RawEtwFrame result = new() { Type = type, Sequence = sequence };

        if (type != RawEtwFrameType.Event && extCount != 0)
        {
            throw new RawEtwProtocolException(RawEtwProtocolError.InvalidField,
                "Only event frames may carry extended data items.");
        }

        switch (type)
        {
            case RawEtwFrameType.Hello:
                result.Hello = ReadHello(header, body, limits);
                break;
            case RawEtwFrameType.Event:
                result.Record = ReadEvent(header, body, extCount, limits);
                break;
            case RawEtwFrameType.Loss:
                if (header.Length < LossHeaderLength)
                {
                    throw new RawEtwProtocolException(RawEtwProtocolError.InvalidLength, "Loss header too short.");
                }

                if (BinaryPrimitives.ReadUInt32LittleEndian(header[12..]) != 0)
                {
                    throw new RawEtwProtocolException(RawEtwProtocolError.InvalidField, "Reserved field is not zero.");
                }

                result.Loss = new RawEtwLoss
                {
                    LostEvents = BinaryPrimitives.ReadUInt64LittleEndian(header),
                    Reason = BinaryPrimitives.ReadUInt32LittleEndian(header[8..])
                };
                break;
        }

        return result;
    }

    /// <summary>
    ///     Reads the next frame from a stream.
    /// </summary>
    /// <param name="stream">The stream to read from.</param>
    /// <param name="limits">The limits to enforce; <see cref="RawEtwRelayLimits.Default" /> when <see langword="null" />.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The frame, or <see langword="null" /> when the stream ended cleanly between frames.</returns>
    /// <exception cref="RawEtwProtocolException">The stream ended inside a frame or the frame is malformed.</exception>
    public static async ValueTask<RawEtwFrame?> ReadFrameAsync(Stream stream, RawEtwRelayLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        byte[]? bytes = await ReadFrameBytesAsync(stream, limits, cancellationToken).ConfigureAwait(false);
        return bytes is null ? null : Decode(bytes, limits);
    }

    /// <summary>
    ///     Reads the bytes of the next frame without decoding its content. The length is validated against
    ///     <paramref name="limits" /> before the buffer is allocated.
    /// </summary>
    /// <returns>The frame bytes, or <see langword="null" /> when the stream ended cleanly between frames.</returns>
    public static async ValueTask<byte[]?> ReadFrameBytesAsync(Stream stream, RawEtwRelayLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        limits ??= RawEtwRelayLimits.Default;

        byte[] prefix = new byte[PrefixLength];
        int read = await ReadFullAsync(stream, prefix, cancellationToken).ConfigureAwait(false);
        if (read == 0)
        {
            return null;
        }

        if (read < PrefixLength)
        {
            throw new RawEtwProtocolException(RawEtwProtocolError.Truncated, "Stream ended inside a frame prefix.");
        }

        uint total = ValidatePrefix(prefix, limits);
        byte[] frame = new byte[total];
        prefix.CopyTo(frame, 0);

        int remaining = (int)total - PrefixLength;
        if (remaining > 0 &&
            await ReadFullAsync(stream, frame.AsMemory(PrefixLength, remaining), cancellationToken)
                .ConfigureAwait(false) < remaining)
        {
            throw new RawEtwProtocolException(RawEtwProtocolError.Truncated, "Stream ended inside a frame.");
        }

        return frame;
    }

    private static async ValueTask<int> ReadFullAsync(Stream stream, Memory<byte> buffer, CancellationToken token)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int n = await stream.ReadAsync(buffer[total..], token).ConfigureAwait(false);
            if (n == 0)
            {
                break;
            }

            total += n;
        }

        return total;
    }

    private static uint ValidatePrefix(ReadOnlySpan<byte> prefix, RawEtwRelayLimits limits)
    {
        if (!prefix[..4].SequenceEqual(Magic))
        {
            throw new RawEtwProtocolException(RawEtwProtocolError.BadMagic, "Bad frame magic.");
        }

        if (prefix[4] != MajorVersion)
        {
            throw new RawEtwProtocolException(RawEtwProtocolError.UnsupportedVersion,
                $"Unsupported major version {prefix[4]}.");
        }

        uint total = BinaryPrimitives.ReadUInt32LittleEndian(prefix[8..]);
        if (total < PrefixLength || total > (uint)limits.MaxFrameLength || total % 8 != 0)
        {
            throw new RawEtwProtocolException(RawEtwProtocolError.InvalidLength,
                $"Invalid frame length {total} (limit {limits.MaxFrameLength}).");
        }

        if (BinaryPrimitives.ReadUInt32LittleEndian(prefix[12..]) != 0)
        {
            throw new RawEtwProtocolException(RawEtwProtocolError.InvalidField, "Frame flags must be zero.");
        }

        return total;
    }

    private static RawEtwHello ReadHello(ReadOnlySpan<byte> header, ReadOnlySpan<byte> body, RawEtwRelayLimits limits)
    {
        if (header.Length < HelloHeaderLength)
        {
            throw new RawEtwProtocolException(RawEtwProtocolError.InvalidLength, "Hello header too short.");
        }

        if (BinaryPrimitives.ReadUInt32LittleEndian(header[24..]) != 0 ||
            BinaryPrimitives.ReadUInt32LittleEndian(header[28..]) != 0)
        {
            throw new RawEtwProtocolException(RawEtwProtocolError.InvalidField, "Reserved field is not zero.");
        }

        ReadOnlySpan<byte> text = body.TrimEnd((byte)0);
        if (text.Length > limits.MaxProducerStringBytes)
        {
            throw new RawEtwProtocolException(RawEtwProtocolError.InvalidLength, "Producer string is too long.");
        }

        return new RawEtwHello
        {
            SessionId = ReadGuid(header),
            Capabilities = BinaryPrimitives.ReadUInt32LittleEndian(header[16..]),
            MaxFrameLength = BinaryPrimitives.ReadUInt32LittleEndian(header[20..]),
            // Invalid UTF-8 is replaced rather than rejected; the string is diagnostic only.
            Producer = Encoding.UTF8.GetString(text)
        };
    }

    private static RawEtwRecord ReadEvent(ReadOnlySpan<byte> h, ReadOnlySpan<byte> body, int extCount,
        RawEtwRelayLimits limits)
    {
        if (h.Length < EventHeaderLength)
        {
            throw new RawEtwProtocolException(RawEtwProtocolError.InvalidLength, "Event header too short.");
        }

        RawEtwTimestampKind kind = (RawEtwTimestampKind)h[84];
        byte pointerSize = h[85];
        byte wppValid = h[86];
        if (kind != RawEtwTimestampKind.FileTime || pointerSize is not (4 or 8) || wppValid > 1 || h[87] != 0)
        {
            throw new RawEtwProtocolException(RawEtwProtocolError.InvalidField,
                "Unsupported timestamp kind, pointer size or reserved field.");
        }

        uint payloadLength = BinaryPrimitives.ReadUInt32LittleEndian(h[92..]);
        if (payloadLength > (uint)limits.MaxPayloadLength || payloadLength > ushort.MaxValue)
        {
            throw new RawEtwProtocolException(RawEtwProtocolError.InvalidLength,
                $"Payload length {payloadLength} exceeds the limit.");
        }

        if (extCount > limits.MaxExtendedItems)
        {
            throw new RawEtwProtocolException(RawEtwProtocolError.InvalidLength,
                $"{extCount} extended data items exceed the limit of {limits.MaxExtendedItems}.");
        }

        RawEtwRecord record = new()
        {
            Size = BinaryPrimitives.ReadUInt16LittleEndian(h),
            HeaderType = BinaryPrimitives.ReadUInt16LittleEndian(h[2..]),
            Flags = BinaryPrimitives.ReadUInt16LittleEndian(h[4..]),
            EventProperty = BinaryPrimitives.ReadUInt16LittleEndian(h[6..]),
            ThreadId = BinaryPrimitives.ReadUInt32LittleEndian(h[8..]),
            ProcessId = BinaryPrimitives.ReadUInt32LittleEndian(h[12..]),
            TimeStamp = BinaryPrimitives.ReadInt64LittleEndian(h[16..]),
            ProviderId = ReadGuid(h[24..]),
            Id = BinaryPrimitives.ReadUInt16LittleEndian(h[40..]),
            Version = h[42],
            Channel = h[43],
            Level = h[44],
            Opcode = h[45],
            Task = BinaryPrimitives.ReadUInt16LittleEndian(h[46..]),
            Keyword = BinaryPrimitives.ReadUInt64LittleEndian(h[48..]),
            ProcessorTime = BinaryPrimitives.ReadUInt64LittleEndian(h[56..]),
            ActivityId = ReadGuid(h[64..]),
            BufferContext = BinaryPrimitives.ReadUInt32LittleEndian(h[80..]),
            TimestampKind = kind,
            PointerSize = pointerSize,
            WppSequenceValid = wppValid == 1,
            WppSequenceNumber = BinaryPrimitives.ReadUInt32LittleEndian(h[88..])
        };

        int offset = 0;
        int paddedPayload = Align8((int)payloadLength);
        if (paddedPayload > body.Length)
        {
            throw new RawEtwProtocolException(RawEtwProtocolError.InvalidLength, "Payload exceeds the frame.");
        }

        record.Payload = body.Slice(0, (int)payloadLength).ToArray();
        offset += paddedPayload;

        long totalExtended = 0;
        for (int i = 0; i < extCount; i++)
        {
            if (body.Length - offset < ExtendedItemHeaderLength)
            {
                throw new RawEtwProtocolException(RawEtwProtocolError.InvalidLength,
                    "Extended data item header exceeds the frame.");
            }

            ReadOnlySpan<byte> item = body[offset..];
            int dataSize = BinaryPrimitives.ReadUInt16LittleEndian(item[6..]);
            if (dataSize > limits.MaxExtendedItemSize)
            {
                throw new RawEtwProtocolException(RawEtwProtocolError.InvalidLength,
                    $"Extended data item of {dataSize} bytes exceeds the limit.");
            }

            totalExtended += dataSize;
            if (totalExtended > limits.MaxTotalExtendedBytes)
            {
                throw new RawEtwProtocolException(RawEtwProtocolError.InvalidLength,
                    "Extended data exceeds the aggregate limit.");
            }

            int padded = Align8(dataSize);
            if (body.Length - offset - ExtendedItemHeaderLength < padded)
            {
                throw new RawEtwProtocolException(RawEtwProtocolError.InvalidLength,
                    "Extended data item exceeds the frame.");
            }

            record.ExtendedData.Add(new RawEtwExtendedData
            {
                Reserved1 = BinaryPrimitives.ReadUInt16LittleEndian(item),
                ExtType = BinaryPrimitives.ReadUInt16LittleEndian(item[2..]),
                Linkage = BinaryPrimitives.ReadUInt16LittleEndian(item[4..]),
                Data = item.Slice(ExtendedItemHeaderLength, dataSize).ToArray()
            });
            offset += ExtendedItemHeaderLength + padded;
        }

        if (offset != body.Length)
        {
            throw new RawEtwProtocolException(RawEtwProtocolError.InvalidLength,
                "Frame has bytes left after the last extended data item.");
        }

        return record;
    }

    private static void WriteEvent(Span<byte> h, Span<byte> body, RawEtwRecord r)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(h, r.Size);
        BinaryPrimitives.WriteUInt16LittleEndian(h[2..], r.HeaderType);
        BinaryPrimitives.WriteUInt16LittleEndian(h[4..], r.Flags);
        BinaryPrimitives.WriteUInt16LittleEndian(h[6..], r.EventProperty);
        BinaryPrimitives.WriteUInt32LittleEndian(h[8..], r.ThreadId);
        BinaryPrimitives.WriteUInt32LittleEndian(h[12..], r.ProcessId);
        BinaryPrimitives.WriteInt64LittleEndian(h[16..], r.TimeStamp);
        WriteGuid(h[24..], r.ProviderId);
        BinaryPrimitives.WriteUInt16LittleEndian(h[40..], r.Id);
        h[42] = r.Version;
        h[43] = r.Channel;
        h[44] = r.Level;
        h[45] = r.Opcode;
        BinaryPrimitives.WriteUInt16LittleEndian(h[46..], r.Task);
        BinaryPrimitives.WriteUInt64LittleEndian(h[48..], r.Keyword);
        BinaryPrimitives.WriteUInt64LittleEndian(h[56..], r.ProcessorTime);
        WriteGuid(h[64..], r.ActivityId);
        BinaryPrimitives.WriteUInt32LittleEndian(h[80..], r.BufferContext);
        h[84] = (byte)r.TimestampKind;
        h[85] = r.PointerSize;
        h[86] = r.WppSequenceValid ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteUInt32LittleEndian(h[88..], r.WppSequenceNumber);
        BinaryPrimitives.WriteUInt32LittleEndian(h[92..], (uint)r.Payload.Length);

        r.Payload.CopyTo(body);
        int offset = Align8(r.Payload.Length);
        foreach (RawEtwExtendedData ext in r.ExtendedData)
        {
            Span<byte> item = body[offset..];
            BinaryPrimitives.WriteUInt16LittleEndian(item, ext.Reserved1);
            BinaryPrimitives.WriteUInt16LittleEndian(item[2..], ext.ExtType);
            BinaryPrimitives.WriteUInt16LittleEndian(item[4..], ext.Linkage);
            BinaryPrimitives.WriteUInt16LittleEndian(item[6..], (ushort)ext.Data.Length);
            ext.Data.CopyTo(item[ExtendedItemHeaderLength..]);
            offset += ExtendedItemHeaderLength + Align8(ext.Data.Length);
        }
    }

    private static int Align8(int value) => (value + 7) & ~7;

    private static Guid ReadGuid(ReadOnlySpan<byte> bytes) => new(bytes[..16], true);

    private static void WriteGuid(Span<byte> destination, Guid value) => value.TryWriteBytes(destination, true, out _);

    private static uint ComputeCrc(ReadOnlySpan<byte> frame)
    {
        uint crc = Crc32C.Append(0, frame[..28]);
        crc = Crc32C.Append(crc, [0, 0, 0, 0]);
        return Crc32C.Append(crc, frame[32..]);
    }
}
