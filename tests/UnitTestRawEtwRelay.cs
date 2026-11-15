using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

using Nefarius.Utilities.ETW.Deserializer.WPP;
using Nefarius.Utilities.ETW.Events;
using Nefarius.Utilities.ETW.Etl;
using Nefarius.Utilities.ETW.Relay;

using Windows.Win32;
using Windows.Win32.System.Diagnostics.Etw;

namespace Nefarius.Utilities.ETW.Tests;

/// <summary>
///     Tests for the raw ETW relay: wire format, sequencing, and decoding on the receiving side.
/// </summary>
public class RawEtwRelayTests
{
    private static readonly Guid SessionId = new("0f8fad5b-d9cb-469f-a165-70867728950e");
    private static readonly Guid Provider = new("11111111-2222-3333-4444-555555555555");

    // ---------------------------------------------------------------------------------------------------------------
    // Frame builders
    // ---------------------------------------------------------------------------------------------------------------

    private static RawEtwFrame Hello(ulong sequence = 0, uint maxFrame = 65536) => new()
    {
        Type = RawEtwFrameType.Hello,
        Sequence = sequence,
        Hello = new RawEtwHello
        {
            SessionId = SessionId,
            MaxFrameLength = maxFrame,
            Producer = "unit-test"
        }
    };

    private static RawEtwFrame End(ulong sequence) => new() { Type = RawEtwFrameType.End, Sequence = sequence };

    private static RawEtwFrame Loss(ulong sequence, ulong lost) => new()
    {
        Type = RawEtwFrameType.Loss,
        Sequence = sequence,
        Loss = new RawEtwLoss { LostEvents = lost, Reason = 1 }
    };

    private static RawEtwFrame Event(ulong sequence, RawEtwRecord record) => new()
    {
        Type = RawEtwFrameType.Event,
        Sequence = sequence,
        Record = record
    };

