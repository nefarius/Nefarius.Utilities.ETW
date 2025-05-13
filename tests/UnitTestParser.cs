using System.Text.Json;

using Kaitai;

using Nefarius.Utilities.ETW;
using Nefarius.Utilities.ETW.Deserializer.WPP;
using Nefarius.Utilities.ETW.Deserializer.WPP.TMF;

namespace EtwTestProject;

public class Tests
{
    [SetUp]
    public void Setup()
    {
    }

    [Test]
    public void TmfFileParserTest()
    {
        Parser parser = new();

        IReadOnlyList<TraceMessageFormat> result = parser.ParseDirectory(Path.GetFullPath(@".\symbols"));

        Assert.That(result, Has.Count.EqualTo(1253));
    }

    [Test]
    public void PdbFileParserTest()
    {
        string testPdb = Path.GetFullPath(@".\symbols\BthPS3.pdb");

        MsPdb pdb = new(new KaitaiStream(File.OpenRead(testPdb)));

        IEnumerable<MsPdb.DbiSymbol> tmfAnnotations = pdb.DbiStream.ModulesList.Items
            .SelectMany(m => m.ModuleData.SymbolsList.Items)
            .Where(s => s.Data.Body is MsPdb.SymAnnotation sa &&
                        sa.Strings.FirstOrDefault()?.Contains("TMF:") == true);

        IEnumerable<string> formatBlocks = tmfAnnotations
            .Select(a => string.Join(Environment.NewLine,
                ((MsPdb.SymAnnotation)a.Data.Body).Strings.Where(s => !s.Contains("TMF:"))));

        string example = formatBlocks.ToList()[200];
    }

    [Test]
    public void WppTraceDecodingTest()
    {
        string etwFilePath = @".\traces\BthPS3.etl";

        JsonWriterOptions options = new() { Indented = true };

        using MemoryStream ms = new();
        using Utf8JsonWriter jsonWriter = new(ms, options);
        using DecodingContext decodingContext = new(new PdbFilesDecodingContextType(
            @".\symbols\BthPS3.pdb",
            @".\symbols\BthPS3PSM.pdb"
        ));

        if (!EtwUtil.ConvertToJson(jsonWriter, [etwFilePath], converterOptions =>
            {
                // ReSharper disable once AccessToDisposedClosure
                converterOptions.WppDecodingContext = decodingContext;
            }))
        {
            Assert.Fail();
        }

        ms.Seek(0, SeekOrigin.Begin);

        using FileStream outFile = File.OpenWrite("BthPS3.json");
        ms.CopyTo(outFile);

        Assert.Pass();
    }
}