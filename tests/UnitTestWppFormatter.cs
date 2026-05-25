using System.Collections.ObjectModel;

using Nefarius.Utilities.ETW.Deserializer.WPP;
using Nefarius.Utilities.ETW.Deserializer.WPP.TMF;

namespace Nefarius.Utilities.ETW.Tests;

/// <summary>
///     Unit tests for <see cref="WppFormatter" /> — the pure formatting logic extracted from
///     <c>WppEventRecord</c>.  These tests run without any file I/O or Windows ETW APIs.
/// </summary>
/// <remarks>
///     <see cref="WppFormatter" /> is <c>internal</c>; access is granted via
///     <c>[assembly: InternalsVisibleTo("Nefarius.Utilities.ETW.Tests")]</c>
///     in <c>src/AssemblyInfo.cs</c>.
/// </remarks>
[Category("Unit")]
public class WppFormatterTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static TraceMessageFormat MakeFormat(string messageFormat, params FunctionParameter[] parameters)
    {
        return new TraceMessageFormat
        {
            MessageGuid   = Guid.NewGuid(),
            Provider      = "TestProvider",
            FileName      = "test.c",
            Opcode        = "TestOpcode",
            Id            = 1,
            MessageFormat = messageFormat,
            Level         = "TRACE_LEVEL_VERBOSE",
            Flags         = "TRACE_TEST",
            Function      = "TestFunction",
            FunctionParameters = parameters
        };
    }

    private static IReadOnlyDictionary<int, (FunctionParameter, object)> Params(
        params (int Index, FunctionParameter Param, object Value)[] entries)
    {
        return entries.ToDictionary(
            e => e.Index,
            e => (e.Param, e.Value));
    }

    private static FunctionParameter MakeParam(ItemType type, int index, string? expression = null)
    {
        return new FunctionParameter
        {
            Expression = expression ?? "x",
            Type       = type,
            Index      = index
        };
    }

    // -----------------------------------------------------------------------
    // WppFormatter.Substitute — basic cases
    // -----------------------------------------------------------------------

    [Test]
    public void Substitute_ReturnsMessageVerbatim_WhenNoParameters()
    {
        TraceMessageFormat format = MakeFormat("Hello, world.");

        string result = WppFormatter.Substitute(format, new Dictionary<int, (FunctionParameter, object)>());

        Assert.That(result, Is.EqualTo("Hello, world."));
    }

    [Test]
    public void Substitute_ReplacesStringPlaceholder()
    {
        FunctionParameter param = MakeParam(ItemType.ItemString, index: 1);
        TraceMessageFormat format = MakeFormat("Value: %1!s!", param);

        string result = WppFormatter.Substitute(
            format,
            Params((1, param, "hello")));

        Assert.That(result, Is.EqualTo("Value: hello"));
    }

    [Test]
    public void Substitute_LeavesPlaceholderIntact_WhenIndexMissing()
    {
        FunctionParameter param = MakeParam(ItemType.ItemLong, index: 1);
        TraceMessageFormat format = MakeFormat("A=%1!d! B=%2!d!", param);

        // Only index 1 is provided; index 2 is absent.
        string result = WppFormatter.Substitute(
            format,
            Params((1, param, 42)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Does.Contain("42"));
            Assert.That(result, Does.Contain("%2!d!"), "Missing placeholder must remain unchanged.");
        }
    }

    // -----------------------------------------------------------------------
    // Hex specifiers
    // -----------------------------------------------------------------------

    [Test]
    public void Substitute_FormatsHexValue_WithUppercaseX()
    {
        FunctionParameter param = MakeParam(ItemType.ItemLong, index: 1);
        TraceMessageFormat format = MakeFormat("%1!X!", param);

        string result = WppFormatter.Substitute(format, Params((1, param, (int)0xABCD)));

        Assert.That(result, Is.EqualTo("ABCD"));
    }

    [Test]
    public void Substitute_FormatsHexValue_WithZeroPadding()
    {
        FunctionParameter param = MakeParam(ItemType.ItemLong, index: 2);
        TraceMessageFormat format = MakeFormat("%2!04X!", param);

        string result = WppFormatter.Substitute(format, Params((2, param, (int)0xFF)));

        Assert.That(result, Is.EqualTo("00FF"));
    }

    [Test]
    public void Substitute_Preserves0xPrefix_WhenPresentInFormatString()
    {
        FunctionParameter param = MakeParam(ItemType.ItemLong, index: 1);
        TraceMessageFormat format = MakeFormat("addr=0x%1!X!", param);

        string result = WppFormatter.Substitute(format, Params((1, param, (int)0x1234)));

        Assert.That(result, Is.EqualTo("addr=0x1234"));
    }

    [Test]
    public void Substitute_DoesNotDoublePrefix_When0xAlreadyPresent()
    {
        // The regex captures the "0x" before the % sign; the formatter then produces "0x..." itself.
        // The result must not be "0x0x...".
        FunctionParameter param = MakeParam(ItemType.ItemLong, index: 1);
        TraceMessageFormat format = MakeFormat("0x%1!X!", param);

        string result = WppFormatter.Substitute(format, Params((1, param, (int)0xBEEF)));

        Assert.That(result, Does.Not.Contain("0x0x"));
    }

    // -----------------------------------------------------------------------
    // Decimal and unsigned specifiers
    // -----------------------------------------------------------------------

    [Test]
    public void Substitute_FormatsDecimalInteger()
    {
        FunctionParameter param = MakeParam(ItemType.ItemLong, index: 3);
        TraceMessageFormat format = MakeFormat("count=%3!d!", param);

        string result = WppFormatter.Substitute(format, Params((3, param, 123)));

        Assert.That(result, Is.EqualTo("count=123"));
    }

    [Test]
    public void Substitute_FormatsUnsignedInteger_WithLowercaseU()
    {
        // WPP "%u" = unsigned decimal; .NET has no "U" specifier so it must be mapped to "D".
        FunctionParameter param = MakeParam(ItemType.ItemLong, index: 1);
        TraceMessageFormat format = MakeFormat("%1!u!", param);

        string result = WppFormatter.Substitute(format, Params((1, param, 4294967295U)));

        Assert.That(result, Is.EqualTo("4294967295"));
    }

    [Test]
    public void Substitute_FormatsUnsignedInteger_WithUppercaseU()
    {
        // WPP also emits "%U" (uppercase); the regex must match it and the specifier
        // mapping must translate it to .NET's "D", not leave it as "U" (which is invalid).
        FunctionParameter param = MakeParam(ItemType.ItemLong, index: 1);
        TraceMessageFormat format = MakeFormat("%1!U!", param);

        string result = WppFormatter.Substitute(format, Params((1, param, 4294967295U)));

        Assert.That(result, Is.EqualTo("4294967295"));
    }

    [Test]
    public void Substitute_FormatsUnsignedInteger_WithZeroPad()
    {
        // Verify zero-padding still works when the specifier is "u".
        FunctionParameter param = MakeParam(ItemType.ItemLong, index: 1);
        TraceMessageFormat format = MakeFormat("%1!08u!", param);

        string result = WppFormatter.Substitute(format, Params((1, param, 42U)));

        Assert.That(result, Is.EqualTo("00000042"));
    }

    // -----------------------------------------------------------------------
    // Format-error fallback
    // -----------------------------------------------------------------------

    [Test]
    public void Substitute_ReturnsFallbackString_OnFormatException()
    {
        // "%1!q!" — "q" is not in [Xxdu] so NumericFormatTokenRegex does not match,
        // falling through to string.Format("{0:q}", 42).  "q" is not a valid composite
        // format specifier for Int32, so .NET throws FormatException, which is caught
        // and turned into the "<format error…>" fallback string.
        FunctionParameter param = MakeParam(ItemType.ItemLong, index: 1);
        TraceMessageFormat format = MakeFormat("%1!q!", param);

        string result = WppFormatter.Substitute(
            format,
            Params((1, param, 42)));

        Assert.That(result, Does.StartWith("<format error for %1!"));
    }

    // -----------------------------------------------------------------------
    // WppFormatter.ItemToString — NTSTATUS
    // -----------------------------------------------------------------------

    [Test]
    public void ItemToString_TranslatesKnownNtStatus()
    {
        FunctionParameter param = MakeParam(ItemType.ItemNTSTATUS, index: 1);

        string result = WppFormatter.ItemToString(param, (uint)0x00000000); // STATUS_SUCCESS

        Assert.That(result, Does.Contain("STATUS_SUCCESS"));
        Assert.That(result, Does.Contain("0x00000000"));
    }

    [Test]
    public void ItemToString_ReturnsUnknownMessage_ForUnrecognizedNtStatus()
    {
        FunctionParameter param = MakeParam(ItemType.ItemNTSTATUS, index: 1);

        string result = WppFormatter.ItemToString(param, (uint)0xDEADBEEF);

        Assert.That(result, Does.StartWith("Unknown NTSTATUS Error code: 0x"));
    }

    // -----------------------------------------------------------------------
    // WppFormatter.ItemToString — WINERROR
    // -----------------------------------------------------------------------

    [Test]
    public void ItemToString_TranslatesKnownWinError()
    {
        FunctionParameter param = MakeParam(ItemType.ItemWINERROR, index: 1);

        string result = WppFormatter.ItemToString(param, (uint)0); // ERROR_SUCCESS

        Assert.That(result, Is.Not.Null.And.Not.Empty);
    }

    // -----------------------------------------------------------------------
    // WppFormatter.ItemToString — HRESULT
    // -----------------------------------------------------------------------

    [Test]
    public void ItemToString_TranslatesSuccessHResult()
    {
        FunctionParameter param = MakeParam(ItemType.ItemHRESULT, index: 1);

        // S_OK = 0 — Marshal.GetExceptionForHR returns null for success codes, so we
        // expect the fallback "Unknown HRESULT" message rather than throwing.
        string result = WppFormatter.ItemToString(param, 0);

        Assert.That(result, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void ItemToString_TranslatesFailureHResult()
    {
        FunctionParameter param = MakeParam(ItemType.ItemHRESULT, index: 1);

        string result = WppFormatter.ItemToString(param, unchecked((int)0x80004005)); // E_FAIL

        Assert.That(result, Is.Not.Null.And.Not.Empty);
    }

    // -----------------------------------------------------------------------
    // WppFormatter.ItemToString — ItemListByte (enum lookup)
    // -----------------------------------------------------------------------

    [Test]
    public void ItemToString_ResolvesListByteToEnumString()
    {
        var listItems = new ReadOnlyDictionary<int, string>(
            new Dictionary<int, string>
            {
                [0] = "Low",
                [1] = "Normal",
                [2] = "High"
            });

        FunctionParameter param = new()
        {
            Expression = "priority",
            Type       = ItemType.ItemListByte,
            Index      = 1,
            ListItems  = listItems
        };

        string result = WppFormatter.ItemToString(param, (byte)1);

        Assert.That(result, Is.EqualTo("Normal"));
    }

    // -----------------------------------------------------------------------
    // WppFormatter.PlaceholderRegex — smoke tests
    // -----------------------------------------------------------------------

    [Test]
    public void PlaceholderRegex_MatchesTypicalWppPlaceholder()
    {
        System.Text.RegularExpressions.Match m = WppFormatter.PlaceholderRegex().Match("%1!s!");

        Assert.That(m.Success, Is.True);
        Assert.That(m.Groups[2].Value, Is.EqualTo("1"));
        Assert.That(m.Groups[3].Value, Is.EqualTo("s"));
    }

    [Test]
    public void PlaceholderRegex_Captures0xPrefix()
    {
        System.Text.RegularExpressions.Match m = WppFormatter.PlaceholderRegex().Match("0x%1!X!");

        Assert.That(m.Success, Is.True);
        Assert.That(m.Groups[1].Success, Is.True);
    }

    // -----------------------------------------------------------------------
    // WppFormatter.SubstituteContext — context markers and %0 stripping
    // -----------------------------------------------------------------------

    [Test]
    public void SubstituteContext_ReplacesFunc()
    {
        TraceMessageFormat format = MakeFormat("[%!FUNC!] message");

        string result = WppFormatter.SubstituteContext(format, format.MessageFormat);

        Assert.That(result, Is.EqualTo("[TestFunction] message"));
    }

    [Test]
    public void SubstituteContext_ReplacesLevel()
    {
        TraceMessageFormat format = MakeFormat("level=%!LEVEL! msg");

        string result = WppFormatter.SubstituteContext(format, format.MessageFormat);

        Assert.That(result, Is.EqualTo("level=TRACE_LEVEL_VERBOSE msg"));
    }

    [Test]
    public void SubstituteContext_ReplacesFlags()
    {
        TraceMessageFormat format = MakeFormat("flags=%!FLAGS!");

        string result = WppFormatter.SubstituteContext(format, format.MessageFormat);

        Assert.That(result, Is.EqualTo("flags=TRACE_TEST"));
    }

    [Test]
    public void SubstituteContext_ReplacesKeywordsAsAliasOfFlags()
    {
        TraceMessageFormat format = MakeFormat("kw=%!KEYWORDS!");

        string result = WppFormatter.SubstituteContext(format, format.MessageFormat);

        Assert.That(result, Is.EqualTo("kw=TRACE_TEST"));
    }

    [Test]
    public void SubstituteContext_ReplacesFile()
    {
        TraceMessageFormat format = MakeFormat("src=%!FILE!");

        string result = WppFormatter.SubstituteContext(format, format.MessageFormat);

        Assert.That(result, Is.EqualTo("src=test.c"));
    }

    [Test]
    public void SubstituteContext_ReplacesLineWithEmpty()
    {
        TraceMessageFormat format = MakeFormat("line=%!LINE! end");

        string result = WppFormatter.SubstituteContext(format, format.MessageFormat);

        Assert.That(result, Is.EqualTo("line= end"));
    }

    [Test]
    public void SubstituteContext_LeavesUnknownMarkerIntact()
    {
        TraceMessageFormat format = MakeFormat("%!FOO! bar");

        string result = WppFormatter.SubstituteContext(format, format.MessageFormat);

        Assert.That(result, Is.EqualTo("%!FOO! bar"));
    }

    [Test]
    public void SubstituteContext_StripsLeadingStdPrefixWithSpace()
    {
        TraceMessageFormat format = MakeFormat("%0 [%!FUNC!] msg");

        string result = WppFormatter.SubstituteContext(format, format.MessageFormat);

        Assert.That(result, Is.EqualTo("[TestFunction] msg"));
    }

    [Test]
    public void SubstituteContext_StripsLeadingStdPrefixWithoutSpace()
    {
        TraceMessageFormat format = MakeFormat("%0[%!FUNC!] msg");

        string result = WppFormatter.SubstituteContext(format, format.MessageFormat);

        Assert.That(result, Is.EqualTo("[TestFunction] msg"));
    }

    [Test]
    public void SubstituteContext_DoesNotStrip_WhenPercent0_NotAtStart()
    {
        TraceMessageFormat format = MakeFormat("x%0 trailing");

        string result = WppFormatter.SubstituteContext(format, format.MessageFormat);

        // %0 not at the very start is left as-is
        Assert.That(result, Does.Contain("%0"));
    }

    // -----------------------------------------------------------------------
    // WppFormatter.Substitute — USEPREFIX-baked TMF strings (end-to-end)
    // -----------------------------------------------------------------------

    [Test]
    public void Substitute_RendersBakedInUserPrefix_WithNtStatusParam()
    {
        // Mirrors a real TMF produced by USEPREFIX(*, "%!STDPREFIX! [%!FUNC!] <--"):
        // #typev ... "%0 [%!FUNC!] <-- Exit <status=%10!s!>"
        FunctionParameter statusParam = MakeParam(ItemType.ItemNTSTATUS, index: 10);
        TraceMessageFormat format = MakeFormat(
            "%0 [%!FUNC!] <-- Exit <status=%10!s!>",
            statusParam);

        string result = WppFormatter.Substitute(
            format,
            Params((10, statusParam, (uint)0x00000000)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Does.Not.Contain("%0"));
            Assert.That(result, Does.Not.Contain("%!FUNC!"));
            Assert.That(result, Does.Contain("[TestFunction]"));
            Assert.That(result, Does.Contain("STATUS_SUCCESS"));
        }
    }

    // -----------------------------------------------------------------------
    // WppFormatter.ItemToString — new ItemType formatters
    // -----------------------------------------------------------------------

    [Test]
    public void ItemToString_FormatsIPv4()
    {
        FunctionParameter param = MakeParam(ItemType.ItemIPAddr, index: 1);

        // 192.168.1.1 in network byte order: bytes 0xC0 0xA8 0x01 0x01
        // As a little-endian UInt32 read from the wire that is 0x0101A8C0
        uint networkOrderValue = 0x0101A8C0;
        string result = WppFormatter.ItemToString(param, networkOrderValue);

        Assert.That(result, Is.EqualTo("192.168.1.1"));
    }

    [Test]
    public void ItemToString_FormatsPort()
    {
        FunctionParameter param = MakeParam(ItemType.ItemPort, index: 1);

        // Port 80 in network byte order is 0x5000 as a UInt16 (big-endian)
        ushort networkOrderPort = 0x5000;
        string result = WppFormatter.ItemToString(param, networkOrderPort);

        Assert.That(result, Is.EqualTo("80"));
    }

    [Test]
    public void ItemToString_FormatsTimestamp()
    {
        FunctionParameter param = MakeParam(ItemType.ItemTimestamp, index: 1);

        // Known FILETIME: 2000-01-01 00:00:00 UTC
        DateTime dt        = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        long     fileTime  = dt.ToFileTimeUtc();
        string   result    = WppFormatter.ItemToString(param, fileTime);

        // ISO-8601 round-trip: "2000-01-01T00:00:00.0000000Z"
        Assert.That(result, Does.StartWith("2000-01-01T00:00:00"));
    }

    [Test]
    public void ItemToString_FormatsWaitTime_AsDuration()
    {
        // %!due! uses ItemWaitTime and must produce day~h:mm:ss (same as %!delta!), not an absolute ISO timestamp
        FunctionParameter param = MakeParam(ItemType.ItemWaitTime, index: 1);

        long ms = (0L * 86400 + 0 * 3600 + 5 * 60 + 30) * 1000; // 5 min 30 sec
        string result = WppFormatter.ItemToString(param, ms);

        Assert.That(result, Is.EqualTo("0~0:05:30"));
    }

    [Test]
    public void ItemToString_FormatsTimeDelta()
    {
        FunctionParameter param = MakeParam(ItemType.ItemTimeDelta, index: 1);

        // 1 day, 2 hours, 3 minutes, 4 seconds = (1*86400 + 2*3600 + 3*60 + 4) * 1000 ms
        // WPP format is day~h:mm:ss — the hours component is NOT zero-padded
        long ms = (1L * 86400 + 2 * 3600 + 3 * 60 + 4) * 1000;
        string result = WppFormatter.ItemToString(param, ms);

        Assert.That(result, Is.EqualTo("1~2:03:04"));
    }

    [Test]
    public void ItemToString_FormatsClsid()
    {
        FunctionParameter param = MakeParam(ItemType.ItemCLSID, index: 1);
        Guid guid = new("12345678-1234-1234-1234-123456789ABC");

        string result = WppFormatter.ItemToString(param, guid);

        Assert.That(result, Is.EqualTo("{12345678-1234-1234-1234-123456789ABC}"));
    }

    [Test]
    public void ItemToString_FormatsChar4_Printable()
    {
        FunctionParameter param = MakeParam(ItemType.ItemChar4, index: 1);

        // 'WAVE' as a FOURCC = 0x45564157 (little-endian: W A V E)
        int wave = BitConverter.ToInt32(new byte[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' }, 0);
        string result = WppFormatter.ItemToString(param, wave);

        Assert.That(result, Is.EqualTo("WAVE"));
    }

    [Test]
    public void ItemToString_FormatsChar4_NonPrintableReplacedWithDot()
    {
        FunctionParameter param = MakeParam(ItemType.ItemChar4, index: 1);

        int value = BitConverter.ToInt32(new byte[] { 0x01, (byte)'K', 0xFF, (byte)'Z' }, 0);
        string result = WppFormatter.ItemToString(param, value);

        Assert.That(result[1], Is.EqualTo('K'));
        Assert.That(result[0], Is.EqualTo('.'));
        Assert.That(result[2], Is.EqualTo('.'));
        Assert.That(result[3], Is.EqualTo('Z'));
    }

    [Test]
    public void ItemToString_FormatsItemSetLong_AsBitNames()
    {
        var listItems = new ReadOnlyDictionary<int, string>(new Dictionary<int, string>
        {
            [0] = "Read",
            [1] = "Write",
            [2] = "Execute"
        });

        FunctionParameter param = new()
        {
            Expression = "perms",
            Type       = ItemType.ItemSetLong,
            Index      = 1,
            ListItems  = listItems
        };

        // bits 0 and 2 set = Read | Execute
        string result = WppFormatter.ItemToString(param, (uint)0b101);

        Assert.That(result, Is.EqualTo("Read, Execute"));
    }

    [Test]
    public void ItemToString_FormatsItemSetLong_NoBitsSet_ReturnsHex()
    {
        var listItems = new ReadOnlyDictionary<int, string>(new Dictionary<int, string>
        {
            [0] = "Read",
            [1] = "Write"
        });

        FunctionParameter param = new()
        {
            Expression = "perms",
            Type       = ItemType.ItemSetLong,
            Index      = 1,
            ListItems  = listItems
        };

        string result = WppFormatter.ItemToString(param, (uint)0);

        Assert.That(result, Does.StartWith("0x"));
    }

    [Test]
    public void ItemToString_ResolvesListLongToBoolString()
    {
        // Mirrors CUSTOM_TYPE(bool, ItemListLong(false,true)) from defaultwpp.ini
        var listItems = new ReadOnlyDictionary<int, string>(new Dictionary<int, string>
        {
            [0] = "false",
            [1] = "true"
        });

        FunctionParameter param = new()
        {
            Expression = "flag",
            Type       = ItemType.ItemListLong,
            Index      = 1,
            ListItems  = listItems
        };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(WppFormatter.ItemToString(param, (int)0), Is.EqualTo("false"));
            Assert.That(WppFormatter.ItemToString(param, (int)1), Is.EqualTo("true"));
        }
    }

    [Test]
    public void ItemToString_NdisStatus_UsesNtStatusLookup()
    {
        FunctionParameter param = MakeParam(ItemType.ItemNDIS_STATUS, index: 1);

        string result = WppFormatter.ItemToString(param, (uint)0x00000000); // STATUS_SUCCESS

        Assert.That(result, Does.Contain("STATUS_SUCCESS"));
    }

    [Test]
    public void ItemToString_NdisOid_FormatsAsHex()
    {
        FunctionParameter param = MakeParam(ItemType.ItemNDIS_OID, index: 1);

        string result = WppFormatter.ItemToString(param, (uint)0x00010101);

        Assert.That(result, Is.EqualTo("0x00010101"));
    }
}
