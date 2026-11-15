using System.Text;
using System.Text.Json;

using Nefarius.Utilities.ETW.Deserializer.WPP;

namespace Nefarius.Utilities.ETW.Tests;

/// <summary>
///     Verifies that the managed ETL backend produces the same JSON as the native Windows APIs, and that it keeps
///     matching the golden files generated on Windows (<c>tests/golden</c>) on every platform.
/// </summary>
/// <remarks>
///     The Windows CI job writes the native and managed output of every case to the directory named by the
///     <c>PARITY_OUTPUT_DIR</c> environment variable and uploads it as an artifact; the native files can be
///     committed as golden files.
/// </remarks>
public class BackendParityTests
{
    private const int MaxReportedDifferences = 15;

    private static readonly string[] AllPdbs =
    [
        "./symbols/BthPS3.pdb", "./symbols/BthPS3PSM.pdb", "./symbols/dshidmini.pdb"
    ];

    public static IEnumerable<TestCaseData> Cases()
    {
        yield return new TestCaseData("BthPS3_0", "./traces/BthPS3_0.etl", false, false).SetName("{m}(BthPS3_0)");
        yield return new TestCaseData("BthPS3_0_rewrite", "./traces/BthPS3_0.etl", true, false)
            .SetName("{m}(BthPS3_0_rewrite)");
        yield return new TestCaseData("BthPS3_0_raw", "./traces/BthPS3_0.etl", false, true).SetName("{m}(BthPS3_0_raw)");
        yield return new TestCaseData("DsHidMini", "./traces/DsHidMini.etl", false, false).SetName("{m}(DsHidMini)");
        yield return new TestCaseData("DsHidMini_rewrite", "./traces/DsHidMini.etl", true, false)
            .SetName("{m}(DsHidMini_rewrite)");
    }

    private static string Convert(string path, bool rewrite, bool raw, EtwBackend backend)
    {
        using MemoryStream ms = new();
        using (Utf8JsonWriter writer = new(ms))
        {
            DecodingContext context = new(PdbFileDecodingContextType.CreateFrom(AllPdbs));

            bool ok = EtwUtil.ConvertToJson(writer, [path], o =>
            {
                o.Backend = backend;
                o.WppDecodingContext = context;
                o.RewriteWppProviderName = rewrite;
                o.PreserveRawTimestamps = raw;
            });

            Assert.That(ok, Is.True, $"{backend} conversion failed.");
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static void Compare(JsonElement expected, JsonElement actual, string path, List<string> differences)
    {
        if (differences.Count >= MaxReportedDifferences)
        {
            return;
        }

        if (expected.ValueKind != actual.ValueKind)
        {
            differences.Add($"{path}: kind {expected.ValueKind} vs {actual.ValueKind}");
            return;
        }

        switch (expected.ValueKind)
        {
            case JsonValueKind.Object:
            {
                Dictionary<string, JsonElement> other = actual.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
                foreach (JsonProperty property in expected.EnumerateObject())
                {
                    if (!other.Remove(property.Name, out JsonElement counterpart))
                    {
                        differences.Add($"{path}.{property.Name}: missing");
                        continue;
                    }

                    Compare(property.Value, counterpart, $"{path}.{property.Name}", differences);
                }

                foreach (string extra in other.Keys)
                {
                    differences.Add($"{path}.{extra}: unexpected");
                }

                break;
            }
            case JsonValueKind.Array when path == "$.Events":
                CompareEvents(expected, actual, path, differences);
                break;
            case JsonValueKind.Array:
            {
                if (expected.GetArrayLength() != actual.GetArrayLength())
                {
                    differences.Add($"{path}: length {expected.GetArrayLength()} vs {actual.GetArrayLength()}");
                    return;
                }

                int index = 0;
                JsonElement.ArrayEnumerator expectedItems = expected.EnumerateArray();
                JsonElement.ArrayEnumerator actualItems = actual.EnumerateArray();
                while (expectedItems.MoveNext() && actualItems.MoveNext())
                {
                    if (differences.Count >= MaxReportedDifferences)
                    {
                        return;
                    }

                    Compare(expectedItems.Current, actualItems.Current, $"{path}[{index}]", differences);
                    index++;
                }

                break;
            }
            default:
                if (expected.GetRawText() != actual.GetRawText())
                {
                    differences.Add($"{path}: {expected.GetRawText()} vs {actual.GetRawText()}");
                }

                break;
        }
    }

    /// <summary>
    ///     Events that share a timestamp have no defined order across logger buffers. Compare each equal-timestamp
    ///     run as a set, and keep every other event in order.
    /// </summary>
    private static void CompareEvents(JsonElement expected, JsonElement actual, string path, List<string> differences)
    {
        JsonElement[] expectedEvents = expected.EnumerateArray().ToArray();
        JsonElement[] actualEvents = actual.EnumerateArray().ToArray();
        if (expectedEvents.Length != actualEvents.Length)
        {
            differences.Add($"{path}: length {expectedEvents.Length} vs {actualEvents.Length}");
            return;
        }

        int index = 0;
        while (index < expectedEvents.Length && differences.Count < MaxReportedDifferences)
        {
            long timestamp = EventTimestamp(expectedEvents[index]);
            int expectedEnd = RunEnd(expectedEvents, index, timestamp);
            int actualEnd = RunEnd(actualEvents, index, timestamp);
            if (expectedEnd == index + 1 && actualEnd == index + 1)
            {
                Compare(expectedEvents[index], actualEvents[index], $"{path}[{index}]", differences);
                index++;
                continue;
            }

            if (expectedEnd - index != actualEnd - index)
            {
                differences.Add(
                    $"{path}[{index}]: {expectedEnd - index} events at {timestamp} vs {actualEnd - index}");
                index = Math.Max(expectedEnd, actualEnd);
                continue;
            }

            // Canonical form ignores object key order and whitespace, keeps array order, and keeps duplicates.
            JsonElement[] expectedRun = expectedEvents[index..expectedEnd]
                .OrderBy(Canonical, StringComparer.Ordinal).ToArray();
            JsonElement[] actualRun = actualEvents[index..actualEnd]
                .OrderBy(Canonical, StringComparer.Ordinal).ToArray();
            for (int offset = 0; offset < expectedRun.Length && differences.Count < MaxReportedDifferences; offset++)
            {
                Compare(expectedRun[offset], actualRun[offset], $"{path}[{index + offset}]", differences);
            }

            index = expectedEnd;
        }
    }

    private static int RunEnd(JsonElement[] events, int start, long timestamp)
    {
        int end = start + 1;
        while (end < events.Length && EventTimestamp(events[end]) == timestamp)
        {
            end++;
        }

        return end;
    }

    private static long EventTimestamp(JsonElement evt)
    {
        return evt.GetProperty("Event").GetProperty("Timestamp").GetInt64();
    }

    /// <summary>
    ///     Stable text for sorting events that share a timestamp. Object properties are sorted by name and whitespace is
    ///     dropped. Array order and duplicate values stay as they are, matching <see cref="Compare" />.
    /// </summary>
    private static string Canonical(JsonElement element)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            WriteCanonical(writer, element);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (JsonProperty property in element.EnumerateObject()
                             .OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement item in element.EnumerateArray())
                {
                    WriteCanonical(writer, item);
                }

                writer.WriteEndArray();
                break;
            default:
                // Keep the parsed token text, so scalar equality matches Compare.
                writer.WriteRawValue(element.GetRawText());
                break;
        }
    }

