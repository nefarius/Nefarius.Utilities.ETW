using Nefarius.Utilities.ETW.Deserializer.WPP;
using Nefarius.Utilities.ETW.Events;

namespace Nefarius.Utilities.ETW.Tests;

/// <summary>
///     Verifies that <see cref="EtwMetadataScanOptions" /> callbacks fire with sensible payloads
///     when scanning a real trace file, and that no errors are reported on the happy path.
///
///     <c>BthPS3_0.etl</c> embeds PDB references via the legacy
///     <c>MSNT_SystemTrace/EventTrace/DbgIdRSDS</c> event (the <see cref="EtwMetadataScanOptions.OnKernelDbgIdRsds" />
///     path), not the newer <c>KernelTraceControl/ImageID/DbgID_RSDS</c> event
///     (<see cref="EtwMetadataScanOptions.OnDbgIdRsds" />).  Tests are written accordingly.
/// </summary>
[Category("EndToEnd")]
public class ScanCallbackTests
{
    private const string EtwFilePath = @".\traces\BthPS3_0.etl";

    // -----------------------------------------------------------------------
    // OnKernelDbgIdRsds  (the path BthPS3_0.etl actually uses)
    // -----------------------------------------------------------------------

    [Test]
    public void OnKernelDbgIdRsds_IsFiredAtLeastOnce_WithValidPayload()
    {
        List<KernelDbgIdRsdsEventInfo> captured = [];
        List<string> errors = [];

        IReadOnlyCollection<PdbMetaData> result = EtwUtil.EnumeratePdbReferences(
            [EtwFilePath],
            opts =>
            {
                opts.OnKernelDbgIdRsds = info => captured.Add(info);
                opts.ReportError = errors.Add;
            });

        Assert.That(errors,   Is.Empty,     "ReportError must not fire on a valid trace file.");
        Assert.That(captured, Is.Not.Empty, "OnKernelDbgIdRsds must fire at least once for BthPS3_0.etl.");

        KernelDbgIdRsdsEventInfo sample = captured[0];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(sample.Timestamp, Is.GreaterThan(0),              "Timestamp must be non-zero.");
            Assert.That(sample.PdbName,   Is.Not.Null.And.Not.Empty,      "PdbName must be present.");
            Assert.That(sample.Age,       Is.GreaterThan(0U),             "Age must be a positive value.");
            Assert.That(sample.Guid,      Is.Not.EqualTo(Guid.Empty),     "Guid must not be empty.");
        }
    }

    [Test]
    public void OnKernelDbgIdRsds_PayloadMatchesReturnedPdbMetaData()
    {
        List<KernelDbgIdRsdsEventInfo> captured = [];

        IReadOnlyCollection<PdbMetaData> result = EtwUtil.EnumeratePdbReferences(
            [EtwFilePath],
            opts => opts.OnKernelDbgIdRsds = info => captured.Add(info));

        Assert.That(captured, Is.Not.Empty, "OnKernelDbgIdRsds must fire at least once.");

        foreach (KernelDbgIdRsdsEventInfo info in captured)
        {
            if (string.IsNullOrEmpty(info.PdbName))
            {
                continue;
            }

            PdbMetaData projected = info.ToPdbMetaData();
            Assert.That(result, Does.Contain(projected),
                $"Projected PdbMetaData for '{info.PdbName}' must appear in the returned collection.");
        }
    }

    // -----------------------------------------------------------------------
    // OnDbgIdRsds / OnImageId — not present in BthPS3_0.etl (uses kernel path)
    // -----------------------------------------------------------------------

    [Test]
    public void OnDbgIdRsds_DoesNotFireForThisTrace_BecauseItUsesKernelPath()
    {
        // BthPS3_0.etl embeds PDB info via the legacy MSNT_SystemTrace provider,
        // not via KernelTraceControl/ImageID.  OnDbgIdRsds must remain empty while
        // the references are still discovered (via OnKernelDbgIdRsds).
        List<DbgIdRsdsEventInfo> captured = [];

        IReadOnlyCollection<PdbMetaData> result = EtwUtil.EnumeratePdbReferences(
            [EtwFilePath],
            opts => opts.OnDbgIdRsds = info => captured.Add(info));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(captured, Is.Empty,     "OnDbgIdRsds must not fire for this trace.");
            Assert.That(result,   Is.Not.Empty, "PDB references must still be discovered via the kernel path.");
        }
    }

    // -----------------------------------------------------------------------
    // Shared happy-path checks
    // -----------------------------------------------------------------------

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
            Assert.That(refs,     Is.Not.Empty,                  "Must discover at least one PDB reference.");
            Assert.That(pdbNames, Does.Contain("bthps3.pdb"),    "BthPS3.pdb must be referenced in the trace.");
            Assert.That(pdbNames, Does.Contain("bthps3psm.pdb"), "BthPS3PSM.pdb must be referenced in the trace.");
        }
    }
}
