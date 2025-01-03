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
        string etwFilePath = @"C:\Users\Nefarius\Documents\WPR Files\WIN11-DEV-VM.01-03-2025.20-55-29.etl";

        using MemoryStream ms = new();
        using Utf8JsonWriter jsonWriter = new(ms);

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