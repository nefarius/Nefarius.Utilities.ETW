using System.Diagnostics.CodeAnalysis;

using Nefarius.Utilities.ETW.Events;

namespace Nefarius.Utilities.ETW;

/// <summary>
///     Adjustments for <see cref="EtwUtil.EnumeratePdbReferences" />.
/// </summary>
[SuppressMessage("ReSharper", "UnusedMember.Global")]
public sealed class EtwMetadataScanOptions
{
    internal EtwMetadataScanOptions() { }

    /// <summary>
    ///     Reports potential scanning errors.
    /// </summary>
    public Action<string>? ReportError { get; set; }

    /// <summary>
    ///     Invoked for every <c>KernelTraceControl/ImageID/DbgID_RSDS</c> event (opcode 36, provider
    ///     <c>b3e675d7-2554-4f18-830b-2762732560de</c>) encountered in the trace.
    /// </summary>
    /// <remarks>
    ///     Each such event identifies a PDB that was used to instrument the code being traced.
    ///     The <see cref="DbgIdRsdsEventInfo.ToPdbMetaData" /> helper projects the event into the
    ///     <see cref="Deserializer.WPP.PdbMetaData" /> shape expected by
    ///     <see cref="EtwJsonConverterOptions.WppDecodingContext" />.
    /// </remarks>
    public Action<DbgIdRsdsEventInfo>? OnDbgIdRsds { get; set; }

    /// <summary>
    ///     Invoked for every <c>KernelTraceControl/ImageID</c> event (opcode 0, provider
    ///     <c>b3e675d7-2554-4f18-830b-2762732560de</c>) encountered in the trace.
    /// </summary>
    /// <remarks>
    ///     These events carry the base address, size, and original file name of a loaded image.
    /// </remarks>
    public Action<ImageIdEventInfo>? OnImageId { get; set; }

    /// <summary>
    ///     Invoked for every <c>KernelTraceControl/ImageID/FileVersion</c> event (opcode 64, provider
    ///     <c>b3e675d7-2554-4f18-830b-2762732560de</c>) encountered in the trace.
    /// </summary>
    /// <remarks>
    ///     These events carry the version-resource strings of a loaded image.
    /// </remarks>
    public Action<ImageIdFileVersionEventInfo>? OnImageIdFileVersion { get; set; }

    /// <summary>
    ///     Invoked for every <c>MSNT_SystemTrace/EventTrace/DbgIdRSDS</c> event emitted by the legacy kernel Event Trace
    ///     provider (<c>EventTraceGuid</c>) encountered in the trace.
    /// </summary>
    public Action<KernelDbgIdRsdsEventInfo>? OnKernelDbgIdRsds { get; set; }
}
