using System.Collections.ObjectModel;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using Nefarius.Utilities.ETW.Deserializer.WPP;
using Nefarius.Utilities.ETW.Deserializer.WPP.TMF;

namespace Nefarius.Utilities.ETW.Tests;

/// <summary>
///     Core parser tests: TMF and PDB format parsing, ETL decode assertions, and plausibility checks.
/// </summary>
[Category("Parse")]
public class Tests
{
    private const int ExpectedTypesCount = 2610;

    private static readonly TraceMessageFormat ExpectedSampleType = new()
    {
        FileName      = "Bluetooth.Context.c",
        Flags         = "TRACE_BTH",
        Function      = "BthPS3_DeviceContextHeaderInit",
        Id            = 12,
        Level         = "TRACE_LEVEL_VERBOSE",
        MessageFormat = "%0 [%!FUNC!] <-- Exit <status=%10!s!>",
        MessageGuid   = Guid.Parse("e4b27b5e-24d0-369f-a4b5-23228e160bd2"),
        Opcode        = "Bluetooth_Context_c149",
        Provider      = "BthPS3"
    };

    // Heavy parse results shared across tests in this fixture.
    private ReadOnlyCollection<TraceMessageFormat> _tmfResult  = null!;
    private ReadOnlyCollection<TraceMessageFormat> _pdbResult  = null!;

    [OneTimeSetUp]
    public void OneTimeSetup()
    {
        _tmfResult = Shared.ExtractFromFormatFiles();
        _pdbResult = Shared.ExtractFromSymbolFiles();
    }

    [SetUp]
    public void Setup() { }

    // -----------------------------------------------------------------------
    // Format-file (TMF) parsing
    // -----------------------------------------------------------------------

    [Test]
    public void TmfFileParserTest()
    {
        TraceMessageFormat sample = _tmfResult.Single(f => f.Opcode.Equals("Bluetooth_Context_c149"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sample, Is.EqualTo(ExpectedSampleType));
            Assert.That(_tmfResult, Has.Count.EqualTo(ExpectedTypesCount));
        }
    }

    // -----------------------------------------------------------------------
    // PDB parsing
    // -----------------------------------------------------------------------

    [Test]
    public void PdbFileParserTest()
    {
        TraceMessageFormat sample = _pdbResult.Single(f => f.Opcode.Equals("Bluetooth_Context_c149"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sample, Is.EqualTo(ExpectedSampleType));
            Assert.That(_pdbResult, Has.Count.EqualTo(ExpectedTypesCount));
        }
    }

    // -----------------------------------------------------------------------
    // Plausibility: TMF and PDB results must be equivalent
    // -----------------------------------------------------------------------

    [Test]
    public void PlausibilityTest()
    {
        Assert.That(_tmfResult, Is.EquivalentTo(_pdbResult));
    }

    // -----------------------------------------------------------------------
    // BthPS3 ETL decode — full context (both PDB files)
    // -----------------------------------------------------------------------

