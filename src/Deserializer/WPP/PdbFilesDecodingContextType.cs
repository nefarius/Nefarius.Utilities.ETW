using System.Text;

using Kaitai;

using Nefarius.Utilities.ETW.Deserializer.WPP.TMF;

namespace Nefarius.Utilities.ETW.Deserializer.WPP;

/// <summary>
///     A <see cref="TDH_CONTEXT_TYPE.TDH_CONTEXT_PDB_PATH" /> wrapper for use with <see cref="DecodingContext" />.
/// </summary>
public sealed class PdbFilesDecodingContextType()
    : DecodingContextType(TDH_CONTEXT_TYPE.TDH_CONTEXT_PDB_PATH)
{
    /// <summary>
    ///     Gets decoding info from one or multiple <c>.PDB</c> files.
    /// </summary>
    /// <param name="path">
    ///     Null-terminated Unicode string that contains the name of the .pdb file for the binary that
    ///     contains WPP messages. This parameter can be used as an alternative to TDH_CONTEXT_WPP_TMFFILE or
    ///     TDH_CONTEXT_WPP_TMFSEARCHPATH.
    /// </param>
    /// <remarks>To specify multiple files, use <see cref="CreateFrom" />.</remarks>
    public PdbFilesDecodingContextType(string path) : this()
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        Buffer = new ReadOnlyMemory<byte>(Encoding.Unicode.GetBytes(Path.GetFullPath(path))
            .Concat("\0\0"u8.ToArray())
            .ToArray()
        );

        TmfParser parser = new();

        using KaitaiStream stream = new(File.OpenRead(path));
        MsPdb pdb = new(stream);

        IEnumerable<SymProc32AnnotationPair> annotations = pdb
            .DbiStream.ModulesList.Items
            .SelectMany(m => m.ModuleData.SymbolsList.Items)
            .ToList()
            .ExtractTmfAnnotations();

        List<TraceMessageFormat> result = parser
            .ExtractTraceMessageFormats(annotations)
            .ToList();

        TraceMessageFormats = result.AsReadOnly();
    }

    /// <summary>
    ///     Converts a list of paths to <c>*.pdb</c> files into their corresponding <see cref="PdbFilesDecodingContextType" />
    ///     objects.
    /// </summary>
    /// <param name="pathList">One or more paths to <c>*.pdb</c> files.</param>
    /// <returns>One or more <see cref="PdbFilesDecodingContextType" />s.</returns>
    public static IList<DecodingContextType> CreateFrom(params IList<string> pathList)
    {
        return pathList.Select(DecodingContextType (path) => new PdbFilesDecodingContextType(path)).ToList();
    }
}