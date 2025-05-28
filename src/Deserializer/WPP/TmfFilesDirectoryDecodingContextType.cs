using System.Diagnostics.CodeAnalysis;

using Nefarius.Utilities.ETW.Deserializer.WPP.TMF;

namespace Nefarius.Utilities.ETW.Deserializer.WPP;

/// <summary>
///     A <see cref="TDH_CONTEXT_TYPE.TDH_CONTEXT_WPP_TMFFILE" />  wrapper for use with <see cref="DecodingContext" />.
/// </summary>
[SuppressMessage("ReSharper", "UnusedType.Global")]
public sealed class TmfFilesDirectoryDecodingContextType()
    : DecodingContextType
{
    /// <summary>
    ///     Gets decoding info from a directory containing multiple <c>.tmf</c> files.
    /// </summary>
    /// <param name="path">Path to a directory containing multiple <c>.tmf</c> files. </param>
    public TmfFilesDirectoryDecodingContextType(string path) : this()
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        TmfParser tmfParser = new();

        TraceMessageFormats = tmfParser
            .ParseDirectory(path)
            .Distinct();
    }

    /// <summary>
    ///     Converts a list of paths to <c>*.tmf</c> files into their corresponding
    ///     <see cref="TmfFilesDirectoryDecodingContextType" />
    ///     objects.
    /// </summary>
    /// <param name="pathList">One or more paths to <c>*.pdb</c> files.</param>
    /// <returns>One or more <see cref="TmfFilesDirectoryDecodingContextType" />s.</returns>
    public static IList<DecodingContextType> CreateFrom(params IList<string> pathList)
    {
        return pathList.Select(DecodingContextType (path) => new TmfFilesDirectoryDecodingContextType(path)).ToList();
    }
}