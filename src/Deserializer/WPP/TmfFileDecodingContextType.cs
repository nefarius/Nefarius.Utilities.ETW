using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Nefarius.Utilities.ETW.Deserializer.WPP;

/// <summary>
///     A <see cref="TDH_CONTEXT_TYPE.TDH_CONTEXT_WPP_TMFFILE" />  wrapper for use with <see cref="DecodingContext" />.
/// </summary>
[SuppressMessage("ReSharper", "UnusedType.Global")]
[Obsolete]
public sealed class TmfFileDecodingContextType() : DecodingContextType(TDH_CONTEXT_TYPE.TDH_CONTEXT_WPP_TMFFILE)
{
    /// <summary>
    ///     Gets decoding info from a single <c>.TMF</c> file.
    /// </summary>
    /// <param name="path">
    ///     Null-terminated Unicode string that contains the name of the .tmf file used for parsing the WPP log.
    ///     Typically, the .tmf file name is picked up from the event GUID so you do not have to specify the file name.
    /// </param>
    public TmfFileDecodingContextType(string path) : this()
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        Buffer = new ReadOnlyMemory<byte>(Encoding.Unicode.GetBytes(Path.GetFullPath(path))
            .Concat("\0\0"u8.ToArray())
            .ToArray()
        );
    }
}