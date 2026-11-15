namespace Nefarius.Utilities.ETW.Tests;

/// <summary>
///     Windows-only entry points must fail with a clear exception elsewhere instead of a DllNotFoundException.
/// </summary>
public class PlatformGuardTests
{
    [SetUp]
    public void SkipOnWindows()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Windows supports real-time sessions.");
        }
    }

    [Test]
    public void RealtimeSession_Create_Throws()
    {
        Assert.Throws<PlatformNotSupportedException>(() => EtwRealtimeSession.Create("guard-test"));
    }

    [Test]
    public void StopOrphanSession_Throws()
    {
        Assert.Throws<PlatformNotSupportedException>(() => EtwUtil.StopOrphanSession("guard-test"));
    }

    [Test]
    public void EnumerateSessionNames_Throws()
    {
        Assert.Throws<PlatformNotSupportedException>(() => EtwUtil.EnumerateSessionNames());
    }

    [Test]
    public void EnumerateRealtimeEventsAsync_Throws()
    {
        Assert.Throws<PlatformNotSupportedException>(() => EtwUtil.EnumerateRealtimeEventsAsync("guard-test"));
    }

    [Test]
    public void ConvertRealtimeToJson_Throws()
    {
        using MemoryStream ms = new();
        using System.Text.Json.Utf8JsonWriter writer = new(ms);
        Assert.Throws<PlatformNotSupportedException>(() => EtwUtil.ConvertRealtimeToJson(writer, "guard-test"));
    }
}
