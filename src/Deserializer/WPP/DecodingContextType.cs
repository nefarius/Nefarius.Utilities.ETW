using System.Text;

namespace Nefarius.Utilities.ETW.Deserializer.WPP;

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
///     <see cref="TDH_CONTEXT_TYPE.TDH_CONTEXT_PDB_PATH" /> wrapper.
/// </summary>
internal sealed class PdbFilesDecodingContextType()
    : DecodingContextType(TDH_CONTEXT_TYPE.TDH_CONTEXT_PDB_PATH)
{
    public PdbFilesDecodingContextType(params IList<string> pathList) : this()
    {
        Buffer = new ReadOnlyMemory<byte>(Encoding.Unicode.GetBytes(string.Join(';', pathList)));
    }
}

/// <summary>
///     <see cref="TDH_CONTEXT_TYPE.TDH_CONTEXT_WPP_TMFSEARCHPATH" /> wrapper.
/// </summary>
internal sealed class TmfFilesDecodingContextType()
    : DecodingContextType(TDH_CONTEXT_TYPE.TDH_CONTEXT_WPP_TMFSEARCHPATH)
{
    public TmfFilesDecodingContextType(params IList<string> pathList) : this()
    {
        Buffer = new ReadOnlyMemory<byte>(Encoding.Unicode.GetBytes(string.Join(';', pathList)));
    }
}

/// <summary>
///     <see cref="TDH_CONTEXT_TYPE.TDH_CONTEXT_WPP_TMFFILE" /> wrapper.
/// </summary>
internal sealed class TmfFileDecodingContextType() : DecodingContextType(TDH_CONTEXT_TYPE.TDH_CONTEXT_WPP_TMFFILE)
{
    public TmfFileDecodingContextType(string path) : this()
    {
        Buffer = new ReadOnlyMemory<byte>(Encoding.Unicode.GetBytes(path));
    }
}