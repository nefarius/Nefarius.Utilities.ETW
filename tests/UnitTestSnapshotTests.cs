using VerifyNUnit;

namespace Nefarius.Utilities.ETW.Tests;

/// <summary>
///     Snapshot / golden-master tests for the decoded JSON output.
///
///     On first run (or after a deliberate change) the test will write a <c>.received.txt</c> file
///     next to its <c>.verified.txt</c> counterpart.  Review and approve the received output by
///     renaming/copying it to the corresponding <c>.verified.txt</c> file (or use the Verify
///     diffing tool / <c>dotnet verify accept</c>).
///
///     Baseline files live under <c>tests/Snapshots/</c> and must be committed to the repository
///     so that CI can detect regressions in the serialized output.
/// </summary>
[Category("EndToEnd")]
public class SnapshotTests
{
    [Test]
    public Task BthPs3EtlTrace_SnapshotMatches()
    {
        string json = Shared.BthPs3EtlTraceDecodeToString();
        return Verifier.Verify(json)
            .UseDirectory("Snapshots")
            .UseFileName("BthPS3_0");
    }

    [Test]
    public Task DsHidMiniEtlTrace_SnapshotMatches()
    {
        string json = Shared.DsHidMiniEtlTraceDecodeToString();
        return Verifier.Verify(json)
            .UseDirectory("Snapshots")
            .UseFileName("DsHidMini");
    }
}
