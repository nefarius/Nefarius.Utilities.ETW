using System.Text;
using System.Text.Json;

using Nefarius.Utilities.ETW.Deserializer;

using Windows.Win32.System.Diagnostics.Etw;

namespace Nefarius.Utilities.ETW.Tests;

/// <summary>
///     Custom instrumentation manifest support, built around real driver manifests.
/// </summary>
[Category("Unit")]
public unsafe class CustomManifestTests
{
    private static readonly Guid BthPs3Provider = new("37dcd579-e844-4c80-9c8b-a10850b6fac6");
    private static readonly Guid BthPs3PsmProvider = new("586aa8b1-53a6-404f-9b3e-14483e514a2c");
    private static readonly Guid HidHideLogProvider = new("6DA3FFA4-AB7D-40E2-B50C-C4B41BBA36E3");
    private static readonly Guid HidHideTraceProvider = new("D9F22586-7514-4164-BB9B-5C67D5BD2BC7");

    private static instrumentationManifest Load(string file, Guid provider)
    {
        return new EventSourceManifest(provider, File.OpenRead(Path.Combine("manifests", file))).Schema;
    }

    private static EventMetadata Build(instrumentationManifest manifest, Guid provider, ushort id, byte version = 0)
    {
        IEventTraceOperand? operand = EventTraceOperandBuilder.Build(manifest, provider, id, version, 0);
        Assert.That(operand, Is.Not.Null, $"No operand for {provider} event {id} v{version}.");
        return operand!.Metadata;
    }

    [Test]
    public void BthPs3_EventsResolveWithTheirTemplates()
    {
        instrumentationManifest manifest = Load("BthPS3.man", BthPs3Provider);

        EventMetadata start = Build(manifest, BthPs3Provider, 1);
        EventMetadata failed = Build(manifest, BthPs3Provider, 22);
        EventMetadata noTemplate = Build(manifest, BthPs3Provider, 8);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(start.Name, Is.EqualTo("Nefarius BthPS3 Profile Driver/StartEvent/Start"));
            Assert.That(start.Properties.Select(p => p.Name), Is.EqualTo(new[] { "DriverObjPtr", "Status" }));
            Assert.That(failed.Properties.Select(p => p.Name),
                Is.EqualTo(new[] { "ScopeName", "FunctionName", "Status" }));
            Assert.That(noTemplate.Properties, Is.Empty);
        }
    }

    [Test]
    public void BthPs3Psm_EventsResolveWithTheirTemplates()
    {
        instrumentationManifest manifest = Load("BthPS3PSM.man", BthPs3PsmProvider);

        EventMetadata detailed = Build(manifest, BthPs3PsmProvider, 11);

        Assert.That(detailed.Properties.Select(p => p.Name), Is.EqualTo(new[]
        {
            "OriginalPsm", "EffectivePsm", "Patched", "Channel", "InstanceId", "TransportType"
        }));
    }

    [Test]
    public void HidHide_OverlappingEventIdsResolveToTheirOwnProvider()
    {
        instrumentationManifest manifest = Load("HidHide.man", HidHideLogProvider);

        EventMetadata log = Build(manifest, HidHideLogProvider, 49);
        EventMetadata trace = Build(manifest, HidHideTraceProvider, 49);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(log.ProviderGuid, Is.EqualTo(HidHideLogProvider));
            Assert.That(log.Name, Does.StartWith("Nefarius HidHide/General"));
            Assert.That(trace.ProviderGuid, Is.EqualTo(HidHideTraceProvider));
            Assert.That(trace.Name, Does.StartWith("Nefarius-Drivers-HidHide/Log"));
            Assert.That(trace.Properties.Select(p => p.Name),
                Is.EqualTo(new[] { "FileName", "LineNumber", "FunctionName", "MessageW", "MessageA" }));
        }
    }

    [Test]
    public void HidHide_EventOnlyDefinedByTheSecondProviderIsNotFoundForTheFirst()
    {
        instrumentationManifest manifest = Load("HidHide.man", HidHideLogProvider);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(EventTraceOperandBuilder.Build(manifest, HidHideLogProvider, 1, 0, 0), Is.Null);
            Assert.That(EventTraceOperandBuilder.Build(manifest, HidHideTraceProvider, 1, 0, 0), Is.Not.Null);
        }
    }

    [Test]
    public void AnUnknownProviderResolvesNothing()
    {
        instrumentationManifest manifest = Load("BthPS3.man", BthPs3Provider);

        Assert.That(EventTraceOperandBuilder.Build(manifest, Guid.NewGuid(), 1, 0, 0), Is.Null);
    }

    [Test]
    public void AnUnknownEventIdResolvesNothing()
    {
        instrumentationManifest manifest = Load("BthPS3.man", BthPs3Provider);

        Assert.That(EventTraceOperandBuilder.Build(manifest, BthPs3Provider, 999, 0, 0), Is.Null);
    }

    [Test]
    public void TheEventVersionMustMatchAndDefaultsToZero()
    {
        instrumentationManifest manifest = Load("BthPS3.man", BthPs3Provider);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Build(manifest, BthPs3Provider, 3, 0).Version, Is.EqualTo(0));
            Assert.That(EventTraceOperandBuilder.Build(manifest, BthPs3Provider, 3, 1, 0), Is.Null);
        }
    }

    [Test]
    public void ARecordIsDecodedThroughTheCustomManifestAndTheLookupRunsOncePerProvider()
    {
        int lookups = 0;
        using MemoryStream ms = new();
        using (Utf8JsonWriter json = new(ms))
        {
            Deserializer<EtwJsonWriter> deserializer = new(new EtwJsonWriter(json), provider =>
            {
                lookups++;
                return provider == BthPs3Provider
                    ? File.OpenRead(Path.Combine("manifests", "BthPS3.man"))
                    : null;
            });

            json.WriteStartArray();
            Feed(deserializer, BthPs3Provider, 3, BitConverter.GetBytes(5u));
            Feed(deserializer, BthPs3Provider, 4, BitConverter.GetBytes(2u));
            json.WriteEndArray();
        }

        using JsonDocument doc = JsonDocument.Parse(Encoding.UTF8.GetString(ms.ToArray()));
        JsonElement first = doc.RootElement[0].GetProperty("Event");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(lookups, Is.EqualTo(1));
            Assert.That(doc.RootElement.GetArrayLength(), Is.EqualTo(2));
            Assert.That(first.GetProperty("Name").GetString(), Does.EndWith("/HciVersion/Info"));
            Assert.That(first.GetProperty("Properties")[0].GetProperty("Version").GetUInt32(), Is.EqualTo(5));
            Assert.That(doc.RootElement[1].GetProperty("Event").GetProperty("Name").GetString(),
                Does.EndWith("/HciVersionTooLow/Info"));
        }
    }

    private static void Feed(Deserializer<EtwJsonWriter> deserializer, Guid provider, ushort id, byte[] payload)
    {
        fixed (byte* payloadPtr = payload)
        {
            EVENT_RECORD record = default;
            record.EventHeader.ProviderId = provider;
            record.EventHeader.EventDescriptor.Id = id;
            record.UserData = payloadPtr;
            record.UserContext = payloadPtr;
            record.UserDataLength = (ushort)payload.Length;
            deserializer.Deserialize(&record);
        }
    }
}
