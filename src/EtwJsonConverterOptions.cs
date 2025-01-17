using Nefarius.Utilities.ETW.Deserializer.WPP;

namespace Nefarius.Utilities.ETW;

/// <summary>
///     Adjusstments for <see cref="EtwUtil" />.
/// </summary>
public sealed class EtwJsonConverterOptions
{
    internal EtwJsonConverterOptions() { }

    /// <summary>
    ///     Reports potential parsing errors.
    /// </summary>
    public Action<string>? ReportError { get; set; }

    /// <summary>
    ///     Custom manifest provider lookup.
    /// </summary>
    public Func<Guid, Stream?>? CustomProviderManifest { get; set; }

    /// <summary>
    ///     <see cref="DecodingContext" /> to read WPP events.
    /// </summary>
    public DecodingContext? DecodingContext { get; set; }
}