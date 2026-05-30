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

    // WPP control GUIDs from the TMC: S_ANNOTATION records in the test PDBs.
    // Verified from tests/symbols/BthPS3.pdb-cvdump.txt and BthPS3PSM.pdb-cvdump.txt.
    private static readonly Guid BthPS3ControlGuid = Guid.Parse("37dcd579-e844-4c80-9c8b-a10850b6fac6");
    private static readonly Guid BthPS3PsmControlGuid = Guid.Parse("586aa8b1-53a6-404f-9b3e-14483e514a2c");

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

    // -----------------------------------------------------------------------
    // TmfFileDecodingContextType
    // -----------------------------------------------------------------------

    [Test]
    [Category("Unit")]
    public void TmfFileDecodingContextType_Throws_OnEmptyPath()
    {
        Assert.Throws<ArgumentException>(() => _ = new TmfFileDecodingContextType(string.Empty));
    }

    [Test]
    [Category("Parse")]
    public void TmfFileDecodingContextType_LooksUpKnownFormat()
    {
        DecodingContext context = new(new TmfFileDecodingContextType(
            @".\symbols\e4b27b5e-24d0-369f-a4b5-23228e160bd2.tmf"));

        TraceMessageFormat? result = context.GetTraceMessageFormatFor(KnownGuid, KnownId);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Opcode, Is.EqualTo("Bluetooth_Context_c149"));
    }

    // -----------------------------------------------------------------------
    // WppTraceControl / TMC: extraction — PdbFileDecodingContextType
    // -----------------------------------------------------------------------

    [Test]
    [Category("Parse")]
    public void PdbFileDecodingContextType_WppTraceControls_ContainsBthPS3ControlGuid()
    {
        PdbFileDecodingContextType ctx = new(@".\symbols\BthPS3.pdb");

        Assert.That(ctx.WppTraceControls.Select(c => c.ControlGuid),
            Does.Contain(BthPS3ControlGuid));
    }

    [Test]
    [Category("Parse")]
    public void PdbFileDecodingContextType_WppTraceControls_HasCorrectName()
    {
        PdbFileDecodingContextType ctx = new(@".\symbols\BthPS3.pdb");

        WppTraceControl? ctrl = ctx.WppTraceControls
            .FirstOrDefault(c => c.ControlGuid == BthPS3ControlGuid);

        Assert.That(ctrl, Is.Not.Null);
        Assert.That(ctrl!.Name, Is.EqualTo("BthPS3TraceGuid"));
    }

    [Test]
    [Category("Parse")]
    public void PdbFileDecodingContextType_WppTraceControls_HasExpectedBitFlags()
    {
        PdbFileDecodingContextType ctx = new(@".\symbols\BthPS3.pdb");

        WppTraceControl? ctrl = ctx.WppTraceControls
            .FirstOrDefault(c => c.ControlGuid == BthPS3ControlGuid);

        Assert.That(ctrl, Is.Not.Null);
        // The first four flag names are documented in the cvdump.
        Assert.That(ctrl!.BitFlags, Does.Contain("MYDRIVER_ALL_INFO"));
        Assert.That(ctrl.BitFlags, Does.Contain("TRACE_DRIVER"));
        Assert.That(ctrl.BitFlags, Does.Contain("TRACE_DEVICE"));
        Assert.That(ctrl.BitFlags, Does.Contain("TRACE_QUEUE"));
    }

    [Test]
    [Category("Parse")]
    public void PdbFileDecodingContextType_ProviderGuids_ContainsBthPS3ControlGuid()
    {
        PdbFileDecodingContextType ctx = new(@".\symbols\BthPS3.pdb");

        Assert.That(ctx.ProviderGuids, Does.Contain(BthPS3ControlGuid));
    }

    // -----------------------------------------------------------------------
    // EnumerateProviderGuids static helper
    // -----------------------------------------------------------------------

    [Test]
    [Category("Parse")]
    public void EnumerateProviderGuids_ReturnsBthPS3ControlGuid()
    {
        IReadOnlyCollection<Guid> guids =
            PdbFileDecodingContextType.EnumerateProviderGuids(@".\symbols\BthPS3.pdb");

        Assert.That(guids, Does.Contain(BthPS3ControlGuid));
    }

    [Test]
    [Category("Parse")]
    public void EnumerateProviderGuids_ReturnsBthPS3PsmControlGuid()
    {
        IReadOnlyCollection<Guid> guids =
            PdbFileDecodingContextType.EnumerateProviderGuids(@".\symbols\BthPS3PSM.pdb");

        Assert.That(guids, Does.Contain(BthPS3PsmControlGuid));
    }

    [Test]
    [Category("Parse")]
    public void EnumerateProviderGuids_ReturnsDistinctGuids()
    {
        IReadOnlyCollection<Guid> guids =
            PdbFileDecodingContextType.EnumerateProviderGuids(@".\symbols\BthPS3.pdb");

        Assert.That(guids.Count, Is.EqualTo(guids.Distinct().Count()));
    }

    [Test]
    [Category("Unit")]
    public void EnumerateProviderGuids_Throws_OnEmptyPath()
    {
        Assert.Throws<ArgumentException>(() =>
            PdbFileDecodingContextType.EnumerateProviderGuids(string.Empty));
    }

    // -----------------------------------------------------------------------
    // EnumerateTraceControls static helper
    // -----------------------------------------------------------------------

    [Test]
    [Category("Parse")]
    public void EnumerateTraceControls_ReturnsSingleEntryForBthPS3()
    {
        IReadOnlyCollection<WppTraceControl> controls =
            PdbFileDecodingContextType.EnumerateTraceControls(@".\symbols\BthPS3.pdb");

        Assert.That(controls, Has.Count.EqualTo(1));
        Assert.That(controls.Single().ControlGuid, Is.EqualTo(BthPS3ControlGuid));
        Assert.That(controls.Single().Name, Is.EqualTo("BthPS3TraceGuid"));
    }

    [Test]
    [Category("Unit")]
    public void EnumerateTraceControls_Throws_OnEmptyPath()
    {
        Assert.Throws<ArgumentException>(() =>
            PdbFileDecodingContextType.EnumerateTraceControls(string.Empty));
    }

    // -----------------------------------------------------------------------
    // DecodingContext.ProviderGuids — correct TMC-based values
    // -----------------------------------------------------------------------

    [Test]
    [Category("Parse")]
    public void DecodingContext_ProviderGuids_ContainsBothControlGuids()
    {
        IList<DecodingContextType> types = PdbFileDecodingContextType
            .CreateFrom(@".\symbols\BthPS3.pdb", @".\symbols\BthPS3PSM.pdb");
        DecodingContext context = new(types);

        Assert.That(context.ProviderGuids, Does.Contain(BthPS3ControlGuid));
        Assert.That(context.ProviderGuids, Does.Contain(BthPS3PsmControlGuid));
    }

    [Test]
    [Category("Parse")]
    public void DecodingContext_ProviderGuids_IsDistinct()
    {
        IList<DecodingContextType> types = PdbFileDecodingContextType
            .CreateFrom(@".\symbols\BthPS3.pdb", @".\symbols\BthPS3PSM.pdb");
        DecodingContext context = new(types);

        Assert.That(context.ProviderGuids.Count,
            Is.EqualTo(context.ProviderGuids.Distinct().Count()));
    }

    [Test]
    [Category("Parse")]
    public void DecodingContext_ProviderGuids_IsEmpty_ForTmfOnlyContext()
    {
        // TMF files don't contain TMC: records — provider GUIDs cannot be derived from them.
        IList<DecodingContextType> tmfTypes =
            TmfFilesDirectoryDecodingContextType.CreateFrom(@".\symbols");
        DecodingContext context = new(tmfTypes);

        Assert.That(context.ProviderGuids, Is.Empty);
    }

    [Test]
    [Category("Parse")]
    public void DecodingContext_ProviderGuids_DoesNotContainTmfMessageHashGuid()
    {
        // KnownGuid (e4b27b5e-...) is a per-source-file TMF message hash, not a control GUID.
        DecodingContext context =
            new(PdbFileDecodingContextType.CreateFrom(@".\symbols\BthPS3.pdb"));

        Assert.That(context.ProviderGuids, Does.Not.Contain(KnownGuid));
    }

    // -----------------------------------------------------------------------
    // DecodingContext.GetWppProviderNameOverride  (internal, via InternalsVisibleTo)
    // -----------------------------------------------------------------------

    [Test]
    [Category("Parse")]
    public void GetWppProviderNameOverride_ReturnsBthPS3TraceGuid_ForKnownBthPS3Format()
    {
        // BthPS3.pdb declares exactly one control (BthPS3TraceGuid), so every format
        // derived from that PDB should resolve to the friendly control name.
        DecodingContext context =
            new(PdbFileDecodingContextType.CreateFrom(@".\symbols\BthPS3.pdb"));

        TraceMessageFormat? format = context.GetTraceMessageFormatFor(KnownGuid, KnownId);

        Assert.That(format, Is.Not.Null, "Precondition: known format must resolve.");
        string? name = context.GetWppProviderNameOverride(format!);
        Assert.That(name, Is.EqualTo("BthPS3TraceGuid"));
    }

    [Test]
    [Category("Parse")]
    public void GetWppProviderNameOverride_ReturnsBothControlNames_ForCombinedContext()
    {
        // BthPS3PSM.pdb: MessageGuid ca66b9c0-..., Id 13 (Device_c104) — verified from cvdump.
        Guid psmMessageGuid = Guid.Parse("ca66b9c0-97d7-3776-3daf-3296492866aa");
        const int psmId = 13;

        // Build a combined context — BthPS3.pdb and BthPS3PSM.pdb each declare exactly
        // one control, so the override map must contain entries for both independently.
        DecodingContext context = new(PdbFileDecodingContextType.CreateFrom(
            @".\symbols\BthPS3.pdb",
            @".\symbols\BthPS3PSM.pdb"
        ));

        // KnownGuid/KnownId belongs to BthPS3.pdb.
        TraceMessageFormat? bthPs3Format = context.GetTraceMessageFormatFor(KnownGuid, KnownId);
        Assert.That(bthPs3Format, Is.Not.Null, "Precondition: BthPS3 format must resolve.");
        Assert.That(context.GetWppProviderNameOverride(bthPs3Format!), Is.EqualTo("BthPS3TraceGuid"));

        // psmMessageGuid/psmId belongs to BthPS3PSM.pdb.
        TraceMessageFormat? psmFormat = context.GetTraceMessageFormatFor(psmMessageGuid, psmId);
        Assert.That(psmFormat, Is.Not.Null, "Precondition: BthPS3PSM format must resolve.");
        Assert.That(context.GetWppProviderNameOverride(psmFormat!), Is.EqualTo("BthPS3PSMTraceGuid"));
    }

    [Test]
    [Category("Parse")]
    public void GetWppProviderNameOverride_ReturnsNull_ForTmfOnlyContext()
    {
        // TMF files carry no TMC: annotations, so no override entries can be built.
        IList<DecodingContextType> tmfTypes =
            TmfFilesDirectoryDecodingContextType.CreateFrom(@".\symbols");
        DecodingContext context = new(tmfTypes);

        TraceMessageFormat? format = context.GetTraceMessageFormatFor(KnownGuid, KnownId);
        Assert.That(format, Is.Not.Null, "Precondition: TMF format must resolve.");
        Assert.That(context.GetWppProviderNameOverride(format!), Is.Null,
            "TMF-only context must produce no override (graceful fallback).");
    }

    [Test]
    [Category("Unit")]
    public void GetWppProviderNameOverride_ReturnsNull_ForUnknownFormat()
    {
        DecodingContext context =
            new(PdbFileDecodingContextType.CreateFrom(@".\symbols\BthPS3.pdb"));

        // Fabricate a format that does not exist in BthPS3.pdb.
        TraceMessageFormat unknown = new()
        {
            MessageGuid = Guid.NewGuid(),
            Id = 999,
            Provider = "phantom",
            FileName = "phantom.c",
            Opcode = "op",
            MessageFormat = "%0",
            Level = "TRACE_LEVEL_VERBOSE",
            Flags = "TRACE_DRIVER",
            Function = "PhantomFn"
        };

        Assert.That(context.GetWppProviderNameOverride(unknown), Is.Null);
    }
}
