using System.Runtime.InteropServices;
using System.Threading.Channels;

using Nefarius.Utilities.ETW.Relay;

using Windows.Win32.System.Diagnostics.Etw;

namespace Nefarius.Utilities.ETW.Tests;

/// <summary>
///     Exercises the capture-thread logic of the realtime relay producer without an ETW session, so it runs on every
///     platform.
/// </summary>
public unsafe class RealtimeRelayProducerUnitTests
{
    private static RawEtwRecord Sample(int payloadLength = 6) => new()
    {
        Size = 80,
        ProviderId = Guid.Parse("22FB2CD6-0E7B-422B-A0C7-2FAD1FD0E716"),
        Id = 1,
        Level = 4,
        TimeStamp = 133_000_000_000_000_000,
        Payload = Enumerable.Range(1, payloadLength).Select(i => (byte)i).ToArray()
    };

    private static (EtwUtil.RealtimeRelayProducer Producer, Channel<byte[]> Channel, CancellationTokenSource Stop)
        Create(Action<RawEtwRelayProducerOptions>? configure = null)
    {
        RawEtwRelayProducerOptions options = new();
        configure?.Invoke(options);
        Channel<byte[]> channel = Channel.CreateUnbounded<byte[]>();
        CancellationTokenSource stop = new();
        return (new EtwUtil.RealtimeRelayProducer(channel.Writer, options, stop.Token, CancellationToken.None),
            channel, stop);
    }

    private static List<RawEtwFrame> Drain(Channel<byte[]> channel)
    {
        List<RawEtwFrame> frames = [];
        while (channel.Reader.TryRead(out byte[]? bytes))
        {
            frames.Add(RawEtwFrameCodec.Decode(bytes));
        }

        return frames;
    }

    private static void Deliver(EtwUtil.RealtimeRelayProducer producer, RawEtwRecord record)
    {
        using RelayRecordBuffer buffer = new();
        producer.OnRecord(buffer.Fill(record, out _));
    }

    private static void Buffer(EtwUtil.RealtimeRelayProducer producer, uint headerLost, uint logfileLost,
        out bool keepGoing)
    {
        int size = Math.Max(EtwUtil.RealtimeRelayProducer.HeaderLostOffset,
            EtwUtil.RealtimeRelayProducer.LogfileLostOffset) + 16;
        IntPtr logfile = Marshal.AllocHGlobal(size);
        try
        {
            new Span<byte>((void*)logfile, size).Clear();
            Marshal.WriteInt32(logfile, EtwUtil.RealtimeRelayProducer.HeaderLostOffset, unchecked((int)headerLost));
            Marshal.WriteInt32(logfile, EtwUtil.RealtimeRelayProducer.LogfileLostOffset, unchecked((int)logfileLost));
            keepGoing = producer.OnBuffer(logfile);
        }
        finally
        {
            Marshal.FreeHGlobal(logfile);
        }
    }

    [Test]
    public void Records_AreQueuedInOrderWithContiguousSequences()
    {
        (EtwUtil.RealtimeRelayProducer producer, Channel<byte[]> channel, CancellationTokenSource stop) = Create();
        using CancellationTokenSource stopScope = stop;

        for (ushort i = 1; i <= 3; i++)
        {
            RawEtwRecord record = Sample();
            record.Id = i;
            Deliver(producer, record);
        }

        List<RawEtwFrame> frames = Drain(channel);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(frames.Select(f => f.Type), Is.All.EqualTo(RawEtwFrameType.Event));
            Assert.That(frames.Select(f => f.Sequence), Is.EqualTo(new ulong[] { 0, 1, 2 }));
            Assert.That(frames.Select(f => f.Record!.Id), Is.EqualTo(new ushort[] { 1, 2, 3 }));
            Assert.That(frames[0].Record!.Payload, Is.EqualTo(Sample().Payload));
        }
    }

    [Test]
    public void Records_AreCopiedInsideTheCallback()
    {
        (EtwUtil.RealtimeRelayProducer producer, Channel<byte[]> channel, CancellationTokenSource stop) = Create();
        using CancellationTokenSource stopScope = stop;

        using (RelayRecordBuffer buffer = new())
        {
            EVENT_RECORD* native = buffer.Fill(Sample(), out _);
            producer.OnRecord(native);
            new Span<byte>(native->UserData, native->UserDataLength).Fill(0xEE);
        }

        RawEtwFrame frame = Drain(channel).Single();

        Assert.That(frame.Record!.Payload, Is.EqualTo(Sample().Payload), "Later native mutation must not leak in.");
    }

    [Test]
    public void LossCounters_AreCumulativeAndReportedAsPositiveDeltas()
    {
        (EtwUtil.RealtimeRelayProducer producer, Channel<byte[]> channel, CancellationTokenSource stop) = Create();
        using CancellationTokenSource stopScope = stop;

        Buffer(producer, 0, 0, out bool first);
        Buffer(producer, 3, 0, out _);
        Buffer(producer, 3, 0, out _);
        Deliver(producer, Sample());
        Buffer(producer, 3, 10, out _);
        Buffer(producer, 2, 0, out _);

        List<RawEtwFrame> frames = Drain(channel);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.True);
            Assert.That(frames.Select(f => f.Type), Is.EqualTo(new[]
            {
                RawEtwFrameType.Loss, RawEtwFrameType.Event, RawEtwFrameType.Loss
            }));
            Assert.That(frames[0].Loss!.LostEvents, Is.EqualTo(3UL));
            Assert.That(frames[0].Loss!.Reason, Is.EqualTo(2u));
            Assert.That(frames[2].Loss!.LostEvents, Is.EqualTo(7UL));
            Assert.That(frames.Select(f => f.Sequence), Is.EqualTo(new ulong[] { 0, 1, 2 }));
        }
    }

    [Test]
    public void BufferCallback_StopsTheTraceOnceCancelled()
    {
        (EtwUtil.RealtimeRelayProducer producer, Channel<byte[]> unused, CancellationTokenSource stop) = Create();
        using CancellationTokenSource stopScope = stop;

        stop.Cancel();
        Buffer(producer, 0, 0, out bool keepGoing);

        Assert.That(keepGoing, Is.False);
    }

    [Test]
    public void OversizedFrames_AreReportedAsLossInsteadOfSent()
    {
        (EtwUtil.RealtimeRelayProducer producer, Channel<byte[]> channel, CancellationTokenSource stop) =
            Create(o => o.MaxFrameLength = 200);
        using CancellationTokenSource stopScope = stop;

        Deliver(producer, Sample(payloadLength: 400));
        Deliver(producer, Sample());

        List<RawEtwFrame> frames = Drain(channel);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(frames.Select(f => f.Type), Is.EqualTo(new[] { RawEtwFrameType.Loss, RawEtwFrameType.Event }));
            Assert.That(frames[0].Loss!.LostEvents, Is.EqualTo(1UL));
            Assert.That(frames.Select(f => f.Sequence), Is.EqualTo(new ulong[] { 0, 1 }), "No sequence gap.");
        }
    }

    [Test]
    public void CrcOption_IsHonoured([Values(true, false)] bool withCrc)
    {
        (EtwUtil.RealtimeRelayProducer producer, Channel<byte[]> channel, CancellationTokenSource stop) =
            Create(o => o.WithCrc = withCrc);
        using CancellationTokenSource stopScope = stop;

        Deliver(producer, Sample());
        channel.Reader.TryRead(out byte[]? bytes);

        uint crc = BitConverter.ToUInt32(bytes!, 28);
        Assert.That(crc != 0, Is.EqualTo(withCrc));
    }
}
