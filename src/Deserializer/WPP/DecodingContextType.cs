using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Nefarius.Utilities.ETW.Deserializer.WPP;

/// <summary>
///     Describes a decoding source for use with <see cref="DecodingContext" />.
/// </summary>
/// <param name="contextType"></param>
[SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
internal abstract class DecodingContextType(TDH_CONTEXT_TYPE contextType)
{
    public TDH_CONTEXT_TYPE ContextType { get; } = contextType;

    protected ReadOnlyMemory<byte> Buffer { get; init; }

    public unsafe TDH_CONTEXT AsContext()
    {
        fixed (byte* valueBuffer = Buffer.Span)
        {
            return new TDH_CONTEXT { ParameterType = ContextType, ParameterValue = (ulong)valueBuffer };
        }
    }
}

/// <summary>
///     A <see cref="TDH_CONTEXT_TYPE.TDH_CONTEXT_PDB_PATH" /> wrapper for use with <see cref="DecodingContext" />.
/// </summary>
internal sealed class PdbFilesDecodingContextType()
    : DecodingContextType(TDH_CONTEXT_TYPE.TDH_CONTEXT_PDB_PATH)
{
    /// <summary>
    ///     Gets decoding info from one or multiple <c>.PDB</c> files.
    /// </summary>
    /// <param name="pathList">
    ///     Null-terminated Unicode string that contains the name of the .pdb file for the binary that
    ///     contains WPP messages. This parameter can be used as an alternative to TDH_CONTEXT_WPP_TMFFILE or
    ///     TDH_CONTEXT_WPP_TMFSEARCHPATH.
    /// </param>
    public PdbFilesDecodingContextType(params IList<string> pathList) : this()
    {
        Buffer = new ReadOnlyMemory<byte>(Encoding.Unicode.GetBytes(string.Join(';', pathList)));
    }
}

/// <summary>
///     A <see cref="TDH_CONTEXT_TYPE.TDH_CONTEXT_WPP_TMFSEARCHPATH" />  wrapper for use with
///     <see cref="DecodingContext" />.
/// </summary>
[SuppressMessage("ReSharper", "UnusedType.Global")]
internal sealed class TmfFilesDecodingContextType()
    : DecodingContextType(TDH_CONTEXT_TYPE.TDH_CONTEXT_WPP_TMFSEARCHPATH)
{
    /// <summary>
    ///     Gets decoding info from multiple paths containing <c>.TMF</c> files.
    /// </summary>
    /// <param name="pathList">
    ///     Null-terminated Unicode string that contains the path to the .tmf file. You do not have to
    ///     specify this path if the search path contains the file. Only specify this context information if you also specify
    ///     the TDH_CONTEXT_WPP_TMFFILE context type. If the file is not found, TDH searches the following locations in the
    ///     given order:
    ///     <ul>
    ///         <li>The path specified in the TRACE_FORMAT_SEARCH_PATH environment variable</li>
    ///         <li>The current folder</li>
    ///     </ul>
    /// </param>
    public TmfFilesDecodingContextType(params IList<string> pathList) : this()
    {
        Buffer = new ReadOnlyMemory<byte>(Encoding.Unicode.GetBytes(string.Join(';', pathList)));
    }
}

/// <summary>
///     A <see cref="TDH_CONTEXT_TYPE.TDH_CONTEXT_WPP_TMFFILE" />  wrapper for use with <see cref="DecodingContext" />.
/// </summary>
[SuppressMessage("ReSharper", "UnusedType.Global")]
internal sealed class TmfFileDecodingContextType() : DecodingContextType(TDH_CONTEXT_TYPE.TDH_CONTEXT_WPP_TMFFILE)
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
        Buffer = new ReadOnlyMemory<byte>(Encoding.Unicode.GetBytes(path));
    }
}