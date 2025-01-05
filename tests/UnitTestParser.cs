using System.Text.Json;

using Nefarius.Utilities.ETW;

namespace EtwTestProject;

public class Tests
{
    [SetUp]
    public void Setup()
    {
    }

    [Test]
    public void Test1()
    {
        string etwFilePath = @".\traces\VPadRuntime.etl";

        var options = new JsonWriterOptions
        {
            Indented = true
        };
        
        using MemoryStream ms = new();
        using Utf8JsonWriter jsonWriter = new(ms, options);

        if (!EtwUtil.ConvertToJson(jsonWriter, [etwFilePath], error =>
            {
                Assert.Fail();
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