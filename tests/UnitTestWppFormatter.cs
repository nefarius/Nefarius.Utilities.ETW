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
        // Passing a string where an integer is expected will throw inside string.Format.
        FunctionParameter param = MakeParam(ItemType.ItemLong, index: 1);
        TraceMessageFormat format = MakeFormat("%1!d!", param);

        string result = WppFormatter.Substitute(
            format,
            Params((1, param, "not_an_int")));

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
}