    [Test]
    [Category("EndToEnd")]
    public void BthPs3EtlTraceDecodingTest()
    {
        string json = Shared.BthPs3EtlTraceDecodeToString();

        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        JsonElement events = root.GetProperty("Events");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(events.GetArrayLength(), Is.GreaterThan(0),
                "Decoded trace must contain at least one event.");

            // At least one event should be a successfully decoded WPP event (FormattedString
            // must NOT begin with the fallback prefix "GUID=" when the full context is provided).
            bool hasDecodedWppEvent = events.EnumerateArray()
                .Select(e => e.GetProperty("Event"))
                .Where(e => e.GetProperty("Name").GetString() == "WPP")
                .Select(e => e.GetProperty("Properties")[0])
                .Any(props =>
                {
                    string? fs = props.GetProperty("FormattedString").GetString();
                    return fs is not null && !fs.StartsWith("GUID=");
                });

            Assert.That(hasDecodedWppEvent, Is.True,
                "At least one WPP event must have a successfully decoded FormattedString (not the fallback 'GUID=...' message).");
        }
    }

    // -----------------------------------------------------------------------
    // BthPS3 ETL decode — incomplete context (only BthPS3.pdb, no BthPS3PSM.pdb)
    // -----------------------------------------------------------------------

    [Test]
    [Category("EndToEnd")]
    public void BthPs3EtlTraceIncompleteContextTest()
    {
        string json = Shared.BthPs3EtlIncompleteContextDecodeToString();

        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        JsonElement events = root.GetProperty("Events");

        Assert.That(events.GetArrayLength(), Is.GreaterThan(0),
            "Decoded trace must contain at least one event even with an incomplete context.");

        // Some WPP events must fall back to the "no format information found" message because
        // BthPS3PSM.pdb is absent from the context.
        bool hasFallbackEvent = events.EnumerateArray()
            .Select(e => e.GetProperty("Event"))
            .Where(e => e.GetProperty("Name").GetString() == "WPP")
            .Select(e => e.GetProperty("Properties")[0])
            .Any(props =>
            {
                string? fs = props.GetProperty("FormattedString").GetString();
                return fs is not null && fs.StartsWith("GUID=");
            });

        Assert.That(hasFallbackEvent, Is.True,
            "At least one WPP event must fall back to the 'GUID=...' message when BthPS3PSM.pdb is absent.");
    }

    // -----------------------------------------------------------------------
    // DsHidMini ETL decode
    // -----------------------------------------------------------------------

    [Test]
    [Category("EndToEnd")]
    public void DsHidMiniEtlTraceDecodingTest()
    {
        string json = Shared.DsHidMiniEtlTraceDecodeToString();

        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        JsonElement events = root.GetProperty("Events");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(events.GetArrayLength(), Is.GreaterThan(0),
                "Decoded DsHidMini trace must contain at least one event.");

            bool hasDecodedWppEvent = events.EnumerateArray()
                .Select(e => e.GetProperty("Event"))
                .Where(e => e.GetProperty("Name").GetString() == "WPP")
                .Select(e => e.GetProperty("Properties")[0])
                .Any(props =>
                {
                    string? fs = props.GetProperty("FormattedString").GetString();
                    return fs is not null && !fs.StartsWith("GUID=");
                });

            Assert.That(hasDecodedWppEvent, Is.True,
                "At least one WPP event must have a successfully decoded FormattedString.");
        }
    }

    // -----------------------------------------------------------------------
    // EnumeratePdbReferences – known-good trace
    // -----------------------------------------------------------------------

    [Test]
    [Category("EndToEnd")]
    public void EnumeratePdbReferencesTest()
    {
        const string etwFilePath = @".\traces\BthPS3_0.etl";

        IReadOnlyCollection<PdbMetaData> refs = EtwUtil.EnumeratePdbReferences([etwFilePath]);

        IList<string> pdbNames = refs.Select(r => Path.GetFileName(r.PdbName).ToLowerInvariant()).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(refs,     Is.Not.Empty);
            Assert.That(pdbNames, Does.Contain("bthps3.pdb"));
            Assert.That(pdbNames, Does.Contain("bthps3psm.pdb"));
        }
    }

    // -----------------------------------------------------------------------
    // Symbol-server integration (opt-in via environment variable)
    // -----------------------------------------------------------------------

    [Test]
    [Category("Integration")]
    public async Task SymbolServerDownloadTest()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("RUN_INTEGRATION_TESTS"), "true",
                StringComparison.OrdinalIgnoreCase))
        {
            Assert.Ignore("Set RUN_INTEGRATION_TESTS=true to run integration tests.");
        }

        const string etwFilePath = @".\traces\BthPS3_0.etl";

        ServiceCollection services = new();
        services.AddHttpClient();
        ServiceProvider provider = services.BuildServiceProvider();
        IHttpClientFactory factory = provider.GetRequiredService<IHttpClientFactory>();
        HttpClient client = factory.CreateClient();
        client.BaseAddress = new Uri("https://symbols.nefarius.at/");
        client.Timeout = TimeSpan.FromSeconds(30);

        IReadOnlyCollection<PdbMetaData> pdbRefs = EtwUtil.EnumeratePdbReferences([etwFilePath]);

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));

        List<DecodingContextType> decodingTypes = [];
        foreach (PdbMetaData pdbMetaData in pdbRefs)
        {
            using HttpResponseMessage response =
                await client.GetAsync(pdbMetaData.DownloadPath, cts.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            using MemoryStream memory = new();
            await response.Content.CopyToAsync(memory, cts.Token).ConfigureAwait(false);
            memory.Position = 0;
            decodingTypes.Add(new PdbFileDecodingContextType(memory));
        }

        DecodingContext decodingContext = new(decodingTypes);

        JsonWriterOptions options = new() { Indented = true };
        using MemoryStream ms = new();
        await using Utf8JsonWriter jsonWriter = new(ms, options);

        bool success = EtwUtil.ConvertToJson(jsonWriter, [etwFilePath], converterOptions =>
        {
            converterOptions.WppDecodingContext = decodingContext;
        });

        Assert.That(success, Is.True);

        ms.Position = 0;
        await using FileStream outFile = File.Create("BthPS3_0_server.json");
        await ms.CopyToAsync(outFile, cts.Token).ConfigureAwait(false);
    }
}
