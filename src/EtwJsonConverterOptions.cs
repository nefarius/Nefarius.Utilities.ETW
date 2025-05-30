using Nefarius.Utilities.ETW.Deserializer.WPP;

namespace Nefarius.Utilities.ETW;

/// <summary>
///     Adjustments for <see cref="EtwUtil" />.
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
    public DecodingContext? WppDecodingContext { get; set; }

    /// <summary>
    ///     Custom <see cref="DecodingContext" /> provider lookup.
    /// </summary>
    public Func<PdbMetaData, DecodingContextType?>? ContextProviderLookup { get; set; }

    /// <summary>
    ///     If set, <c>PROCESS_TRACE_MODE_RAW_TIMESTAMP</c> will be applied when processing the trace record.
    /// </summary>
    /// <remarks>
    ///     See
    ///     <a href="https://learn.microsoft.com/en-us/windows/win32/api/evntrace/ns-evntrace-event_trace_logfilea#members">DUMMYUNIONNAME.ProcessTraceMode</a>
    /// </remarks>
    public bool PreserveRawTimestamps { get; set; }
}