    private static RawEtwRecord SampleRecord()
    {
        RawEtwRecord record = new()
        {
            Size = 0x60,
            HeaderType = 0,
            Flags = 0x40,
            EventProperty = 1,
            ThreadId = 0x1111,
            ProcessId = 0x2222,
            TimeStamp = 133_500_000_000_000_000,
            ProviderId = Provider,
            Id = 7,
            Version = 2,
            Channel = 11,
            Level = 4,
            Opcode = 1,
            Task = 9,
            Keyword = 0x8000_0000_0000_0001,
            ProcessorTime = 0x0102030405060708,
            ActivityId = new Guid("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            BufferContext = 0x0001_0003,
            PointerSize = 8,
            Payload = [1, 2, 3, 4, 5]
        };
        record.ExtendedData.Add(new RawEtwExtendedData { Reserved1 = 0, ExtType = 5, Linkage = 1, Data = [9, 8, 7] });
        return record;
    }

    private static byte[] TraceLoggingSchema(string eventName, params byte[][] fields)
    {
        List<byte> body = [0];
        body.AddRange(Encoding.UTF8.GetBytes(eventName));
        body.Add(0);
        foreach (byte[] field in fields)
        {
            body.AddRange(field);
        }

        ushort size = (ushort)(body.Count + 2);
        return [.. BitConverter.GetBytes(size), .. body];
    }

    private static byte[] TraceLoggingField(string name, byte inType, params byte[] rest) =>
        [.. Encoding.UTF8.GetBytes(name), 0, inType, .. rest];

    private static RawEtwRecord TraceLoggingRecord(uint count, string text)
    {
        RawEtwRecord record = new()
        {
            Flags = 0x40,
            ThreadId = 5,
            ProcessId = 6,
            TimeStamp = 133_500_000_000_000_000,
            ProviderId = Provider,
            Level = 3,
            Payload = [.. BitConverter.GetBytes(count), .. Encoding.Unicode.GetBytes(text + "\0")]
        };
        record.ExtendedData.Add(new RawEtwExtendedData
        {
            ExtType = 11,
            Data = TraceLoggingSchema("MyEvent", TraceLoggingField("Count", 8), TraceLoggingField("Name", 1))
        });
        return record;
    }

    private static byte[][] Wire(params RawEtwFrame[] frames) => frames.Select(f => RawEtwFrameCodec.Encode(f)).ToArray();

    private static RawEtwProtocolException AssertRejected(byte[] frame, RawEtwProtocolError expected,
        RawEtwRelayLimits? limits = null)
    {
        RawEtwProtocolException? ex = Assert.Throws<RawEtwProtocolException>(() => RawEtwFrameCodec.Decode(frame, limits));
        Assert.That(ex!.Error, Is.EqualTo(expected));
        return ex;
    }

    private static string Hex(ReadOnlySpan<byte> bytes) => Convert.ToHexString(bytes);

    // ---------------------------------------------------------------------------------------------------------------
    // Wire format
    // ---------------------------------------------------------------------------------------------------------------

    [Test]
    public void Crc32C_MatchesTheStandardCheckValue()
    {
        Assert.That(Crc32C.Append(0, "123456789"u8), Is.EqualTo(0xE3069283u));
        // continuing across segments equals one pass
        uint split = Crc32C.Append(Crc32C.Append(0, "1234"u8), "56789"u8);
        Assert.That(split, Is.EqualTo(0xE3069283u));
    }

    [Test]
    public void Frames_RoundTrip()
    {
        RawEtwRecord record = SampleRecord();
        RawEtwFrame decoded = RawEtwFrameCodec.Decode(RawEtwFrameCodec.Encode(Event(42, record)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(decoded.Type, Is.EqualTo(RawEtwFrameType.Event));
            Assert.That(decoded.Sequence, Is.EqualTo(42UL));
            RawEtwRecord r = decoded.Record!;
            Assert.That(r.Size, Is.EqualTo(record.Size));
            Assert.That(r.Flags, Is.EqualTo(record.Flags));
            Assert.That(r.EventProperty, Is.EqualTo(record.EventProperty));
            Assert.That(r.ThreadId, Is.EqualTo(record.ThreadId));
            Assert.That(r.ProcessId, Is.EqualTo(record.ProcessId));
            Assert.That(r.TimeStamp, Is.EqualTo(record.TimeStamp));
            Assert.That(r.ProviderId, Is.EqualTo(record.ProviderId));
            Assert.That((r.Id, r.Version, r.Channel, r.Level, r.Opcode, r.Task),
                Is.EqualTo((record.Id, record.Version, record.Channel, record.Level, record.Opcode, record.Task)));
            Assert.That(r.Keyword, Is.EqualTo(record.Keyword));
            Assert.That(r.ProcessorTime, Is.EqualTo(record.ProcessorTime));
            Assert.That(r.ActivityId, Is.EqualTo(record.ActivityId));
            Assert.That(r.BufferContext, Is.EqualTo(record.BufferContext));
            Assert.That(r.Payload, Is.EqualTo(record.Payload));
            Assert.That(r.ExtendedData, Has.Count.EqualTo(1));
            Assert.That(r.ExtendedData[0].ExtType, Is.EqualTo(5));
            Assert.That(r.ExtendedData[0].Linkage, Is.EqualTo(1));
            Assert.That(r.ExtendedData[0].Data, Is.EqualTo(new byte[] { 9, 8, 7 }));
        }

        RawEtwFrame hello = RawEtwFrameCodec.Decode(RawEtwFrameCodec.Encode(Hello()));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(hello.Hello!.SessionId, Is.EqualTo(SessionId));
            Assert.That(hello.Hello.Producer, Is.EqualTo("unit-test"));
            Assert.That(hello.Hello.MaxFrameLength, Is.EqualTo(65536u));
        }

        RawEtwFrame loss = RawEtwFrameCodec.Decode(RawEtwFrameCodec.Encode(Loss(3, 17)));
        Assert.That((loss.Loss!.LostEvents, loss.Loss.Reason), Is.EqualTo((17UL, 1u)));
    }

    [Test]
    public void EveryFrame_IsAMultipleOfEightBytes()
    {
        RawEtwRecord odd = SampleRecord();
        odd.Payload = new byte[13];
        odd.ExtendedData[0].Data = new byte[3];

        foreach (RawEtwFrame frame in new[] { Hello(), Event(1, odd), Loss(2, 1), End(3) })
        {
            Assert.That(RawEtwFrameCodec.Encode(frame).Length % 8, Is.Zero, frame.Type.ToString());
        }
    }

    [Test]
    public void GuidsUseRfc4122ByteOrder()
    {
        byte[] bytes = RawEtwFrameCodec.Encode(Event(1, SampleRecord()), false);

        // ProviderId sits at prefix(32) + 24. "11111111-2222-3333-4444-555555555555" read left to right.
        Assert.That(Hex(bytes.AsSpan(32 + 24, 16)), Is.EqualTo("11111111222233334444555555555555"));
    }

    /// <summary>
    ///     Pins the exact bytes of version 1 so a C++ producer can be checked against the same vector.
    /// </summary>
    [Test]
    public void GoldenVector_EventFrame_IsStable()
    {
        byte[] frame = RawEtwFrameCodec.Encode(Event(42, SampleRecord()));
        Assert.That(Hex(frame), Is.EqualTo(GoldenEventFrameHex));
        Assert.That(RawEtwFrameCodec.Decode(Convert.FromHexString(GoldenEventFrameHex)).Record!.Id, Is.EqualTo(7));
    }

    [Test]
    public void GoldenVector_HelloFrame_IsStable()
    {
        Assert.That(Hex(RawEtwFrameCodec.Encode(Hello())), Is.EqualTo(GoldenHelloFrameHex));
    }

    private const string GoldenEventFrameHex =
        "4E4552310100010098000000000000002A0000000000000060000100C2C42CD66000000040000100111100002222000000C083ED8A49DA01111111112222333344445555555555550700020B0401090001000000000000800807060504030201AAAAAAAABBBBCCCCDDDDEEEEEEEEEEEE03000100010800000000000005000000010203040500000000000500010003000908070000000000";
    private const string GoldenHelloFrameHex =
        "4E455231010000005000000000000000000000000000000020000000D8F619690F8FAD5BD9CB469FA16570867728950E00000000000001000000000000000000756E69742D7465737400000000000000";

    // ---------------------------------------------------------------------------------------------------------------
    // Malformed input
    // ---------------------------------------------------------------------------------------------------------------

    [Test]
    public void Decode_RejectsMalformedPrefix()
    {
        byte[] good = RawEtwFrameCodec.Encode(Event(1, SampleRecord()), false);

        byte[] badMagic = (byte[])good.Clone();
        badMagic[0] = (byte)'X';
        AssertRejected(badMagic, RawEtwProtocolError.BadMagic);

        byte[] badVersion = (byte[])good.Clone();
        badVersion[4] = 2;
        AssertRejected(badVersion, RawEtwProtocolError.UnsupportedVersion);

        byte[] flags = (byte[])good.Clone();
        flags[12] = 1;
        AssertRejected(flags, RawEtwProtocolError.InvalidField);

        AssertRejected(good[..20], RawEtwProtocolError.Truncated);
        AssertRejected(good[..^8], RawEtwProtocolError.Truncated);
        AssertRejected([.. good, .. new byte[8]], RawEtwProtocolError.InvalidLength);

        byte[] unaligned = (byte[])good.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(unaligned.AsSpan(8), (uint)good.Length - 1);
        AssertRejected(unaligned[..^1], RawEtwProtocolError.InvalidLength);
    }

    [Test]
    public void Decode_EnforcesFrameLengthLimit()
    {
        byte[] frame = RawEtwFrameCodec.Encode(Event(1, SampleRecord()));
        AssertRejected(frame, RawEtwProtocolError.InvalidLength, new RawEtwRelayLimits { MaxFrameLength = 64 });
    }

    [Test]
    public void Decode_EnforcesChecksum()
    {
        byte[] frame = RawEtwFrameCodec.Encode(Event(1, SampleRecord()));
        frame[^1] ^= 0xFF;
        AssertRejected(frame, RawEtwProtocolError.ChecksumMismatch);

        byte[] noCrc = RawEtwFrameCodec.Encode(Event(1, SampleRecord()), false);
        Assert.That(RawEtwFrameCodec.Decode(noCrc).Sequence, Is.EqualTo(1UL));
        AssertRejected(noCrc, RawEtwProtocolError.ChecksumMismatch, new RawEtwRelayLimits { RequireCrc = true });
    }

    [Test]
    public void Decode_RejectsInconsistentEventBody()
    {
        byte[] good = RawEtwFrameCodec.Encode(Event(1, SampleRecord()), false);
        int header = RawEtwFrameCodec.PrefixLength;

        // unsupported timestamp kind
        byte[] kind = (byte[])good.Clone();
        kind[header + 84] = 0;
        AssertRejected(kind, RawEtwProtocolError.InvalidField);

        // bad pointer size
        byte[] pointer = (byte[])good.Clone();
        pointer[header + 85] = 3;
        AssertRejected(pointer, RawEtwProtocolError.InvalidField);

        // reserved byte
        byte[] reserved = (byte[])good.Clone();
        reserved[header + 87] = 1;
        AssertRejected(reserved, RawEtwProtocolError.InvalidField);

        // payload length beyond the frame
        byte[] payload = (byte[])good.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(header + 92), 60000);
        AssertRejected(payload, RawEtwProtocolError.InvalidLength);

        // payload length above the configured limit
        AssertRejected(good, RawEtwProtocolError.InvalidLength, new RawEtwRelayLimits { MaxPayloadLength = 4 });

        // declared extended item count above the limit
        AssertRejected(good, RawEtwProtocolError.InvalidLength, new RawEtwRelayLimits { MaxExtendedItems = 0 });

        // extended item larger than its limit, and larger than the aggregate limit
        AssertRejected(good, RawEtwProtocolError.InvalidLength, new RawEtwRelayLimits { MaxExtendedItemSize = 2 });
        AssertRejected(good, RawEtwProtocolError.InvalidLength, new RawEtwRelayLimits { MaxTotalExtendedBytes = 2 });

        // extended item size pointing past the frame
        byte[] ext = (byte[])good.Clone();
        int itemOffset = header + RawEtwFrameCodec.EventHeaderLength + 8; // payload (5) padded to 8
        BinaryPrimitives.WriteUInt16LittleEndian(ext.AsSpan(itemOffset + 6), 4000);
        AssertRejected(ext, RawEtwProtocolError.InvalidLength);

        // extended item count larger than what is present
        byte[] count = (byte[])good.Clone();
        BinaryPrimitives.WriteUInt16LittleEndian(count.AsSpan(26), 3);
        AssertRejected(count, RawEtwProtocolError.InvalidLength);

        // bytes left after the last item
        RawEtwRecord withExtra = SampleRecord();
        byte[] extra = RawEtwFrameCodec.Encode(Event(1, withExtra), false);
        BinaryPrimitives.WriteUInt16LittleEndian(extra.AsSpan(26), 0);
        AssertRejected(extra, RawEtwProtocolError.InvalidLength);
    }

