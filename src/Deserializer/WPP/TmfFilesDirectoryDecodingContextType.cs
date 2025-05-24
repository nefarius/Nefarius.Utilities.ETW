using System.Diagnostics.CodeAnalysis;
using System.Text;

using Nefarius.Utilities.ETW.Deserializer.WPP.TMF;

namespace Nefarius.Utilities.ETW.Deserializer.WPP;

/// <summary>
///     A <see cref="TDH_CONTEXT_TYPE.TDH_CONTEXT_WPP_TMFFILE" />  wrapper for use with <see cref="DecodingContext" />.
/// </summary>
[SuppressMessage("ReSharper", "UnusedType.Global")]
public sealed class TmfFilesDirectoryDecodingContextType()
    : DecodingContextType(TDH_CONTEXT_TYPE.TDH_CONTEXT_WPP_TMFFILE)
{
    /// <summary>
    ///     Gets decoding info from a single <c>.tmf</c> file.
    /// </summary>
    /// <param name="path">
    ///     Null-terminated Unicode string that contains the name of the .tmf file used for parsing the WPP log.
    ///     Typically, the .tmf file name is picked up from the event GUID so you do not have to specify the file name.
    /// </param>
    public TmfFilesDirectoryDecodingContextType(string path) : this()
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        Buffer = new ReadOnlyMemory<byte>(Encoding.Unicode.GetBytes(Path.GetFullPath(path))
            .Concat("\0\0"u8.ToArray())
            .ToArray()
        );

        TmfParser tmfParser = new();

        TraceMessageFormats = tmfParser
            .ParseDirectory(path)
            .ToList()
            .AsReadOnly();
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