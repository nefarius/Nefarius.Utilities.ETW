using Kaitai;

using Nefarius.Shared.PdbUtils.Extensions;
using Nefarius.Utilities.ETW.Deserializer.WPP.TMF;

namespace Nefarius.Utilities.ETW.Deserializer.WPP;

/// <summary>
///     A <see cref="DecodingContextType" /> parsing one or more given <c>.pdb</c> files.
/// </summary>
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
    ///     Initializes the decoding context from a KaitaiStream.
    /// </summary>
    /// <param name="kaitaiStream">Kaitai stream containing PDB data.</param>
    private void InitializeFromStream(KaitaiStream kaitaiStream)
    {
        MsPdb pdb = new(kaitaiStream);
        string? originalName = pdb.GetOriginalPdbName();

        IEnumerable<SymProc32AnnotationPair> annotations = pdb
            .DbiStream.ModulesList.Items
            .SelectMany(m => m.ModuleData.SymbolsList.Items)
            .ToList()
            .ExtractTmfAnnotations();

        TraceMessageFormats = TmfParser
            .ExtractTraceMessageFormats(annotations, originalName)
            .Distinct();
    }

    /// <summary>
    ///     Converts a list of paths to <c>*.pdb</c> files into their corresponding <see cref="PdbFileDecodingContextType" />
    ///     objects.
    /// </summary>
    /// <param name="pathList">One or more paths to <c>.pdb</c> files.</param>
    /// <returns>One or more <see cref="PdbFileDecodingContextType" />s.</returns>
    public static IList<DecodingContextType> CreateFrom(params IList<string> pathList)
    {
        return pathList.Select(DecodingContextType (path) => new PdbFileDecodingContextType(path)).ToList();
    }
}