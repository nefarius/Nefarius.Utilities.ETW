using Nefarius.Utilities.ETW.Deserializer.WPP;
using Nefarius.Utilities.ETW.Events;

namespace Nefarius.Utilities.ETW.Tests;

/// <summary>
///     Verifies that every <see cref="EtwMetadataScanOptions" /> callback fires with sensible
///     payloads when scanning a real trace file, and that no errors are reported on the happy path.
/// </summary>
[Category("EndToEnd")]
public class ScanCallbackTests
{
    private const string EtwFilePath = @".\traces\BthPS3_0.etl";

    [Test]
    public void OnDbgIdRsds_IsFiredAtLeastOnce_WithValidPayload()
    {
        List<DbgIdRsdsEventInfo> captured = [];
        List<string> errors = [];

        IReadOnlyCollection<PdbMetaData> result = EtwUtil.EnumeratePdbReferences(
            [EtwFilePath],
            opts =>
            {
                opts.OnDbgIdRsds = info => captured.Add(info);
                opts.ReportError = errors.Add;
            });

        Assert.That(errors,   Is.Empty,     "ReportError must not fire on a valid trace file.");
        Assert.That(captured, Is.Not.Empty, "OnDbgIdRsds must fire at least once for BthPS3_0.etl.");

        DbgIdRsdsEventInfo sample = captured[0];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(sample.Timestamp,   Is.GreaterThan(0),   "Timestamp must be non-zero.");
            Assert.That(sample.ImageBase,   Is.GreaterThan(0UL), "ImageBase must be non-zero.");
            Assert.That(sample.PdbFileName, Is.Not.Null.And.Not.Empty, "PdbFileName must be present.");
            Assert.That(sample.Age,         Is.GreaterThan(0U),  "Age must be a positive value.");
        }
    }

    [Test]
    public void OnDbgIdRsds_PayloadMatchesReturnedPdbMetaData()
    {
        List<DbgIdRsdsEventInfo> captured = [];

        IReadOnlyCollection<PdbMetaData> result = EtwUtil.EnumeratePdbReferences(
            [EtwFilePath],
            opts => opts.OnDbgIdRsds = info => captured.Add(info));

        Assert.That(captured, Is.Not.Empty, "OnDbgIdRsds must fire at least once.");

        foreach (DbgIdRsdsEventInfo info in captured)
        {
            if (string.IsNullOrEmpty(info.PdbFileName))
            {
                continue;
            }

            PdbMetaData projected = info.ToPdbMetaData();
            Assert.That(result, Does.Contain(projected),
                $"Projected PdbMetaData for '{info.PdbFileName}' must appear in the returned collection.");
        }
    }

    [Test]
    public void OnImageId_IsFiredAtLeastOnce_WithValidPayload()
    {
        List<ImageIdEventInfo> captured = [];

        EtwUtil.EnumeratePdbReferences(
            [EtwFilePath],
            opts => opts.OnImageId = info => captured.Add(info));

        Assert.That(captured, Is.Not.Empty, "OnImageId must fire at least once for BthPS3_0.etl.");

        ImageIdEventInfo sample = captured[0];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(sample.ImageBase,        Is.GreaterThan(0UL),         "ImageBase must be non-zero.");
            Assert.That(sample.ImageSize,        Is.GreaterThan(0U),          "ImageSize must be non-zero.");
            Assert.That(sample.OriginalFileName, Is.Not.Null.And.Not.Empty,   "OriginalFileName must be present.");
        }
    }

    [Test]
    public void ReportError_IsNeverFired_ForValidTraceFile()
    {
        List<string> errors = [];

        EtwUtil.EnumeratePdbReferences(
            [EtwFilePath],
            opts => opts.ReportError = errors.Add);

        Assert.That(errors, Is.Empty,
            $"No errors expected for a valid trace file, but got: {string.Join("; ", errors)}");
    }

    [Test]
    public void AllDiscoveredPdbNames_ContainExpectedEntries()
    {
        IReadOnlyCollection<PdbMetaData> refs = EtwUtil.EnumeratePdbReferences([EtwFilePath]);

        IList<string> pdbNames = refs
            .Select(r => Path.GetFileName(r.PdbName).ToLowerInvariant())
            .ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(refs,     Is.Not.Empty,                 "Must discover at least one PDB reference.");
            Assert.That(pdbNames, Does.Contain("bthps3.pdb"),   "BthPS3.pdb must be referenced in the trace.");
            Assert.That(pdbNames, Does.Contain("bthps3psm.pdb"),"BthPS3PSM.pdb must be referenced in the trace.");
        }
    }
}
