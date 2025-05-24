using System.Collections.ObjectModel;
using System.Text;

using Nefarius.Utilities.ETW.Deserializer.WPP.TMF;

namespace Nefarius.Utilities.ETW.Deserializer.WPP;

/// <summary>
///     Describes a decoding source for use with <see cref="DecodingContext" />.
/// </summary>
public abstract class DecodingContextType
{
    internal DecodingContextType(TDH_CONTEXT_TYPE contextType)
    {
        ContextType = contextType;
    }

    [Obsolete]
    private TDH_CONTEXT_TYPE ContextType { get; }

    [Obsolete]
    protected ReadOnlyMemory<byte> Buffer { get; init; }

    /// <summary>
    ///     Collection of extracted <see cref="TraceMessageFormat" />s of this <see cref="DecodingContextType" />.
    /// </summary>
    public ReadOnlyCollection<TraceMessageFormat> TraceMessageFormats { get; protected set; }

    /// <summary>
    ///     Managed string representation of <see cref="Buffer" /> content.
    /// </summary>
    [Obsolete]
    protected string BufferValue => Encoding.Unicode.GetString(Buffer.Span[..(Buffer.Length - 2)]);

    /// <summary>
    ///     Turns this instance into a <see cref="TDH_CONTEXT" /> for use with the TDH APIs.
    /// </summary>
    /// <returns>An instance of <see cref="TDH_CONTEXT" />.</returns>
    [Obsolete]
    internal unsafe TDH_CONTEXT AsContext()
    {
        fixed (byte* valueBuffer = Buffer.Span)
        {
            return new TDH_CONTEXT
            {
                // the lookup type
                ParameterType = ContextType,
                // pointer to the value (typically a filesystem path)
                ParameterValue = (ulong)valueBuffer,
                /* Reserved. Set to 0. */
                ParameterSize = 0
            };
        }
    }
}