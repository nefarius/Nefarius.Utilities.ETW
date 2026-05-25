using System.Security.Principal;
using System.Text.Json;

namespace Nefarius.Utilities.ETW.Tests;

/// <summary>
///     Integration tests for <see cref="EtwUtil.EnumerateRealtimeEventsAsync" /> and
///     <see cref="EtwUtil.ConvertRealtimeToJson" />.
///     These tests require administrator privileges and are skipped when the
///     test runner is not elevated.
/// </summary>
[Category("Realtime")]
[Category("RequiresAdmin")]
public class RealtimeConsumerTests
{
    private const string SessionName = "Nefarius.ETW.Tests.RealtimeConsumer";

    // Microsoft-Windows-Kernel-Process: generates process/thread start events which are a
    // reliable source of events on any Windows machine (simply starting the test runner
    // triggers them).
    private static readonly Guid KernelProcessGuid = new("22FB2CD6-0E7B-422B-A0C7-2FAD1FD0E716");

    [OneTimeSetUp]
    public void EnsureElevated()
    {
        if (!IsRunningAsAdmin())
        {
            Assert.Ignore("Test requires administrator privileges — skipping on non-elevated runner.");
        }

        EtwUtil.StopOrphanSession(SessionName);
    }

    [TearDown]
    public void Cleanup()
    {
        EtwUtil.StopOrphanSession(SessionName);
    }

    // -----------------------------------------------------------------------
    // EnumerateRealtimeEventsAsync: receives events and cancels cleanly
    // -----------------------------------------------------------------------

    [Test]
    [Category("EndToEnd")]
    public async Task RealtimeStreamingReceivesEventsAndCancelsCleanly()
    {
        const int targetEventCount = 5;

        using EtwRealtimeSession session = EtwRealtimeSession.Create(SessionName);
        session.EnableProvider(KernelProcessGuid, TraceEventLevel.Information);

        using CancellationTokenSource cts = new();

        int received = 0;
        bool cancelled = false;

        try
        {
            await foreach (ReadOnlyMemory<byte> eventBytes in
                EtwUtil.EnumerateRealtimeEventsAsync(SessionName, cancellationToken: cts.Token))
            {
                // Validate the JSON structure of each event.
                using JsonDocument doc = JsonDocument.Parse(eventBytes);
                JsonElement root = doc.RootElement;
                Assert.That(root.TryGetProperty("Event", out _), Is.True,
                    "Each yielded buffer must contain an 'Event' JSON object.");

                received++;
                if (received >= targetEventCount)
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
            Assert.That(received, Is.GreaterThanOrEqualTo(targetEventCount),
                "Must have received the requested number of realtime events before cancelling.");

            Assert.That(cancelled, Is.True,
                "OperationCanceledException must propagate to the caller on cancellation.");
        }
    }

    [Test]
    [Category("EndToEnd")]
    public async Task RealtimeStreamingEarlyBreakDisposesCleanly()
    {
        const int breakAfter = 3;

        using EtwRealtimeSession session = EtwRealtimeSession.Create(SessionName);
        session.EnableProvider(KernelProcessGuid, TraceEventLevel.Information);

        int received = 0;

        await foreach (ReadOnlyMemory<byte> _ in EtwUtil.EnumerateRealtimeEventsAsync(SessionName))
        {
            received++;
            if (received >= breakAfter)
            {
                break;
            }
        }

        Assert.That(received, Is.EqualTo(breakAfter),
            "Consumer must receive exactly the requested number of events before breaking.");
    }

    // -----------------------------------------------------------------------
    // ConvertRealtimeToJson: writes a valid JSON wrapper, cancels cleanly
    // -----------------------------------------------------------------------

    [Test]
    [Category("EndToEnd")]
    public async Task ConvertRealtimeToJsonProducesValidJson()
    {
        using EtwRealtimeSession session = EtwRealtimeSession.Create(SessionName);
        session.EnableProvider(KernelProcessGuid, TraceEventLevel.Information);

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(3));
        using MemoryStream ms = new();
        using Utf8JsonWriter writer = new(ms, new JsonWriterOptions { Indented = false });

        bool result = EtwUtil.ConvertRealtimeToJson(writer, SessionName, cancellationToken: cts.Token);

        Assert.That(result, Is.True, "ConvertRealtimeToJson must return true when cancelled normally.");

        string json = System.Text.Encoding.UTF8.GetString(ms.ToArray());
        using JsonDocument doc = JsonDocument.Parse(json);

        Assert.That(doc.RootElement.TryGetProperty("Events", out JsonElement events), Is.True,
            "Output must contain a top-level 'Events' array.");

        Assert.That(events.GetArrayLength(), Is.GreaterThan(0),
            "At least one realtime event must have been captured within the 3-second window.");

        await Task.CompletedTask; // keep the method async for consistency with the rest of the fixture
    }

    // -----------------------------------------------------------------------
    // Argument validation (no admin required)
    // -----------------------------------------------------------------------

    [Test]
    public void EnumerateRealtimeEventsAsync_NullSessionName_Throws() =>
        Assert.Throws<ArgumentNullException>(() =>
            EtwUtil.EnumerateRealtimeEventsAsync(null!));

    [Test]
    public void EnumerateRealtimeEventsAsync_BlankSessionName_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            EtwUtil.EnumerateRealtimeEventsAsync("   "));

    [Test]
    public void ConvertRealtimeToJson_NullWriter_Throws() =>
        Assert.Throws<ArgumentNullException>(() =>
            EtwUtil.ConvertRealtimeToJson(null!, "SomeSession"));

    [Test]
    public void ConvertRealtimeToJson_NullSessionName_Throws()
    {
        using MemoryStream ms = new();
        using Utf8JsonWriter writer = new(ms);

        Assert.Throws<ArgumentNullException>(() =>
            EtwUtil.ConvertRealtimeToJson(writer, null!));
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static bool IsRunningAsAdmin()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        WindowsPrincipal principal = new(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
