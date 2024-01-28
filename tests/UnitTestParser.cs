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
        string etwFilePath = @"C:\VPadRuntime.etl";

        MemoryStream ms = new();
        Utf8JsonWriter jsonWriter = new(ms);

        if (!EtwUtil.ConvertToJson(jsonWriter, new[] { etwFilePath }, error =>
                {
                    Assert.Fail();
                },
                guid =>
                {
                    switch (guid.ToString().ToUpper())
                    {
                        case "021B2C3C-9DD6-4C0A-A53A-6183F1BE11A0":
                            return File.OpenRead(
                                @"D:\Development\git.nefarius.at\ViGEm Framework\library\VPadRuntimeETW.man");
                        case "AFEBAD70-D5DB-4A74-BDA2-764D2A875AAF":
                            // this should never hit since WPP doesn't use a manifest
                            throw new NotImplementedException();
                        default:
                            return null;
                    }
                }))
        {
            Assert.Fail();
        }

        ms.Seek(0, SeekOrigin.Begin);

        StreamReader sr = new(ms);

        string json = sr.ReadToEnd();

        Assert.Pass();
    }
}