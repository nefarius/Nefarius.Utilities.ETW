using System.Diagnostics.CodeAnalysis;

using Nefarius.Utilities.ETW.Deserializer.WPP.TMF;

namespace Nefarius.Utilities.ETW.Deserializer.WPP;

/// <summary>
///     A <see cref="TDH_CONTEXT_TYPE.TDH_CONTEXT_WPP_TMFFILE" /> wrapper for use with <see cref="DecodingContext" />.
/// </summary>
[SuppressMessage("ReSharper", "UnusedType.Global")]
public sealed class TmfFileDecodingContextType()
    : DecodingContextType
{
    /// <summary>
    ///     Gets decoding info from a single <c>.tmf</c> file.
    /// </summary>
    /// <param name="path">Path to a single <c>.tmf</c> file.</param>
    public TmfFileDecodingContextType(string path) : this()
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        TraceMessageFormats = TmfParser
            .ParseFile(path)
            .Distinct();
    }

    /// <summary>
    ///     Converts a list of paths to <c>*.tmf</c> files into their corresponding
    ///     <see cref="TmfFileDecodingContextType" /> objects.
    /// </summary>
    /// <param name="pathList">One or more paths to <c>.tmf</c> files.</param>
    /// <returns>One or more <see cref="TmfFileDecodingContextType" />s.</returns>
    public static IList<DecodingContextType> CreateFrom(params IList<string> pathList)
    {
        ArgumentNullException.ThrowIfNull(pathList, nameof(pathList));

        return pathList.Select(DecodingContextType (path) => new TmfFileDecodingContextType(path)).ToList();
    }
}