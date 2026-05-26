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
    public IEnumerable<TraceMessageFormat> TraceMessageFormats { get; protected set; } = null!;

    /// <summary>
    ///     The distinct set of WPP trace control GUIDs (= ETW provider GUIDs) available from this
    ///     decoding source.  Subclasses that carry the provider GUID — currently only
    ///     <see cref="PdbFileDecodingContextType" /> via its <c>TMC:</c> annotations — override this
    ///     property.  TMF-only sources return an empty collection because <c>.tmf</c> files only
    ///     contain per-call-site message hash GUIDs, not the WPP control GUID.
    /// </summary>
    public virtual IEnumerable<Guid> ProviderGuids => [];
}
