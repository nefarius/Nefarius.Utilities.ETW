using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Nefarius.Utilities.ETW.Deserializer.WPP;

/// <summary>
///     A <see cref="TDH_CONTEXT_TYPE.TDH_CONTEXT_WPP_TMFSEARCHPATH" />  wrapper for use with
///     <see cref="DecodingContext" />.
/// </summary>
[SuppressMessage("ReSharper", "UnusedType.Global")]
public sealed class TmfFileDecodingContextType()
    : DecodingContextType(TDH_CONTEXT_TYPE.TDH_CONTEXT_WPP_TMFSEARCHPATH)
{
    /// <summary>
    ///     Gets decoding info from multiple paths containing <c>.TMF</c> files.
    /// </summary>
    /// <param name="path">
    ///     Null-terminated Unicode string that contains the path to the .tmf file. You do not have to
    ///     specify this path if the search path contains the file. Only specify this context information if you also specify
    ///     the TDH_CONTEXT_WPP_TMFFILE context type. If the file is not found, TDH searches the following locations in the
    ///     given order:
    ///     <ul>
    ///         <li>The path specified in the TRACE_FORMAT_SEARCH_PATH environment variable</li>
    ///         <li>The current folder</li>
    ///     </ul>
    /// </param>
    public TmfFileDecodingContextType(string path) : this()
    {
        ArgumentNullException.ThrowIfNull(path);
        Buffer = new ReadOnlyMemory<byte>(Encoding.Unicode.GetBytes(Path.GetFullPath(path))
            .Concat("\0\0"u8.ToArray())
            .ToArray()
        );
    }
}