    [Test]
    public void Decode_RejectsExtendedDataOnNonEventFrames()
    {
        byte[] frame = RawEtwFrameCodec.Encode(End(1), false);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(26), 1);
        AssertRejected(frame, RawEtwProtocolError.InvalidField);
    }

    [Test]
    public void Decode_ToleratesUnknownFrameTypesAndLargerHeaders()
    {
        byte[] frame = RawEtwFrameCodec.Encode(End(5), false);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(6), 99);
        RawEtwFrame decoded = RawEtwFrameCodec.Decode(frame);
        Assert.That((ushort)decoded.Type, Is.EqualTo(99));
    }

    [Test]
    public void Fuzz_DecodeNeverThrowsAnythingButProtocolExceptions()
    {
        Random random = new(1234);
        byte[] seed = RawEtwFrameCodec.Encode(Event(1, SampleRecord()));

        for (int i = 0; i < 5000; i++)
        {
            byte[] mutated = (byte[])seed.Clone();
            int edits = random.Next(1, 6);
            for (int e = 0; e < edits; e++)
            {
                mutated[random.Next(mutated.Length)] = (byte)random.Next(256);
            }

            if (random.Next(4) == 0)
            {
                mutated = mutated[..random.Next(mutated.Length)];
            }

            try
            {
                RawEtwFrameCodec.Decode(mutated);
            }
            catch (RawEtwProtocolException)
            {
            }
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Streams
    // ---------------------------------------------------------------------------------------------------------------

    private sealed class TrickleStream(byte[] data, int chunk) : Stream
    {
        private int _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = Math.Min(Math.Min(count, chunk), data.Length - _position);
            Array.Copy(data, _position, buffer, offset, n);
            _position += n;
            return n;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Test]
    public async Task ReadFrameAsync_ReassemblesFramesFromTinyReads()
    {
        byte[] stream = Wire(Hello(), Event(1, SampleRecord()), End(2)).SelectMany(b => b).ToArray();
        using TrickleStream input = new(stream, 3);

        List<RawEtwFrameType> types = [];
        while (await RawEtwFrameCodec.ReadFrameAsync(input) is { } frame)
        {
            types.Add(frame.Type);
        }

        Assert.That(types, Is.EqualTo(new[] { RawEtwFrameType.Hello, RawEtwFrameType.Event, RawEtwFrameType.End }));
    }

    [Test]
    public void ReadFrameAsync_RejectsTruncatedStreams()
    {
        byte[] frame = RawEtwFrameCodec.Encode(Event(1, SampleRecord()));

        using MemoryStream midPrefix = new(frame[..10]);
        Assert.That(async () => await RawEtwFrameCodec.ReadFrameAsync(midPrefix),
            NUnit.Framework.Throws.TypeOf<RawEtwProtocolException>().With.Property("Error").EqualTo(RawEtwProtocolError.Truncated));

        using MemoryStream midBody = new(frame[..^16]);
        Assert.That(async () => await RawEtwFrameCodec.ReadFrameAsync(midBody),
            NUnit.Framework.Throws.TypeOf<RawEtwProtocolException>().With.Property("Error").EqualTo(RawEtwProtocolError.Truncated));
    }

    [Test]
    public void ReadFrameAsync_RejectsHugeLengthBeforeAllocating()
    {
        byte[] prefix = new byte[RawEtwFrameCodec.PrefixLength];
        "NER1"u8.CopyTo(prefix);
        prefix[4] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(prefix.AsSpan(8), 0x7FFF_FFF8);

        using MemoryStream stream = new(prefix);
        Assert.That(async () => await RawEtwFrameCodec.ReadFrameAsync(stream),
            NUnit.Framework.Throws.TypeOf<RawEtwProtocolException>().With.Property("Error").EqualTo(RawEtwProtocolError.InvalidLength));
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Sequencing
    // ---------------------------------------------------------------------------------------------------------------

    private static RawEtwFrameDisposition Feed(RawEtwRelayDecoder decoder, RawEtwFrame frame, List<EtwEvent>? events = null)
    {
        return decoder.Process(RawEtwFrameCodec.Encode(frame), events is null ? _ => { } : events.Add);
    }

    private static void AssertSessionError(RawEtwRelayDecoder decoder, RawEtwFrame frame, RawEtwProtocolError expected)
    {
        RawEtwProtocolException? ex = Assert.Throws<RawEtwProtocolException>(() => Feed(decoder, frame));
        Assert.That(ex!.Error, Is.EqualTo(expected));
    }

    [Test]
    public void Session_RequiresHelloFirst()
    {
        using RawEtwRelayDecoder decoder = new();
        AssertSessionError(decoder, Event(0, SampleRecord()), RawEtwProtocolError.HelloRequired);
        Assert.That(decoder.NextSequence, Is.Zero);
    }

    [Test]
    public void Session_AcceptsHelloEventsLossAndEnd()
    {
        int hellos = 0;
        List<RawEtwLoss> losses = [];
        using RawEtwRelayDecoder decoder = new(null, new RawEtwRelayOptions
        {
            OnHello = _ => hellos++,
            OnLoss = losses.Add
        });

        Assert.That(Feed(decoder, Hello()), Is.EqualTo(RawEtwFrameDisposition.Hello));
        Assert.That(decoder.SessionId, Is.EqualTo(SessionId));
        Assert.That(Feed(decoder, Loss(1, 5)), Is.EqualTo(RawEtwFrameDisposition.Loss));
        Assert.That(Feed(decoder, Loss(2, 7)), Is.EqualTo(RawEtwFrameDisposition.Loss));
        Assert.That(Feed(decoder, End(3)), Is.EqualTo(RawEtwFrameDisposition.End));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(hellos, Is.EqualTo(1));
            Assert.That(losses.Select(l => l.LostEvents), Is.EqualTo(new ulong[] { 5, 7 }));
            Assert.That(decoder.LostEvents, Is.EqualTo(12UL));
            Assert.That(decoder.Ended, Is.True);
            Assert.That(decoder.NextSequence, Is.EqualTo(4UL));
        }

        AssertSessionError(decoder, End(4), RawEtwProtocolError.AfterEnd);
    }

    [Test]
    public void Session_RejectsGapsAndConflictingReplays()
    {
        using RawEtwRelayDecoder decoder = new();
        Feed(decoder, Hello());

        AssertSessionError(decoder, Loss(2, 1), RawEtwProtocolError.OutOfOrder);

        Feed(decoder, Loss(1, 1));

        // same sequence, different content
        AssertSessionError(decoder, Loss(1, 99), RawEtwProtocolError.ConflictingReplay);
        Assert.That(decoder.LostEvents, Is.EqualTo(1UL));

        // a second hello
        AssertSessionError(decoder, Hello(2), RawEtwProtocolError.UnexpectedHello);
    }

    [Test]
    public void Session_ToleratesExactRetries()
    {
        using RawEtwRelayDecoder decoder = new();
        Feed(decoder, Hello());
        Feed(decoder, Loss(1, 4));

        byte[] retry = RawEtwFrameCodec.Encode(Loss(1, 4));
        RawEtwFrameDisposition disposition = decoder.Process(retry, _ => { });
        Assert.That(disposition, Is.EqualTo(RawEtwFrameDisposition.Duplicate));
        Assert.That(decoder.LostEvents, Is.EqualTo(4UL), "A retry must not be applied twice.");

        // the hello itself can be retried as well
        Assert.That(Feed(decoder, Hello()), Is.EqualTo(RawEtwFrameDisposition.Duplicate));
        Assert.That(decoder.NextSequence, Is.EqualTo(2UL));
    }

    [Test]
    public void Session_CannotVerifyRetriesBeyondTheWindow()
    {
        using RawEtwRelayDecoder decoder = new();
        Feed(decoder, Hello());
        for (ulong i = 1; i <= 100; i++)
        {
            Feed(decoder, Loss(i, 1));
        }

        Assert.That(Feed(decoder, Loss(99, 1)), Is.EqualTo(RawEtwFrameDisposition.Duplicate));
        AssertSessionError(decoder, Loss(5, 1), RawEtwProtocolError.ConflictingReplay);
    }

    [Test]
    public void Session_RejectsProducersAboveTheFrameLimit()
    {
        using RawEtwRelayDecoder decoder = new(null,
            new RawEtwRelayOptions { Limits = new RawEtwRelayLimits { MaxFrameLength = 4096 } });
        AssertSessionError(decoder, Hello(0, 1 << 20), RawEtwProtocolError.ProducerLimitExceeded);
        Assert.That(decoder.SessionId, Is.Null);
    }

    [Test]
    public void Session_SkipsUnknownFrameTypesButKeepsSequencing()
    {
        using RawEtwRelayDecoder decoder = new();
        Feed(decoder, Hello());

        byte[] unknown = RawEtwFrameCodec.Encode(End(1), false);
        BinaryPrimitives.WriteUInt16LittleEndian(unknown.AsSpan(6), 99);
        Assert.That(decoder.Process(unknown, _ => { }), Is.EqualTo(RawEtwFrameDisposition.Ignored));
        Assert.That(decoder.NextSequence, Is.EqualTo(2UL));
        Assert.That(Feed(decoder, Loss(2, 1)), Is.EqualTo(RawEtwFrameDisposition.Loss));
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Decoding
    // ---------------------------------------------------------------------------------------------------------------

    [Test]
    public void TraceLogging_IsDecodedFromRelayedSchema()
    {
        using RawEtwRelayDecoder decoder = new();
        List<EtwEvent> events = [];
        Feed(decoder, Hello(), events);
        Feed(decoder, Event(1, TraceLoggingRecord(42, "héllo")), events);

        Assert.That(events, Has.Count.EqualTo(1));
        EtwEvent e = events[0];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(e.Name, Is.EqualTo("MyEvent"));
            Assert.That(e.ProviderId, Is.EqualTo(Provider));
            Assert.That(e.ProcessId, Is.EqualTo(6u));
            Assert.That(e.ThreadId, Is.EqualTo(5u));
            Assert.That(e.Level, Is.EqualTo(3));
            Assert.That(e.TimestampUtc, Is.EqualTo(DateTime.FromFileTimeUtc(133_500_000_000_000_000)));
            Assert.That(e.Properties.Kind, Is.EqualTo(EtwValueKind.Object));
            Assert.That(e.Properties.Members.Select(m => m.Key), Is.EqualTo(new[] { "Count", "Name" }));
            Assert.That(e.Properties.Members[0].Value.AsUInt64, Is.EqualTo(42UL));
            Assert.That(e.Properties.Members[1].Value.AsString, Is.EqualTo("héllo"));
        }
    }

    [Test]
    public async Task TraceLogging_JsonStreamMatchesStructuredOutput()
    {
        byte[] stream = Wire(Hello(), Event(1, TraceLoggingRecord(7, "x")), Event(2, TraceLoggingRecord(8, "y")), End(3))
            .SelectMany(b => b).ToArray();

        using MemoryStream input = new(stream);
        List<string> names = [];
        List<uint> counts = [];
        await foreach (ReadOnlyMemory<byte> json in EtwUtil.EnumerateRelayEventsAsync(input))
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement e = doc.RootElement.GetProperty("Event");
            names.Add(e.GetProperty("Name").GetString()!);
            counts.Add(e.GetProperty("Properties")[0].GetProperty("Count").GetUInt32());
        }

        Assert.That(names, Is.EqualTo(new[] { "MyEvent", "MyEvent" }));
        Assert.That(counts, Is.EqualTo(new uint[] { 7, 8 }));
    }

    [Test]
    public async Task DecodeAsync_StopsAtEndFrameAndReportsIt()
    {
        byte[] stream = Wire(Hello(), Event(1, TraceLoggingRecord(7, "x")), End(2), Event(3, TraceLoggingRecord(9, "z")))
            .SelectMany(b => b).ToArray();

        using MemoryStream input = new(stream);
        using RawEtwRelayDecoder decoder = new();
        List<EtwEvent> events = [];
        await foreach (EtwEvent e in decoder.DecodeAsync(input))
        {
            events.Add(e);
        }

        Assert.That(events, Has.Count.EqualTo(1));
        Assert.That(decoder.Ended, Is.True);
    }

    [Test]
    public async Task DecodeAsync_ReportsAStreamThatEndedWithoutEndFrame()
    {
        byte[] stream = Wire(Hello(), Event(1, TraceLoggingRecord(7, "x"))).SelectMany(b => b).ToArray();

        using MemoryStream input = new(stream);
        using RawEtwRelayDecoder decoder = new();
        int count = 0;
        await foreach (EtwEvent _ in decoder.DecodeAsync(input))
        {
            count++;
        }

        Assert.That(count, Is.EqualTo(1));
        Assert.That(decoder.Ended, Is.False);
    }

    [Test]
    public void DecodeFailure_ConsumesTheFrameAndKeepsTheSessionUsable()
    {
        using RawEtwRelayDecoder decoder = new();
        Feed(decoder, Hello());

        // a TraceLogging schema that claims more payload than exists
        RawEtwRecord broken = TraceLoggingRecord(1, "x");
        broken.Payload = [1];

        List<EtwEvent> events = [];
        try
        {
            Feed(decoder, Event(1, broken), events);
        }
        catch (RawEtwProtocolException ex)
        {
            Assert.That(ex.Error, Is.EqualTo(RawEtwProtocolError.DecodeFailed));
        }

        Assert.That(decoder.NextSequence, Is.EqualTo(2UL));
        Feed(decoder, Event(2, TraceLoggingRecord(5, "ok")), events);
        Assert.That(events.Select(e => e.Name), Does.Contain("MyEvent"));
    }

    [Test]
    public void Fuzz_RelayedRecordsNeverEscapeAsOtherExceptions()
    {
        Random random = new(98765);
        using RawEtwRelayDecoder decoder = new();
        Feed(decoder, Hello());

        ulong sequence = 1;
        for (int i = 0; i < 3000; i++)
        {
            RawEtwRecord record = TraceLoggingRecord(1, "fuzz");
            record.Flags = (ushort)(random.Next(4) == 0 ? 0x08 : record.Flags);
            record.Payload = new byte[random.Next(0, 64)];
            random.NextBytes(record.Payload);
            record.ExtendedData[0].ExtType = (ushort)(random.Next(3) == 0 ? random.Next(0, 20) : 11);
            record.ExtendedData[0].Data = new byte[random.Next(0, 96)];
            random.NextBytes(record.ExtendedData[0].Data);

            try
            {
                Feed(decoder, Event(sequence++, record));
            }
            catch (RawEtwProtocolException ex)
            {
                Assert.That(ex.Error, Is.EqualTo(RawEtwProtocolError.DecodeFailed));
            }
        }
    }

    [Test]
    public unsafe void StackTraces_AreReadWithoutMutatingTheRecord()
    {
        byte[] data = [.. BitConverter.GetBytes(0xABCDUL), .. BitConverter.GetBytes(0x10UL), .. BitConverter.GetBytes(0x20UL)];

        fixed (byte* dataPtr = data)
        {
            EVENT_HEADER_EXTENDED_DATA_ITEM item = default;
            item.ExtType = (ushort)PInvoke.EVENT_HEADER_EXT_TYPE_STACK_TRACE64;
            item.DataSize = (ushort)data.Length;
            item.DataPtr = (ulong)dataPtr;

            EVENT_RECORD record = default;
            record.ExtendedDataCount = 1;
            record.ExtendedData = &item;

            Nefarius.Utilities.ETW.Deserializer.RuntimeEventMetadata metadata = new(&record);
            ulong[]? first = metadata.GetStacks(out ulong firstId);
            ulong[]? second = metadata.GetStacks(out ulong secondId);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(first, Is.EqualTo(new ulong[] { 0x10, 0x20 }));
                Assert.That(second, Is.EqualTo(first));
                Assert.That((firstId, secondId), Is.EqualTo((0xABCDUL, 0xABCDUL)));
                Assert.That(item.DataPtr, Is.EqualTo((ulong)dataPtr), "The item must not be advanced.");
            }
        }
    }

    [Test]
    public unsafe void StackTraces_IgnoreNullAndShortItems()
    {
        EVENT_HEADER_EXTENDED_DATA_ITEM nullItem = default;
        nullItem.ExtType = (ushort)PInvoke.EVENT_HEADER_EXT_TYPE_STACK_TRACE64;
        nullItem.DataSize = 64;

        EVENT_RECORD record = default;
        record.ExtendedDataCount = 1;
        record.ExtendedData = &nullItem;

        Nefarius.Utilities.ETW.Deserializer.RuntimeEventMetadata metadata = new(&record);
        Assert.That(metadata.GetStacks(out ulong id), Is.Null);
        Assert.That(id, Is.Zero);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Parity with the ETL decoders
    // ---------------------------------------------------------------------------------------------------------------

    private static unsafe List<RawEtwFrame> FramesFromEtl(string path)
    {
        List<RawEtwFrame> frames = [Hello(maxFrame: 1 << 20)];
        ulong sequence = 1;

        TraceOpenFailure? failure = TraceProcessor.ProcessFiles([path], EtwBackend.Managed, false, default, null,
            (EVENT_RECORD* record, in EtlRecordExtras extras) =>
                frames.Add(Event(sequence++, RelayRecordBuffer.ToRelayRecord(record, extras))));
        Assert.That(failure, Is.Null);

        frames.Add(End(sequence));
        return frames;
    }

    private static DecodingContext WppContext() => new(PdbFileDecodingContextType.CreateFrom(
        "./symbols/BthPS3.pdb", "./symbols/BthPS3PSM.pdb", "./symbols/dshidmini.pdb"));

    [TestCase("./traces/BthPS3_0.etl")]
    [TestCase("./traces/DsHidMini.etl")]
    [TestCase("./traces/fixtures/manifest-x64.etl")]
    [TestCase("./traces/fixtures/tracelogging-x64.etl")]
    [TestCase("./traces/fixtures/kernel-classic.etl")]
    [Category("EndToEnd")]
    public async Task RelayedRecords_DecodeIdenticallyToTheManagedEtlBackend(string path)
    {
        if (!File.Exists(path))
        {
            Assert.Ignore($"{path} is not available.");
        }

        DecodingContext context = WppContext();

        List<string> expected = [];
        await foreach (ReadOnlyMemory<byte> json in EtwUtil.EnumerateEventsAsync([path], o =>
                       {
                           o.Backend = EtwBackend.Managed;
                           o.WppDecodingContext = context;
                       }))
        {
            expected.Add(Encoding.UTF8.GetString(json.Span));
        }

        byte[] stream = FramesFromEtl(path).SelectMany(f => RawEtwFrameCodec.Encode(f)).ToArray();
        using MemoryStream input = new(stream);

        List<string> actual = [];
        await foreach (ReadOnlyMemory<byte> json in EtwUtil.EnumerateRelayEventsAsync(input,
                           o => o.WppDecodingContext = context))
        {
            actual.Add(Encoding.UTF8.GetString(json.Span));
        }

        Assert.That(expected, Is.Not.Empty);
        Assert.That(actual, Has.Count.EqualTo(expected.Count));
        for (int i = 0; i < expected.Count; i++)
        {
            if (expected[i] != actual[i])
            {
                Assert.Fail($"Event {i} differs.\nEtl:   {expected[i]}\nRelay: {actual[i]}");
            }
        }
    }

    [Test]
    [Category("EndToEnd")]
    public async Task WppEvents_AreFormattedFromServerSideSymbolsOnly()
    {
        // The relayed frames carry no symbol information at all: only raw records.
        List<RawEtwFrame> frames = FramesFromEtl("./traces/BthPS3_0.etl");
        Assert.That(frames.Any(f => f.Record is { } r && (r.Flags & 0x08) != 0), Is.True);

        using MemoryStream input = new(frames.SelectMany(f => RawEtwFrameCodec.Encode(f)).ToArray());

        using RawEtwRelayDecoder withSymbols = new(o => o.WppDecodingContext = WppContext());
        string? formatted = null;
        await foreach (EtwEvent e in withSymbols.DecodeAsync(input))
        {
            if (e.Name == "WPP")
            {
                string text = e.Properties.Members.First(m => m.Key == "FormattedString").Value.AsString;
                if (!text.StartsWith("GUID="))
                {
                    formatted = text;
                    break;
                }
            }
        }

        Assert.That(formatted, Is.Not.Null.And.Not.Empty);

        // without the server-side context the same stream only yields the placeholder
        using MemoryStream again = new(frames.SelectMany(f => RawEtwFrameCodec.Encode(f)).ToArray());
        int missing = 0;
        using RawEtwRelayDecoder withoutSymbols = new(o => o.OnWppFormatMissing = (_, _, _) => missing++);
        await foreach (EtwEvent _ in withoutSymbols.DecodeAsync(again))
        {
        }

        Assert.That(missing, Is.GreaterThan(0));
    }
}
