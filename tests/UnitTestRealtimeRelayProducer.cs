using System.Runtime.Versioning;
using System.Security.Principal;

using Nefarius.Utilities.ETW.Relay;

namespace Nefarius.Utilities.ETW.Tests;

/// <summary>
///     Argument checks for <see cref="EtwUtil.EnumerateRealtimeRelayFramesAsync" /> that need no ETW session.
/// </summary>
[SupportedOSPlatform("windows")]
[Category("Realtime")]
[Platform("Win")]
public class RealtimeRelayProducerArgumentTests
{
    [Test]
    public void NullSessionName_Throws() =>
        Assert.Throws<ArgumentNullException>(() => EtwUtil.EnumerateRealtimeRelayFramesAsync(null!));

    [Test]
    public void BlankSessionName_Throws() =>
        Assert.Throws<ArgumentException>(() => EtwUtil.EnumerateRealtimeRelayFramesAsync("  "));

    [Test]
    public void FrameLimitTooSmall_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EtwUtil.EnumerateRealtimeRelayFramesAsync("s", o => o.MaxFrameLength = 8));

    [Test]
    public void FrameLimitAboveAbsoluteMaximum_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EtwUtil.EnumerateRealtimeRelayFramesAsync("s", o => o.MaxFrameLength = uint.MaxValue));

    [Test]
    public void MissingSession_SurfacesProcessTraceFailure()
    {
        // ProcessTrace reports ERROR_WMI_INSTANCE_NOT_FOUND (4201); it must not end like a normal stop.
        System.ComponentModel.Win32Exception? ex = Assert.ThrowsAsync<System.ComponentModel.Win32Exception>(async () =>
        {
            await foreach (byte[] _ in EtwUtil.EnumerateRealtimeRelayFramesAsync(
                               "Nefarius.ETW.Tests.RelayProducer.DoesNotExist"))
            {
            }
        });

        Assert.That(ex!.NativeErrorCode, Is.EqualTo(4201));
    }
}

/// <summary>
///     Live capture through <see cref="EtwUtil.EnumerateRealtimeRelayFramesAsync" />. Requires administrator rights.
/// </summary>
[SupportedOSPlatform("windows")]
[Category("Realtime")]
[Category("RequiresAdmin")]
[Platform("Win")]
public class RealtimeRelayProducerTests
{
    private const string SessionName = "Nefarius.ETW.Tests.RelayProducer";

    private static readonly Guid KernelProcessGuid = new("22FB2CD6-0E7B-422B-A0C7-2FAD1FD0E716");

    [OneTimeSetUp]
    public void EnsureElevated()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
        {
            Assert.Ignore("Test requires administrator privileges.");
        }

        EtwUtil.StopOrphanSession(SessionName);
    }

    [TearDown]
    public void Cleanup() => EtwUtil.StopOrphanSession(SessionName);

    [Test]
    [Category("EndToEnd")]
    public async Task ProducesOrderedFramesAndEndsBeforeCancellation()
    {
        using EtwRealtimeSession session = EtwRealtimeSession.Create(SessionName);
        session.EnableProvider(KernelProcessGuid, TraceEventLevel.Information);

        Guid sessionId = Guid.NewGuid();
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));
        List<RawEtwFrame> frames = [];
        bool cancelled = false;

        try
        {
            await foreach (byte[] bytes in EtwUtil.EnumerateRealtimeRelayFramesAsync(SessionName,
                               o => o.SessionId = sessionId, cts.Token))
            {
                frames.Add(RawEtwFrameCodec.Decode(bytes));
                if (frames.Count(f => f.Type == RawEtwFrameType.Event) >= 5)
                {
                    cts.Cancel();
                }
            }
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(cancelled, Is.True);
            Assert.That(frames[0].Type, Is.EqualTo(RawEtwFrameType.Hello));
            Assert.That(frames[0].Hello!.SessionId, Is.EqualTo(sessionId));
            Assert.That(frames[^1].Type, Is.EqualTo(RawEtwFrameType.End), "The end frame precedes the cancellation.");
            Assert.That(frames.Select(f => f.Sequence), Is.EqualTo(Enumerable.Range(0, frames.Count).Select(i => (ulong)i)));
            Assert.That(frames.Count(f => f.Type == RawEtwFrameType.Event), Is.GreaterThanOrEqualTo(5));
        }
    }

    [Test]
    [Category("EndToEnd")]
    public async Task EarlyBreakReleasesTheTraceHandle()
    {
        using EtwRealtimeSession session = EtwRealtimeSession.Create(SessionName);
        session.EnableProvider(KernelProcessGuid, TraceEventLevel.Information);

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));
        int received = 0;

        await foreach (byte[] _ in EtwUtil.EnumerateRealtimeRelayFramesAsync(SessionName, cancellationToken: cts.Token))
        {
            if (++received >= 3)
            {
                break;
            }
        }

        // A second consumer can attach only if the first released its handle.
        await foreach (byte[] _ in EtwUtil.EnumerateRealtimeRelayFramesAsync(SessionName, cancellationToken: cts.Token))
        {
            break;
        }

        Assert.That(received, Is.EqualTo(3));
    }
}
