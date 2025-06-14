using System.Collections.ObjectModel;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using Nefarius.Utilities.ETW.Deserializer.WPP;
using Nefarius.Utilities.ETW.Deserializer.WPP.TMF;

namespace Nefarius.Utilities.ETW.Tests;

public class Tests
{
    private const int ExpectedTypesCount = 2610;

    private static readonly TraceMessageFormat ExpectedSampleType = new()
    {
        FileName = "Bluetooth.Context.c",
        Flags = "TRACE_BTH",
        Function = "BthPS3_DeviceContextHeaderInit",
        Id = 12,
        Level = "TRACE_LEVEL_VERBOSE",
        MessageFormat = "%0 [%!FUNC!] <-- Exit <status=%10!s!>",
        MessageGuid = Guid.Parse("e4b27b5e-24d0-369f-a4b5-23228e160bd2"),
        Opcode = "Bluetooth_Context_c149",
        Provider = "BthPS3"
    };

    [SetUp]
    public void Setup()
    {
    }

    /// <summary>
    ///     Parses all found .tmf files.
    /// </summary>
    [Test]
    public void TmfFileParserTest()
    {
        IReadOnlyList<TraceMessageFormat> result = Shared.ExtractFromFormatFiles();

        TraceMessageFormat sample = result.Single(format => format.Opcode.Equals("Bluetooth_Context_c149"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sample, Is.EqualTo(ExpectedSampleType));
            Assert.That(result, Has.Count.EqualTo(ExpectedTypesCount));
        }
    }

    /// <summary>
    ///     Parses TMF information directly from PDBs.
    /// </summary>
    [Test]
    public void PdbFileParserTest()
    {
        ReadOnlyCollection<TraceMessageFormat> result = Shared.ExtractFromSymbolFiles();

        TraceMessageFormat sample = result.Single(format => format.Opcode.Equals("Bluetooth_Context_c149"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sample, Is.EqualTo(ExpectedSampleType));
            Assert.That(result, Has.Count.EqualTo(ExpectedTypesCount));
        }
    }

    [Test]
    public void PlausibilityTest()
    {
        IReadOnlyList<TraceMessageFormat> lhs = Shared.ExtractFromFormatFiles();
        ReadOnlyCollection<TraceMessageFormat> rhs = Shared.ExtractFromSymbolFiles();

        List<string> formats = rhs.Select(s => s.MessageFormat).ToList();
        List<ItemType> types = rhs
            .SelectMany(format => format.FunctionParameters)
            .Select(p => p.Type)
            .Distinct()
            .ToList();
        List<string> listItems = rhs
            .SelectMany(format => format.FunctionParameters)
            .Where(p => p is { Type: ItemType.ItemListByte, ListItems: not null })
            .SelectMany(p => p.ListItems!)
            .Select(p => p.Value)
            .Distinct()
            .ToList();

        //var t1 = lhs.Where(x => x.MessageGuid.Equals(Guid.Parse("49c0500c-96ae-35e4-0b57-99f5eded038e")));
        //var t2 = rhs.Where(x => x.MessageGuid.Equals(Guid.Parse("49c0500c-96ae-35e4-0b57-99f5eded038e")));

        //var diff = lhs.Except(rhs).ToList();

        Assert.That(lhs, Is.EquivalentTo(rhs));
    }


    /// <summary>
    ///     Decodes a sample .etl file with TMFs from PDBs.
    /// </summary>
    [Test]
    public void BthPs3EtlTraceDecodingTest()
    {
        Assert.That(Shared.BthPs3EtlTraceDecoding(), Is.True);
    }

    [Test]
    public void BthPs3EtlTraceIncompleteContextTest()
    {
        const string etwFilePath = @".\traces\BthPS3_0.etl";

        JsonWriterOptions options = new() { Indented = true };

        using MemoryStream ms = new();
        using Utf8JsonWriter jsonWriter = new(ms, options);
        DecodingContext decodingContext = new(PdbFileDecodingContextType.CreateFrom(
            @".\symbols\BthPS3.pdb"
        ));

        if (!EtwUtil.ConvertToJson(jsonWriter, [etwFilePath], converterOptions =>
            {
                converterOptions.WppDecodingContext = decodingContext;
            }))
        {
            Assert.Fail();
        }

        ms.Seek(0, SeekOrigin.Begin);

        using FileStream outFile = File.OpenWrite("BthPS3_0_partial.json");
        ms.CopyTo(outFile);

        Assert.Pass();
    }

    [Test]
    public void SymbolServerDownloadTest()
    {
        const string etwFilePath = @".\traces\BthPS3_0.etl";

        JsonWriterOptions options = new() { Indented = true };

        using MemoryStream ms = new();
        using Utf8JsonWriter jsonWriter = new(ms, options);

        ServiceCollection services = new();
        services.AddHttpClient();
        ServiceProvider provider = services.BuildServiceProvider();

        IHttpClientFactory factory = provider.GetRequiredService<IHttpClientFactory>();
        HttpClient client = factory.CreateClient();
        client.BaseAddress = new Uri("http://192.168.2.12:5000");

        if (!EtwUtil.ConvertToJson(jsonWriter, [etwFilePath], converterOptions =>
            {
                converterOptions.ContextProviderLookup = pdbMetaData =>
                {
                    using Stream webStream = client.GetStreamAsync(pdbMetaData.DownloadPath).GetAwaiter().GetResult();
                    using MemoryStream memory = new();
                    // we cannot seek a web stream, so we need to cache it in memory first
                    webStream.CopyTo(memory);
                    memory.Position = 0;

                    return new PdbFileDecodingContextType(memory);
                };
            }))
        {
            Assert.Fail();
        }

        ms.Seek(0, SeekOrigin.Begin);

        using FileStream outFile = File.OpenWrite("BthPS3_0_server.json");
        ms.CopyTo(outFile);

        Assert.Pass();
    }

    [Test]
    public void DsHidMiniEtlTraceDecodingTest()
    {
        Assert.That(Shared.DsHidMiniEtlTraceDecoding(), Is.True);
    }
}