using System.Collections.ObjectModel;

using Nefarius.Utilities.ETW.Deserializer.WPP;
using Nefarius.Utilities.ETW.Deserializer.WPP.TMF;

namespace Nefarius.Utilities.ETW.Tests;

[Category("Unit")]
public class WppEnumTests
{
    private static FunctionParameter Enum(ItemType type, params (long Value, string Name)[] values)
    {
        return new FunctionParameter
        {
            Expression = "x",
            Type = type,
            Index = 1,
            EnumTypeName = "MyEnum",
            EnumValues = new ReadOnlyDictionary<long, string>(values.ToDictionary(v => v.Value, v => v.Name))
        };
    }

    [Test]
    public void ItemEnum_UsesNamesFromPdbMetadataAndFallsBackToTheRawValue()
    {
        FunctionParameter parameter = Enum(ItemType.ItemEnum, (1, "Red"), (2, "Green"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(WppFormatter.ItemToString(parameter, 2u), Is.EqualTo("Green"));
            Assert.That(WppFormatter.ItemToString(parameter, 9u), Is.EqualTo("9"));
            Assert.That(WppFormatter.ItemToString(new FunctionParameter { Expression = "x", Type = ItemType.ItemEnum, Index = 1 }, 2u),
                Is.EqualTo("2"));
        }
    }

    [Test]
    public void ItemFlagsEnum_ComposesNamesAndKeepsUnknownBits()
    {
        FunctionParameter parameter = Enum(ItemType.ItemFlagsEnum, (0, "None"), (1, "Read"), (2, "Write"), (3, "ReadWrite"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(WppFormatter.ItemToString(parameter, 0u), Is.EqualTo("None"));
            Assert.That(WppFormatter.ItemToString(parameter, 3u), Is.EqualTo("ReadWrite"));
            Assert.That(WppFormatter.ItemToString(parameter, 1u), Is.EqualTo("Read"));
            Assert.That(WppFormatter.ItemToString(parameter, 0x11u), Is.EqualTo("Read | 0x10"));
            Assert.That(WppFormatter.ItemToString(parameter, 0x10u), Is.EqualTo("0x00000010"));
        }
    }

    [Test]
    public void TmfEnumParameters_KeepTheirTypeName()
    {
        string path = Path.Combine(Path.GetTempPath(), $"enum-{Guid.NewGuid():N}.tmf");
        File.WriteAllText(path, string.Join('\n',
            "11111111-2222-3333-4444-555555555555 Sample // SRC=sample.c MJ=1 MN=0",
            "#typev sample_c_10 10 \"%0 mode=%10!s!\" // LEVEL=TRACE_LEVEL_INFORMATION FLAGS=FLAG_ALL FUNC=Work",
            "{",
            "mode, ItemEnum(MyEnum) -- 10",
            "}",
            ""));
        try
        {
            TraceMessageFormat format = new TmfFileDecodingContextType(path).TraceMessageFormats.Single();
            FunctionParameter parameter = format.FunctionParameters.Single();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(parameter.Type, Is.EqualTo(ItemType.ItemEnum));
                Assert.That(parameter.EnumTypeName, Is.EqualTo("MyEnum"));
                Assert.That(parameter.EnumValues, Is.Null);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void PdbEnumReader_ReadsEnumeratorsFromTheTypeStream()
    {
        using FileStream stream = File.OpenRead("./symbols/BthPS3.pdb");
        using Kaitai.KaitaiStream kaitai = new(stream);
        Kaitai.MsPdb pdb = new(kaitai);

        Dictionary<string, IReadOnlyDictionary<long, string>> enums =
            PdbEnumReader.Read(pdb, new HashSet<string> { "_WDF_EXECUTION_LEVEL", "NoSuchEnum" });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(enums.Keys, Is.EqualTo(new[] { "_WDF_EXECUTION_LEVEL" }));
                        Assert.That(enums["_WDF_EXECUTION_LEVEL"][2], Is.EqualTo("WdfExecutionLevelPassive"));
            Assert.That(enums["_WDF_EXECUTION_LEVEL"], Has.Count.EqualTo(4));
        }
    }

    [Test]
    public void WppEventsWithoutADecodingContext_AreReportedAsMissingInsteadOfDropped()
    {
        List<Guid> missing = [];
        using MemoryStream ms = new();
        using (System.Text.Json.Utf8JsonWriter writer = new(ms))
        {
            Assert.That(EtwUtil.ConvertToJson(writer, ["./traces/BthPS3_0.etl"], o =>
            {
                o.Backend = EtwBackend.Managed;
                o.OnWppFormatMissing = (guid, _, _) => missing.Add(guid);
            }), Is.True);
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(missing, Is.Not.Empty);
            Assert.That(System.Text.Encoding.UTF8.GetString(ms.ToArray()), Does.Contain("No format information found."));
        }
    }
}