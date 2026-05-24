using System.Text.Json;

using VerifyNUnit;

namespace Nefarius.Utilities.ETW.Tests;

/// <summary>
///     Snapshot / golden-master tests for the decoded JSON output.
///
///     Rather than snapshotting the full (potentially multi-thousand-line) JSON, each test
///     captures a compact structural summary so that:
///     <list type="bullet">
///         <item>Baseline files stay small and diff-readable.</item>
///         <item>CI log output remains manageable.</item>
///         <item>Regressions in event count, WPP decoding, and structure are still caught.</item>
///     </list>
///
///     On first run (or after a deliberate change) the test writes a <c>.received.txt</c> file
///     next to the <c>.verified.txt</c> baseline.  Approve it with <c>dotnet verify accept</c>
///     or by renaming it, then commit the verified file so CI can compare against it.
///
///     Baseline files live under <c>tests/Snapshots/</c>.
/// </summary>
[Category("EndToEnd")]
public class SnapshotTests
{
    [Test]
    public Task BthPs3EtlTrace_StructuralSnapshotMatches()
    {
        object summary = BuildSummary(Shared.BthPs3EtlTraceDecodeToString(), sampleCount: 3);
        return Verifier.Verify(summary)
            .UseDirectory("Snapshots")
            .UseFileName("BthPS3_0_summary");
    }

    [Test]
    public Task DsHidMiniEtlTrace_StructuralSnapshotMatches()
    {
        object summary = BuildSummary(Shared.DsHidMiniEtlTraceDecodeToString(), sampleCount: 3);
        return Verifier.Verify(summary)
            .UseDirectory("Snapshots")
            .UseFileName("DsHidMini_summary");
    }

    // -----------------------------------------------------------------------

    /// <summary>
    ///     Builds a compact, deterministic summary of a decoded ETL JSON string suitable for
    ///     snapshotting.  The summary captures:
    ///     <list type="bullet">
    ///         <item>Total event count.</item>
    ///         <item>Number of WPP events.</item>
    ///         <item>Number of WPP events with a successfully decoded FormattedString (not the fallback).</item>
    ///         <item>The first <paramref name="sampleCount" /> decoded WPP events (name, provider, formatted string).</item>
    ///         <item>The set of unique event names seen across the trace.</item>
    ///     </list>
    /// </summary>
    private static object BuildSummary(string json, int sampleCount)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement eventsArray = doc.RootElement.GetProperty("Events");

        List<JsonElement> allEvents = eventsArray
            .EnumerateArray()
            .Select(e => e.GetProperty("Event"))
            .ToList();

        List<JsonElement> wppEvents = allEvents
            .Where(e => e.GetProperty("Name").GetString() == "WPP")
            .ToList();

        int decodedWppCount = wppEvents
            .Count(e =>
            {
                string? fs = e.GetProperty("Properties")[0].GetProperty("FormattedString").GetString();
                return fs is not null && !fs.StartsWith("GUID=");
            });

        List<object> sampleWppEvents = wppEvents
            .Where(e =>
            {
                string? fs = e.GetProperty("Properties")[0].GetProperty("FormattedString").GetString();
                return fs is not null && !fs.StartsWith("GUID=");
            })
            .Take(sampleCount)
            .Select(e =>
            {
                JsonElement props = e.GetProperty("Properties")[0];
                return (object)new
                {
                    Provider       = props.GetProperty("GuidName").GetString(),
                    Function       = props.GetProperty("FunctionName").GetString(),
                    Level          = props.GetProperty("LevelName").GetString(),
                    FormattedString = props.GetProperty("FormattedString").GetString()
                };
            })
            .ToList();

        List<string> uniqueEventNames = allEvents
            .Select(e => e.GetProperty("Name").GetString()!)
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        return new
        {
            TotalEventCount    = allEvents.Count,
            WppEventCount      = wppEvents.Count,
            DecodedWppCount    = decodedWppCount,
            UniqueEventNames   = uniqueEventNames,
            SampleDecodedEvents = sampleWppEvents
        };
    }
}
