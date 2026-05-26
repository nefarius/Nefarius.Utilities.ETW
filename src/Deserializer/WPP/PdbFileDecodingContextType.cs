using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

using Kaitai;

using Nefarius.Shared.PdbUtils.Extensions;
using Nefarius.Utilities.ETW.Deserializer.WPP.TMF;

namespace Nefarius.Utilities.ETW.Deserializer.WPP;

/// <summary>
///     A <see cref="DecodingContextType" /> parsing one or more given <c>.pdb</c> files.
/// </summary>
[SuppressMessage("ReSharper", "UnusedMember.Global")]
[SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
public sealed class PdbFileDecodingContextType()
    : DecodingContextType
{
    /// <summary>
    ///     Gets decoding info from one or multiple <c>.pdb</c> files by file path.
    /// </summary>
    /// <param name="path">Relative or absolute path to a <c>.pdb</c> file.</param>
    public PdbFileDecodingContextType(string path) : this()
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        using FileStream fileStream = File.OpenRead(path);
        using KaitaiStream kaitaiStream = new(fileStream);
        InitializeFromStream(kaitaiStream);
    }

    /// <summary>
    ///     Gets decoding info from a stream containing PDB file data.
    /// </summary>
    /// <param name="stream">A stream containing a <c>.pdb</c> file.</param>
    public PdbFileDecodingContextType(Stream stream) : this()
    {
        ArgumentNullException.ThrowIfNull(stream);

        using KaitaiStream kaitaiStream = new(stream);
        InitializeFromStream(kaitaiStream);
    }

    /// <summary>
    ///     The WPP trace control blocks discovered in this PDB — one entry per
    ///     <c>WPP_DEFINE_CONTROL_GUID</c> declaration found in the symbol stream.
    ///     Each entry contains the provider GUID, its friendly name, and the declared
    ///     <c>WPP_DEFINE_BIT</c> flag names.
    /// </summary>
    public IReadOnlyCollection<WppTraceControl> WppTraceControls { get; private set; } =
        new ReadOnlyCollection<WppTraceControl>([]);

    /// <inheritdoc />
    /// <remarks>
    ///     For PDB sources the provider GUIDs are the WPP trace control GUIDs extracted from
    ///     <c>TMC:</c> <c>S_ANNOTATION</c> records in the symbol stream — one per
    ///     <c>WPP_DEFINE_CONTROL_GUID</c> declaration.
    /// </remarks>
    public override IEnumerable<Guid> ProviderGuids =>
        WppTraceControls.Select(c => c.ControlGuid);

    /// <summary>
    ///     Initializes the decoding context from a KaitaiStream.
    /// </summary>
    private void InitializeFromStream(KaitaiStream kaitaiStream)
    {
        MsPdb pdb = new(kaitaiStream);
        string? originalName = pdb.GetOriginalPdbName();

        // Materialise the full flat symbol list once so we can pass it to both extractors
        // without iterating the module list twice.
        List<MsPdb.DbiSymbol> allSymbols = pdb.DbiStream.ModulesList.Items
            .SelectMany(m => m.ModuleData.SymbolsList.Items)
            .ToList();

        TraceMessageFormats = TmfParser
            .ExtractTraceMessageFormats(allSymbols.ExtractTmfAnnotations(), originalName)
            .Distinct();

        WppTraceControls = allSymbols
            .ExtractTraceControls()
            .Select(c => c with { OriginalSymbolFileName = originalName })
            .DistinctBy(c => c.ControlGuid)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    ///     Converts a list of paths to <c>*.pdb</c> files into their corresponding
    ///     <see cref="PdbFileDecodingContextType" /> objects.
    /// </summary>
    /// <param name="pathList">One or more paths to <c>.pdb</c> files.</param>
    /// <returns>One or more <see cref="PdbFileDecodingContextType" />s.</returns>
    public static IList<DecodingContextType> CreateFrom(params IList<string> pathList)
    {
        return pathList.Select(DecodingContextType (path) => new PdbFileDecodingContextType(path)).ToList();
    }

    /// <summary>
    ///     Convenience helper that parses a single <c>.pdb</c> file and returns the distinct set of
    ///     WPP trace control GUIDs (= ETW provider GUIDs) embedded in it.
    /// </summary>
    /// <param name="path">Relative or absolute path to a <c>.pdb</c> file.</param>
    /// <returns>
    ///     A distinct, read-only collection of <see cref="Guid" />s — one per
    ///     <c>WPP_DEFINE_CONTROL_GUID</c> block found in the PDB. The collection is empty if the
    ///     PDB contains no WPP <c>TMC:</c> annotations.
    /// </returns>
    public static IReadOnlyCollection<Guid> EnumerateProviderGuids(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        PdbFileDecodingContextType ctx = new(path);
        return ctx.WppTraceControls.Select(c => c.ControlGuid).ToArray();
    }

    /// <summary>
    ///     Convenience helper that parses a single <c>.pdb</c> file and returns the full
    ///     <see cref="WppTraceControl" /> records found in it — including the provider GUID,
    ///     friendly name, and <c>WPP_DEFINE_BIT</c> flag names.
    /// </summary>
    /// <param name="path">Relative or absolute path to a <c>.pdb</c> file.</param>
    /// <returns>
    ///     A read-only collection of <see cref="WppTraceControl" />s, deduplicated by
    ///     <see cref="WppTraceControl.ControlGuid" />. Empty if the PDB has no WPP annotations.
    /// </returns>
    public static IReadOnlyCollection<WppTraceControl> EnumerateTraceControls(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        PdbFileDecodingContextType ctx = new(path);
        return ctx.WppTraceControls;
    }
}
