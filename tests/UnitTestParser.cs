using System.Text.Json;

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
        string etwFilePath = @".\traces\VPadRuntime.etl";

        JsonWriterOptions options = new() { Indented = true };

        using MemoryStream ms = new();
        using Utf8JsonWriter jsonWriter = new(ms, options);
        using DecodingContext decodingContext = new(new PdbFilesDecodingContextType(@"D:\Downloads\tmftest\nssvpd.pdb"));

        if (!EtwUtil.ConvertToJson(jsonWriter, [etwFilePath], converterOptions =>
            {
                // ReSharper disable once AccessToDisposedClosure
                converterOptions.DecodingContext = decodingContext; 
            }))
        {
            Assert.Fail();
        }

        ms.Seek(0, SeekOrigin.Begin);

        using FileStream outFile = File.OpenWrite("output.json");
        ms.CopyTo(outFile);

        Assert.Pass();
    }
}