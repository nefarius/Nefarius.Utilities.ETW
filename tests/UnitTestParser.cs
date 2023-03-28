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
        string etwFilePath = @"C:\ProgramData\ViGEmRuntime.etl";

        MemoryStream ms = new();
        Utf8JsonWriter jsonWriter = new(ms);

        if (!EtwUtil.ConvertToJson(jsonWriter, new[] { etwFilePath }, error =>
            {
                Assert.Fail();
            }))
        {
            Assert.Fail();
        }

        ms.Seek(0, SeekOrigin.Begin);

        var sr = new StreamReader(ms);

        var json = sr.ReadToEnd();

        Assert.Pass();
    }
}