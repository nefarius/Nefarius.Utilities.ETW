using System.Text.Json;

namespace Nefarius.Utilities.ETW.Tests;

/// <summary>
///     Checks of the managed backend's JSON output that run on every platform.
/// </summary>
public class ManagedBackendTests
{
    private static JsonElement ConvertEvents(string path)
    {
        using MemoryStream ms = new();
        using (Utf8JsonWriter writer = new(ms))
        {
            Assert.That(EtwUtil.ConvertToJson(writer, [path], o => o.Backend = EtwBackend.Managed), Is.True);
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
