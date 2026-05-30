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
    ///     Invoked whenever a WPP event is decoded but no matching <see cref="Deserializer.WPP.TMF.TraceMessageFormat" />
    ///     was found in the supplied <see cref="WppDecodingContext" /> (i.e. the event's <c>FormattedString</c> was
    ///     substituted with the <c>"GUID=... - No format information found."</c> placeholder).
    ///     Receives the provider trace GUID, the WPP event id, and the version.
    ///     Fires once per affected event; consumers are expected to deduplicate as needed.
    /// </summary>
    public Action<Guid, ushort, uint>? OnWppFormatMissing { get; set; }

    /// <summary>
    ///     Custom manifest provider lookup.
    /// </summary>
    public Func<Guid, Stream?>? CustomProviderManifest { get; set; }

    /// <summary>
    ///     <see cref="DecodingContext" /> to read WPP events.
    /// </summary>
    /// <remarks>
    ///     Build this context by calling <see cref="EtwUtil.EnumeratePdbReferences" /> first to discover all PDB
    ///     references in the trace, resolve each <see cref="PdbMetaData" /> to its actual <c>.pdb</c> file, then
    ///     construct a <see cref="DecodingContext" /> from the resulting
    ///     <see cref="PdbFileDecodingContextType" /> (or <see cref="TmfFilesDirectoryDecodingContextType" />) instances
    ///     before calling <see cref="EtwUtil.ConvertToJson" />.
    /// </remarks>
    public DecodingContext? WppDecodingContext { get; set; }

    /// <summary>
    ///     When <see langword="true" />, the <c>GuidName</c> / <c>Provider</c> field in decoded WPP events is
    ///     overridden with the friendly name declared in the <c>TMC:</c> <c>WPP_DEFINE_CONTROL_GUID</c> annotation
    ///     (e.g. <c>DsHidMiniTraceGuid</c>) instead of the raw TMF module token (e.g. <c>sys</c>).
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The override is resolved on a per-PDB basis: if a PDB declares exactly one control GUID whose
    ///         name is non-empty, every message format that originated from that PDB is mapped to that name.
    ///         PDBs with zero or more than one control GUID are skipped (ambiguous mapping) and the original
    ///         <c>format.Provider</c> value is kept unchanged — so the fallback is always silent and safe.
    ///     </para>
    ///     <para>
    ///         Has no effect when <see cref="WppDecodingContext" /> is not set or was built from TMF files only.
    ///     </para>
    ///     <para>Default is <see langword="false" /> (existing output is unchanged).</para>
    /// </remarks>
    public bool RewriteWppProviderName { get; set; }

    /// <summary>
    ///     If set, <c>PROCESS_TRACE_MODE_RAW_TIMESTAMP</c> will be applied when processing the trace record.
    /// </summary>
    /// <remarks>
    ///     See
    ///     <a href="https://learn.microsoft.com/en-us/windows/win32/api/evntrace/ns-evntrace-event_trace_logfilea#members">DUMMYUNIONNAME.ProcessTraceMode</a>
    /// </remarks>
    public bool PreserveRawTimestamps { get; set; }
}