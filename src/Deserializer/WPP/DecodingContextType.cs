using System.Collections.ObjectModel;

using Nefarius.Utilities.ETW.Deserializer.WPP.TMF;

namespace Nefarius.Utilities.ETW.Deserializer.WPP;

/// <summary>
///     Describes a decoding source for use with one or more <see cref="DecodingContext" />s.
/// </summary>
public abstract class DecodingContextType
{
    /// <summary>
    ///     Collection of extracted <see cref="TraceMessageFormat" />s of this <see cref="DecodingContextType" />.
    /// </summary>
    public ReadOnlyCollection<TraceMessageFormat> TraceMessageFormats { get; protected init; } = null!;
}