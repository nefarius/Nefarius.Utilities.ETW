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
    private static readonly Guid OsrUsbFx2Provider = new("69cd60e3-430f-4da4-b1b3-e3bdaf945875");
    private static readonly Guid DsHidMiniProvider = new("ba65b162-c05c-4bd7-b75d-db8d030daf7f");
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
    public void DsHidMini_EventsResolveWithTheirTemplates()
    {
        instrumentationManifest manifest = Load("DsHidMini.man", DsHidMiniProvider);

        EventMetadata hresult = Build(manifest, DsHidMiniProvider, 3);
        EventMetadata powerUp = Build(manifest, DsHidMiniProvider, 28);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(hresult.Properties.Select(p => p.Name),
                Is.EqualTo(new[] { "ScopeName", "FunctionName", "HResult" }));
            Assert.That(powerUp.Properties.Select(p => p.Name),
                Is.EqualTo(new[] { "Address", "PreviousState", "ConnectionType", "Status" }));
        }
    }

    [Test]
    public void OsrUsbFx2_CustomTasksAndOpcodesResolve()
    {
        instrumentationManifest manifest = Load("osrusbfx2.man", OsrUsbFx2Provider);

        EventMetadata readStart = Build(manifest, OsrUsbFx2Provider, 1);
        EventMetadata readFail = Build(manifest, OsrUsbFx2Provider, 3);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(readStart.Name, Is.EqualTo("OSRUSBFX2/read/Start"));
            Assert.That(readStart.Properties.Select(p => p.Name), Is.EqualTo(new[] { "Device", "Length" }));
            Assert.That(readFail.Name, Is.EqualTo("OSRUSBFX2/read/fail"));
        }
    }

    [TestCase("osrusbfx2.man", "69cd60e3-430f-4da4-b1b3-e3bdaf945875")]
    [TestCase("BthPS3.man", "37dcd579-e844-4c80-9c8b-a10850b6fac6")]
    [TestCase("BthPS3PSM.man", "586aa8b1-53a6-404f-9b3e-14483e514a2c")]
    [TestCase("DsHidMini.man", "ba65b162-c05c-4bd7-b75d-db8d030daf7f")]
    [TestCase("HidHide.man", "6DA3FFA4-AB7D-40E2-B50C-C4B41BBA36E3")]
    [TestCase("HidHide.man", "D9F22586-7514-4164-BB9B-5C67D5BD2BC7")]
    [TestCase("HidHideCLI.man", "E3DF118F-1AFC-4630-9F03-6C6F73848738")]
    [TestCase("HidHideCLI.man", "54C5B24F-321D-46B0-BD2E-79FDF7BAB7AC")]
    [TestCase("HidHideClient.man", "1E545280-184F-4954-B1E4-9BF8898F2571")]
    [TestCase("HidHideClient.man", "3BEA3F5D-F523-467E-912D-E01DCF6DADCF")]
    public void EveryDefinedEventBuildsAnOperand(string file, string providerGuid)
    {
        Guid provider = Guid.Parse(providerGuid);
        instrumentationManifest manifest = Load(file, provider);

        List<(ushort Id, byte Version)> declared = manifest.Items
            .OfType<InstrumentationType>()
            .SelectMany(i => i.Items.OfType<EventsType>())
            .SelectMany(e => e.Items.OfType<ProviderType>())
            .Where(p => Guid.TryParse(p.guid, out Guid g) && g == provider)
            .SelectMany(p => p.Items.OfType<DefinitionType>())
            .SelectMany(d => d.Items)
            .Select(e => (ushort.Parse(e.value), byte.Parse(e.version ?? "0")))
            .ToList();

        Assert.That(declared, Is.Not.Empty);

        using (Assert.EnterMultipleScope())
        {
            foreach ((ushort id, byte version) in declared)
            {
                IEventTraceOperand? operand = null;
                Assert.DoesNotThrow(() => operand = EventTraceOperandBuilder.Build(manifest, provider, id, version, 0),
                    $"event {id} v{version}");
                Assert.That(operand, Is.Not.Null, $"event {id} v{version} was not resolved");
            }
        }
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

    [TestCase("HidHideCLI.man", "E3DF118F-1AFC-4630-9F03-6C6F73848738", "54C5B24F-321D-46B0-BD2E-79FDF7BAB7AC")]
    [TestCase("HidHideClient.man", "1E545280-184F-4954-B1E4-9BF8898F2571", "3BEA3F5D-F523-467E-912D-E01DCF6DADCF")]
    public void HidHideTools_OverlappingEventIdsResolveToTheirOwnProvider(string file, string generalGuid,
        string traceGuid)
    {
        Guid general = Guid.Parse(generalGuid);
        Guid trace = Guid.Parse(traceGuid);
        instrumentationManifest manifest = Load(file, general);

        EventMetadata fromGeneral = Build(manifest, general, 49);
        EventMetadata fromTrace = Build(manifest, trace, 49);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(fromGeneral.ProviderGuid, Is.EqualTo(general));
            Assert.That(fromGeneral.Name, Does.Contain("/General"));
            Assert.That(fromTrace.ProviderGuid, Is.EqualTo(trace));
            Assert.That(fromTrace.Name, Does.Contain("/Log"));
            Assert.That(EventTraceOperandBuilder.Build(manifest, general, 1, 0, 0), Is.Null);
            Assert.That(EventTraceOperandBuilder.Build(manifest, trace, 1, 0, 0), Is.Not.Null);
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
