using System.Collections.ObjectModel;
using System.Text.Json;

using Kaitai;

using Nefarius.Utilities.ETW;
using Nefarius.Utilities.ETW.Deserializer.WPP;
using Nefarius.Utilities.ETW.Deserializer.WPP.TMF;

namespace EtwTestProject;

public class Tests
{
    private const int ExpectedTypesCount = 1253;

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
        IReadOnlyList<TraceMessageFormat> result = ExtractFromFormatFiles();

        TraceMessageFormat sample = result.Single(format => format.Opcode.Equals("Bluetooth_Context_c149"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sample, Is.EqualTo(ExpectedSampleType));
            Assert.That(result, Has.Count.EqualTo(ExpectedTypesCount));
        }
    }

    private static IReadOnlyList<TraceMessageFormat> ExtractFromFormatFiles()
    {
        TmfParser tmfParser = new();

        IReadOnlyList<TraceMessageFormat> result = tmfParser.ParseDirectory(Path.GetFullPath(@".\symbols"));

        return result;
    }

    /// <summary>
    ///     Parses TMF information directly from PDBs.
    /// </summary>
    [Test]
    public void PdbFileParserTest()
    {
        ReadOnlyCollection<TraceMessageFormat> result = ExtractFromSymbolFiles();

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
        IReadOnlyList<TraceMessageFormat> lhs = ExtractFromFormatFiles();
        ReadOnlyCollection<TraceMessageFormat> rhs = ExtractFromSymbolFiles();

        var t1 = lhs.Where(x => x.Opcode.Equals("Dmf_CrashDump_c3719"));
        var t2 = rhs.Where(x => x.Opcode.Equals("Dmf_CrashDump_c3719"));
        
        var diff = lhs.Except(rhs).ToList();

        // Assert.That(lhs, Is.EquivalentTo(rhs));
    }

    private static ReadOnlyCollection<TraceMessageFormat> ExtractFromSymbolFiles()
    {
        string profilePdbPath = Path.GetFullPath(@".\symbols\BthPS3.pdb");
        string filterPdbPath = Path.GetFullPath(@".\symbols\BthPS3PSM.pdb");

        TmfParser parser = new();

        MsPdb profilePdb = new(new KaitaiStream(File.OpenRead(profilePdbPath)));

        IEnumerable<SymProc32AnnotationPair> profileAnnotations = profilePdb
            .DbiStream.ModulesList.Items
            .SelectMany(m => m.ModuleData.SymbolsList.Items)
            .ToList()
            .ExtractTmfAnnotations();

        List<TraceMessageFormat> profileRefined = parser
            .ExtractTraceMessageFormats(profileAnnotations)
            .ToList();

        MsPdb filterPdb = new(new KaitaiStream(File.OpenRead(filterPdbPath)));

        IEnumerable<SymProc32AnnotationPair> filterAnnotations = filterPdb
            .DbiStream.ModulesList.Items
            .SelectMany(m => m.ModuleData.SymbolsList.Items)
            .ToList()
            .ExtractTmfAnnotations();

        List<TraceMessageFormat> filterRefined = parser
            .ExtractTraceMessageFormats(filterAnnotations)
            .ToList();

        List<TraceMessageFormat> result = profileRefined
            .Concat(filterRefined)
            .Distinct()
            .ToList();

        return result.AsReadOnly();
    }

    /// <summary>
    ///     Decodes a sample .etl file with TMFs from PDBs.
    /// </summary>
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