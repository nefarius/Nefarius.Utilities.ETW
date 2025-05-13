using System.Text.Json;

using Kaitai;

using Nefarius.Utilities.ETW;
using Nefarius.Utilities.ETW.Deserializer.WPP;

namespace EtwTestProject;

public class Tests
{
    [SetUp]
    public void Setup()
    {
    }

    [Test]
    public void WppTraceDecodingTest()
    {
        string testPdb = Path.GetFullPath(@".\symbols\BthPS3.pdb");

        MsPdb pdb = new(new KaitaiStream(File.OpenRead(testPdb)));

        MsPdb.UModuleInfo mi = pdb.DbiStream.ModulesList.Items.First();
        IEnumerable<MsPdb.DbiSymbol> annotations =
            mi.ModuleData.SymbolsList.Items.Where(s => s.Data.Body is MsPdb.SymAnnotation);
        IEnumerable<MsPdb.DbiSymbol> tmfAnnotations =
            annotations.Where(a =>
                ((MsPdb.SymAnnotation)a.Data.Body).Strings.FirstOrDefault()?.Contains("TMF:") ?? false);

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