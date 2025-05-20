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

    private static IEnumerable<(MsPdb.SymProc32 Proc, List<MsPdb.SymAnnotation> Annotations)> EnumerateGroups(
        List<MsPdb.DbiSymbol> symbols)
    {
        for (int i = 0; i < symbols.Count;)
        {
            if (symbols[i].Data.Body is MsPdb.SymProc32 proc)
            {
                List<MsPdb.SymAnnotation> annotations = new();
                i++; // Advance to check for annotations

                while (i < symbols.Count)
                {
                    if (symbols[i].Data.Body is MsPdb.SymAnnotation annotation)
                    {
                        annotations.Add(annotation);
                        i++;
                    }
                    else if (symbols[i].Data.Body is MsPdb.SymProc32)
                    {
                        // Next SymProc32 encountered – break to process it on the next loop iteration
                        break;
                    }
                    else
                    {
                        // Skip unrelated symbol types
                        i++;
                    }
                }

                yield return (proc, annotations);
            }
            else
            {
                // Skip unrelated symbol types
                i++;
            }
        }
    }

    private static IEnumerable<TraceMessageFormat> ExtractTraceMessageFormats(
        IEnumerable<(MsPdb.SymProc32 Proc, List<MsPdb.SymAnnotation> Annotations)> groups)
    {
        Parser parser = new();

        foreach ((MsPdb.SymProc32 proc, List<MsPdb.SymAnnotation> annotations) in groups)
        {
            foreach (string block in annotations.Select(annotation =>
                         string.Join(Environment.NewLine, annotation.Strings.Skip(1))))
            {
                using StringReader sr = new(block);
                IReadOnlyList<TraceMessageFormat> results = parser.ParseFile(sr, proc.Name.Value);
                if (results.Any())
                {
                    yield return results[0];
                }
            }
        }
    }

    [Test]
    public void PdbFileParserTest()
    {
        string testPdb = Path.GetFullPath(@".\symbols\BthPS3.pdb");

        MsPdb pdb = new(new KaitaiStream(File.OpenRead(testPdb)));

        IEnumerable<(MsPdb.SymProc32 Proc, List<MsPdb.SymAnnotation> Annotations)> groups = EnumerateGroups(pdb
                .DbiStream.ModulesList.Items
                .SelectMany(m => m.ModuleData.SymbolsList.Items).ToList())
            .Where((tuple, _) => tuple.Annotations.Count != 0 &&
                                 tuple.Annotations.Any(annotation => annotation.Strings.Contains("TMF:")));

        List<TraceMessageFormat> refined = ExtractTraceMessageFormats(groups).ToList();
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