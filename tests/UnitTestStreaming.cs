using System.Text.Json;

using Nefarius.Utilities.ETW.Deserializer.WPP;

namespace Nefarius.Utilities.ETW.Tests;

/// <summary>
///     Tests for <see cref="EtwUtil.EnumerateEventsAsync" />.
/// </summary>
[Category("Streaming")]
public class StreamingTests
{
    // -----------------------------------------------------------------------
    // Smoke test: events are streamed and structurally correct
    // -----------------------------------------------------------------------

    [Test]
    [Category("EndToEnd")]
    public async Task StreamingEnumerationYieldsEventsTest()
    {
        const string etwFilePath = @".\traces\BthPS3_0.etl";

        DecodingContext decodingContext = new(PdbFileDecodingContextType.CreateFrom(
            @".\symbols\BthPS3.pdb",
            @".\symbols\BthPS3PSM.pdb"
        ));

        int eventCount = 0;
        bool hasDecodedWppEvent = false;

        await foreach (ReadOnlyMemory<byte> eventBytes in EtwUtil.EnumerateEventsAsync(
                           [etwFilePath],
                           opts => opts.WppDecodingContext = decodingContext))
        {
            eventCount++;

            using JsonDocument doc = JsonDocument.Parse(eventBytes);
            JsonElement root = doc.RootElement;
            JsonElement evt = root.GetProperty("Event");

            string? name = evt.GetProperty("Name").GetString();
            if (name == "WPP")
            {
                string? fs = evt.GetProperty("Properties")[0].GetProperty("FormattedString").GetString();
                if (fs is not null && !fs.StartsWith("GUID="))
                {
                    hasDecodedWppEvent = true;
                }
            }
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(eventCount, Is.GreaterThan(0),
                "Streaming enumeration must yield at least one event.");

            Assert.That(hasDecodedWppEvent, Is.True,
                "At least one WPP event must have a successfully decoded FormattedString (not the fallback 'GUID=...' message).");
        }
    }

    // -----------------------------------------------------------------------
    // Smoke test: event count matches the synchronous ConvertToJson output
    // -----------------------------------------------------------------------

    [Test]
    [Category("EndToEnd")]
    public async Task StreamingEventCountMatchesSynchronousTest()
    {
        const string etwFilePath = @".\traces\BthPS3_0.etl";

        DecodingContext decodingContext = new(PdbFileDecodingContextType.CreateFrom(
            @".\symbols\BthPS3.pdb",
            @".\symbols\BthPS3PSM.pdb"
        ));

        // Count events via the synchronous JSON path.
        string fullJson = Shared.BthPs3EtlTraceDecodeToString();
        using JsonDocument syncDoc = JsonDocument.Parse(fullJson);
        int syncCount = syncDoc.RootElement.GetProperty("Events").GetArrayLength();

        // Count events via the streaming path.
        int streamCount = 0;
        await foreach (ReadOnlyMemory<byte> _ in EtwUtil.EnumerateEventsAsync(
                           [etwFilePath],
                           opts => opts.WppDecodingContext = decodingContext))
        {
            streamCount++;
        }

        Assert.That(streamCount, Is.EqualTo(syncCount),
            "Streaming path must yield the same number of events as the synchronous path.");
    }

    // -----------------------------------------------------------------------
    // Cancellation: enumerator terminates cleanly when cancelled mid-stream
    // -----------------------------------------------------------------------

    [Test]
    [Category("EndToEnd")]
    public async Task CancellationStopsEnumerationTest()
    {
        const string etwFilePath = @".\traces\BthPS3_0.etl";
        const int cancelAfter = 10;

        // Provide the full decoding context so WPP events are decoded and the trace
        // yields well more than cancelAfter items before the cancellation is triggered.
        DecodingContext decodingContext = new(PdbFileDecodingContextType.CreateFrom(
            @".\symbols\BthPS3.pdb",
            @".\symbols\BthPS3PSM.pdb"
        ));

        using CancellationTokenSource cts = new();

        int received = 0;
        bool cancelled = false;

        try
        {
            await foreach (ReadOnlyMemory<byte> _ in EtwUtil.EnumerateEventsAsync(
                               [etwFilePath],
                               opts => opts.WppDecodingContext = decodingContext,
                               cts.Token))
            {
                received++;
                if (received >= cancelAfter)
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
            Assert.That(received, Is.GreaterThanOrEqualTo(cancelAfter),
                "Must have received at least the requested number of events before cancelling.");

            Assert.That(cancelled, Is.True,
                "OperationCanceledException must be propagated to the caller on cancellation.");
        }
    }

    // -----------------------------------------------------------------------
    // Disposal path: break without CancellationToken — worker must exit cleanly
    // -----------------------------------------------------------------------

    [Test]
    [Category("EndToEnd")]
    public async Task EarlyBreakWithoutCancellationDisposesCleanlyTest()
    {
        const string etwFilePath = @".\traces\BthPS3_0.etl";
        const int breakAfter = 10;

        DecodingContext decodingContext = new(PdbFileDecodingContextType.CreateFrom(
            @".\symbols\BthPS3.pdb",
            @".\symbols\BthPS3PSM.pdb"
        ));

        int received = 0;

        // No CancellationTokenSource — exercises the linked-CTS disposal path that
        // unblocks the producer when the consumer breaks early without cancelling.
        await foreach (ReadOnlyMemory<byte> _ in EtwUtil.EnumerateEventsAsync(
                           [etwFilePath],
                           opts => opts.WppDecodingContext = decodingContext))
        {
            received++;
            if (received >= breakAfter)
            {
                break;
            }
        }

        // No OperationCanceledException expected; loop must have completed normally.
        Assert.That(received, Is.EqualTo(breakAfter),
            "Consumer must receive exactly the requested number of events before breaking.");
    }
}
