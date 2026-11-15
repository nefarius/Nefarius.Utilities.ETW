using System.Text.Json;

using Nefarius.Utilities.ETW.Deserializer.WPP;

namespace Nefarius.Utilities.ETW.Tests;

/// <summary>
///     Checks of the managed backend's JSON output that run on every platform.
/// </summary>
public class ManagedBackendTests
{
    private static JsonElement ConvertEvents(string path, Action<EtwJsonConverterOptions>? configure = null)
    {
        using MemoryStream ms = new();
        using (Utf8JsonWriter writer = new(ms))
        {
            Assert.That(EtwUtil.ConvertToJson(writer, [path], o =>
            {
                o.Backend = EtwBackend.Managed;
                configure?.Invoke(o);
            }), Is.True);
        }

        return JsonDocument.Parse(ms.ToArray()).RootElement.GetProperty("Events").Clone();
    }

    [Test]
    public void TraceHeaderEvent_IsDecoded()
    {
        JsonElement events = ConvertEvents("./traces/BthPS3_0.etl");

        JsonElement header = events.EnumerateArray()
            .Select(e => e.GetProperty("Event"))
            .First(e => e.GetProperty("Name").GetString() == "MSNT_SystemTrace/EventTrace/Header");
        JsonElement props = header.GetProperty("Properties")[0];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(props.GetProperty("BufferSize").GetUInt32(), Is.EqualTo(65536));
            Assert.That(props.GetProperty("PointerSize").GetUInt32(), Is.EqualTo(8));
            Assert.That(props.GetProperty("NumberOfProcessors").GetUInt32(), Is.GreaterThan(0));
            Assert.That(props.GetProperty("SessionNameString").GetString(), Is.Not.Empty);
            Assert.That(props.GetProperty("LogFileNameString").GetString(), Does.EndWith(".etl"));
            Assert.That(header.GetProperty("Timestamp").GetInt64(),
                Is.EqualTo(props.GetProperty("StartTime").GetInt64()));
            Assert.That(header.GetProperty("ProviderGuid").GetGuid(),
                Is.EqualTo(Guid.Parse("9e814aad-3204-11d2-9a82-006008a86939")));
            Assert.That(header.GetProperty("Version").GetInt32(), Is.EqualTo(2));
            Assert.That(props.GetProperty("TimeZoneInformation").GetArrayLength(), Is.EqualTo(176));
        }
    }

    [Test]
    public void EventTraceBookkeeping_MatchesTheNativeSchema()
    {
        DecodingContext context = new(PdbFileDecodingContextType.CreateFrom(
            ["./symbols/BthPS3.pdb", "./symbols/BthPS3PSM.pdb"]));
        JsonElement events = ConvertEvents("./traces/BthPS3_0.etl", o => o.WppDecodingContext = context);
        JsonElement[] decoded = events.EnumerateArray().Select(e => e.GetProperty("Event")).ToArray();

        JsonElement partition = decoded[1];
        JsonElement build = decoded[2];
        JsonElement debugId = decoded[3];
        JsonElement wpp = decoded.First(e => e.GetProperty("Name").GetString() == "WPP");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(partition.GetProperty("Properties")[0].GetProperty("EventVersion").GetInt32(), Is.EqualTo(0));
            Assert.That(partition.GetProperty("Properties")[0].GetProperty("PartitionId").GetString(), Is.Empty);
            Assert.That(build.GetProperty("Properties")[0].GetProperty("BuildString").GetString(), Does.Contain("amd64"));
            Assert.That(build.GetProperty("ProcessId").GetUInt32(), Is.EqualTo(uint.MaxValue));
            Assert.That(build.GetProperty("ThreadId").GetUInt32(), Is.EqualTo(uint.MaxValue));
            Assert.That(debugId.GetProperty("Properties")[0].GetProperty("PdbName").GetString(), Does.EndWith(".pdb"));
            Assert.That(wpp.GetProperty("Properties")[0].GetProperty("ProviderGuid").GetGuid(), Is.EqualTo(Guid.Empty));
        }
    }

    [Test]
    public void NativeBackend_IsRejectedOffWindows()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Native backend is available.");
        }

        using MemoryStream ms = new();
        using Utf8JsonWriter writer = new(ms);
        Assert.Throws<PlatformNotSupportedException>(() =>
            EtwUtil.ConvertToJson(writer, ["./traces/BthPS3_0.etl"], o => o.Backend = EtwBackend.Native));
    }

    [Test]
    public void MissingFile_ReportsErrorAndReturnsFalse()
    {
        using MemoryStream ms = new();
        using Utf8JsonWriter writer = new(ms);
        List<string> errors = [];

        bool result = EtwUtil.ConvertToJson(writer, ["./traces/__missing__.etl"], o =>
        {
            o.Backend = EtwBackend.Managed;
            o.ReportError = errors.Add;
        });

        Assert.That(result, Is.False);
        Assert.That(errors, Has.Count.EqualTo(1));
    }
}
