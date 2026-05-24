using Nefarius.Utilities.ETW.Deserializer.WPP;
using Nefarius.Utilities.ETW.Deserializer.WPP.TMF;

namespace Nefarius.Utilities.ETW.Tests;

/// <summary>
///     Tests for <see cref="DecodingContext" />, <see cref="PdbFileDecodingContextType" />, and
///     <see cref="TmfFilesDirectoryDecodingContextType" />.
/// </summary>
public class DecodingContextTests
{
    // The known sample that exists in both BthPS3.pdb and the TMF directory.
    private static readonly Guid KnownGuid = Guid.Parse("e4b27b5e-24d0-369f-a4b5-23228e160bd2");
    private const int KnownId = 12;

    // -----------------------------------------------------------------------
    // DecodingContext constructor guards
    // -----------------------------------------------------------------------

    [Test]
    [Category("Unit")]
    public void DecodingContext_Throws_WhenDecodingTypesIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => _ = new DecodingContext(null!));
    }

    // -----------------------------------------------------------------------
    // DecodingContext.GetTraceMessageFormatFor  (internal, accessible via InternalsVisibleTo)
    // -----------------------------------------------------------------------

    [Test]
    [Category("Parse")]
    public void GetTraceMessageFormatFor_ReturnsNull_ForUnknownGuid()
    {
        DecodingContext context = new(PdbFileDecodingContextType.CreateFrom(@".\symbols\BthPS3.pdb"));

        TraceMessageFormat? result = context.GetTraceMessageFormatFor(Guid.NewGuid(), 0);

        Assert.That(result, Is.Null);
    }

    [Test]
    [Category("Parse")]
    public void GetTraceMessageFormatFor_ReturnsNull_WhenGuidIsNull()
    {
        DecodingContext context = new(PdbFileDecodingContextType.CreateFrom(@".\symbols\BthPS3.pdb"));

        TraceMessageFormat? result = context.GetTraceMessageFormatFor(null, 0);

        Assert.That(result, Is.Null);
    }

    [Test]
    [Category("Parse")]
    public void GetTraceMessageFormatFor_ReturnsKnownFormat_WhenGuidAndIdMatch()
    {
        DecodingContext context = new(PdbFileDecodingContextType.CreateFrom(@".\symbols\BthPS3.pdb"));

        TraceMessageFormat? result = context.GetTraceMessageFormatFor(KnownGuid, KnownId);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Opcode, Is.EqualTo("Bluetooth_Context_c149"));
    }

    // -----------------------------------------------------------------------
    // DecodingContext.ExtendWith
    // -----------------------------------------------------------------------

    [Test]
    [Category("Parse")]
    public void ExtendWith_MergesFormatsFromBothContexts()
    {
        // BthPS3PSM.pdb contains formats that BthPS3.pdb does not — loading only one
        // at a time lets us confirm ExtendWith truly unions both sets.
        IList<DecodingContextType> bthPs3Types    = PdbFileDecodingContextType.CreateFrom(@".\symbols\BthPS3.pdb");
        IList<DecodingContextType> bthPs3PsmTypes = PdbFileDecodingContextType.CreateFrom(@".\symbols\BthPS3PSM.pdb");

        DecodingContext bthPs3Context = new(bthPs3Types);
        DecodingContext extended      = bthPs3Context.ExtendWith(bthPs3PsmTypes);

        int originalCount = bthPs3Types.SelectMany(t => t.TraceMessageFormats).Distinct().Count();
        int psmCount      = bthPs3PsmTypes.SelectMany(t => t.TraceMessageFormats).Distinct().Count();

        // The extended context must have at least as many formats as each source individually.
        int extendedCount = bthPs3PsmTypes
            .Concat(bthPs3Types)
            .SelectMany(t => t.TraceMessageFormats)
            .Distinct()
            .Count();

        // The known BthPS3.pdb format must still be resolvable.
        TraceMessageFormat? fromOriginal = extended.GetTraceMessageFormatFor(KnownGuid, KnownId);
        Assert.That(fromOriginal, Is.Not.Null,
            "The format from the base context must still be resolvable in the extended context.");
    }

    // -----------------------------------------------------------------------
    // PdbFileDecodingContextType – stream vs path parity
    // -----------------------------------------------------------------------

    [Test]
    [Category("Parse")]
    public void PdbFileDecodingContextType_StreamCtor_ProducesSameFormatsAsPathCtor()
    {
        // Path-based
        PdbFileDecodingContextType fromPath = new(@".\symbols\BthPS3.pdb");

        // Stream-based — open the same file as a stream
        using FileStream fs = File.OpenRead(@".\symbols\BthPS3.pdb");
        PdbFileDecodingContextType fromStream = new(fs);

        IEnumerable<TraceMessageFormat> pathFormats   = fromPath.TraceMessageFormats.Distinct();
        IEnumerable<TraceMessageFormat> streamFormats = fromStream.TraceMessageFormats.Distinct();

        Assert.That(streamFormats, Is.EquivalentTo(pathFormats),
            "The stream-based constructor must produce identical TraceMessageFormats to the path-based one.");
    }

    // -----------------------------------------------------------------------
    // TmfFilesDirectoryDecodingContextType
    // -----------------------------------------------------------------------

    [Test]
    [Category("Parse")]
    public void TmfFilesDirectoryDecodingContextType_LooksUpKnownFormat()
    {
        IList<DecodingContextType> tmfTypes = TmfFilesDirectoryDecodingContextType.CreateFrom(@".\symbols");
        DecodingContext context = new(tmfTypes);

        TraceMessageFormat? result = context.GetTraceMessageFormatFor(KnownGuid, KnownId);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Opcode, Is.EqualTo("Bluetooth_Context_c149"));
    }
}