    private static void AssertEquivalent(string expectedJson, string actualJson, string what)
    {
        List<string> differences = Differences(expectedJson, actualJson);

        Assert.That(differences, Is.Empty, $"{what} differ:{Environment.NewLine}" +
                                           string.Join(Environment.NewLine, differences));
    }

    [Test]
    public void EqualTimestampEvents_IgnoreFormattingAndPropertyOrder()
    {
        const string expected = """
                                {
                                  "Events": [
                                    { "Event": { "Timestamp": 1, "Name": "A", "Values": [1, 2] } },
                                    { "Event": { "Timestamp": 1, "Values": [3], "Name": "B" } }
                                  ]
                                }
                                """;
        const string actual =
            """{"Events":[{"Event":{"Name":"B","Timestamp":1,"Values":[3]}},{"Event":{"Values":[1,2],"Name":"A","Timestamp":1}}]}""";

        AssertEquivalent(expected, actual, "reordered events");

        const string swappedArray =
            """{"Events":[{"Event":{"Name":"A","Timestamp":1,"Values":[2,1]}},{"Event":{"Name":"B","Timestamp":1,"Values":[3]}}]}""";
        List<string> differences = Differences(expected, swappedArray);
        Assert.That(differences, Is.Not.Empty);

        const string duplicate =
            """{"Events":[{"Event":{"Name":"A","Timestamp":1,"Values":[1,2]}},{"Event":{"Name":"A","Timestamp":1,"Values":[1,2]}}]}""";
        Assert.That(Differences(expected, duplicate), Is.Not.Empty);
    }

    private static List<string> Differences(string expectedJson, string actualJson)
    {
        using JsonDocument expected = JsonDocument.Parse(expectedJson);
        using JsonDocument actual = JsonDocument.Parse(actualJson);
        List<string> differences = [];
        Compare(expected.RootElement, actual.RootElement, "$", differences);
        return differences;
    }

    [Test]
    [Platform("Win")]
    [TestCaseSource(nameof(Cases))]
    public void NativeAndManaged_ProduceIdenticalJson(string name, string path, bool rewrite, bool raw)
    {
        string native = Convert(path, rewrite, raw, EtwBackend.Native);
        string managed = Convert(path, rewrite, raw, EtwBackend.Managed);

        string? outputDir = Environment.GetEnvironmentVariable("PARITY_OUTPUT_DIR");
        if (!string.IsNullOrEmpty(outputDir))
        {
            Directory.CreateDirectory(outputDir);
            File.WriteAllText(Path.Combine(outputDir, name + ".json"), native);
            File.WriteAllText(Path.Combine(outputDir, name + ".managed.json"), managed);
        }

        AssertEquivalent(native, managed, "native and managed output");
    }

    [Test]
    [TestCaseSource(nameof(Cases))]
    public void ManagedOutput_MatchesGoldenFile(string name, string path, bool rewrite, bool raw)
    {
        string golden = Path.Combine("golden", name + ".json");
        if (!File.Exists(golden))
        {
            Assert.Ignore($"No golden file '{golden}'; commit the native output of the Windows CI job to enable.");
        }

        AssertEquivalent(File.ReadAllText(golden), Convert(path, rewrite, raw, EtwBackend.Managed),
            "golden and managed output");
    }
}
