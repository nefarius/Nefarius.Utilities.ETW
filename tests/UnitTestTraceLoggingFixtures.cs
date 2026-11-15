using System.Text;
using System.Text.Json;

namespace Nefarius.Utilities.ETW.Tests;

/// <summary>
///     Decodes real TraceLogging captures (x64 and x86 producers) on both backends.
/// </summary>
[Category("Unit")]
public class TraceLoggingFixtureTests
{
    private static JsonElement[] Events(string fixture, EtwBackend backend)
    {
        using MemoryStream ms = new();
        using (Utf8JsonWriter writer = new(ms))
        {
            Assert.That(
                EtwUtil.ConvertToJson(writer, [$"./traces/fixtures/{fixture}.etl"], o => o.Backend = backend),
                Is.True);
        }

        using JsonDocument doc = JsonDocument.Parse(Encoding.UTF8.GetString(ms.ToArray()));
        return doc.RootElement.GetProperty("Events").EnumerateArray().Select(e => e.Clone()).ToArray();
    }

    private static JsonElement Named(JsonElement[] events, string name)
    {
        return events.First(e => e.GetProperty("Event").GetProperty("Name").GetString() == name)
            .GetProperty("Event").GetProperty("Properties")[0];
    }

    [TestCase("tracelogging-x64", EtwBackend.Managed)]
    [TestCase("tracelogging-x86", EtwBackend.Managed)]
    [TestCase("tracelogging-x64", EtwBackend.Native)]
    [TestCase("tracelogging-x86", EtwBackend.Native)]
    public void TraceLoggingEvents_UseTheirSchemaNamesAndTypes(string fixture, EtwBackend backend)
    {
        if (backend == EtwBackend.Native && !OperatingSystem.IsWindows())
        {
            Assert.Ignore("The native backend needs Windows.");
        }

        JsonElement[] events = Events(fixture, backend);
        JsonElement scalars = Named(events, "Scalars");
        JsonElement arrays = Named(events, "Arrays");
        JsonElement nested = Named(events, "Nested");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(scalars.GetProperty("B").ValueKind, Is.EqualTo(JsonValueKind.True).Or.EqualTo(JsonValueKind.False));
            Assert.That(arrays.GetProperty("Ints").ValueKind, Is.EqualTo(JsonValueKind.Array));
            Assert.That(arrays.GetProperty("Empty").GetArrayLength(), Is.Zero);
            Assert.That(arrays.TryGetProperty("Ints.Count", out _), Is.False);
            Assert.That(nested.GetProperty("Outer").GetProperty("Inner").ValueKind, Is.EqualTo(JsonValueKind.Object));
            Assert.That(nested.GetProperty("Outer").TryGetProperty("After", out _), Is.True);
            Assert.That(nested.GetProperty("Sentinel").GetUInt32(), Is.EqualTo(0xCAFEBABEu));
        }
    }
}