using System.Text.Json;

using Nefarius.Utilities.ETW.Deserializer.WPP;

namespace Nefarius.Utilities.ETW.Tests;

/// <summary>
///     Tests for argument validation in <see cref="EtwUtil" />.
///     These tests exercise null-/empty-input guards and verify that a bad file path
///     correctly invokes <see cref="EtwJsonConverterOptions.ReportError" /> /
///     <see cref="EtwMetadataScanOptions.ReportError" /> rather than throwing.
/// </summary>
[Category("Unit")]
public class EtwUtilArgumentValidationTests
{
    // -----------------------------------------------------------------------
    // EnumeratePdbReferences – parameter guards
    // -----------------------------------------------------------------------

    [Test]
    public void EnumeratePdbReferences_Throws_WhenInputIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => EtwUtil.EnumeratePdbReferences(null!));
    }

    [Test]
    public void EnumeratePdbReferences_Throws_WhenListContainsEmptyString()
    {
        Assert.Throws<ArgumentException>(() => EtwUtil.EnumeratePdbReferences([""]));
    }

    [Test]
    public void EnumeratePdbReferences_Throws_WhenListContainsWhitespace()
    {
        Assert.Throws<ArgumentException>(() => EtwUtil.EnumeratePdbReferences(["   "]));
    }

    [Test]
    public void EnumeratePdbReferences_Throws_WhenListContainsNullEntry()
    {
        Assert.Throws<ArgumentException>(() => EtwUtil.EnumeratePdbReferences([null!]));
    }

    // -----------------------------------------------------------------------
    // EnumeratePdbReferences – non-existent file
    // -----------------------------------------------------------------------

    [Test]
    [Category("EndToEnd")]
    public void EnumeratePdbReferences_ReportsError_AndReturnsEmpty_WhenFileDoesNotExist()
    {
        List<string> errors = [];

        IReadOnlyCollection<PdbMetaData> result = EtwUtil.EnumeratePdbReferences(
            [@".\traces\__does_not_exist__.etl"],
            opts => opts.ReportError = errors.Add);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(errors,  Is.Not.Empty, "ReportError must have been called for a missing file.");
            Assert.That(result,  Is.Empty,     "No PDB references can be discovered from a missing file.");
        }
    }

    // -----------------------------------------------------------------------
    // ConvertToJson – non-existent file
    // -----------------------------------------------------------------------

    [Test]
    [Category("EndToEnd")]
    public void ConvertToJson_ReturnsFalse_AndReportsError_WhenFileDoesNotExist()
    {
        List<string> errors = [];

        using MemoryStream ms = new();
        using Utf8JsonWriter writer = new(ms);

        bool result = EtwUtil.ConvertToJson(
            writer,
            [@".\traces\__does_not_exist__.etl"],
            opts => opts.ReportError = errors.Add);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.False,      "ConvertToJson must return false for a missing file.");
            Assert.That(errors, Is.Not.Empty,  "ReportError must have been called for a missing file.");
        }
    }
}
