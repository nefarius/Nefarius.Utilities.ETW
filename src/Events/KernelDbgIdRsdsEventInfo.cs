using System.Diagnostics.CodeAnalysis;

using Nefarius.Utilities.ETW.Deserializer.WPP;

namespace Nefarius.Utilities.ETW.Events;

/// <summary>
///     Strongly-typed representation of a <c>MSNT_SystemTrace/EventTrace/DbgIdRSDS</c> event emitted by the
///     legacy kernel Event Trace provider (<see cref="PInvoke.EventTraceGuid" />).
/// </summary>
/// <remarks>
///     The layout of this event is: <c>Guid</c> (16 bytes) + <c>Age</c> (uint32) + null-terminated ANSI PDB name.
///     This is distinct from the <see cref="DbgIdRsdsEventInfo" /> emitted by the
///     <c>KernelTraceControl/ImageID</c> provider, which prepends <c>ImageBase</c> and <c>ProcessId</c>.
/// </remarks>
[SuppressMessage("ReSharper", "UnusedMember.Global")]
[SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
public readonly record struct KernelDbgIdRsdsEventInfo
{
    /// <summary>Raw event timestamp (100-nanosecond intervals since January 1, 1601).</summary>
    public long Timestamp { get; init; }

    /// <summary>Process ID of the process that logged the event.</summary>
    public uint ProcessId { get; init; }

    /// <summary>Thread ID of the thread that logged the event.</summary>
    public uint ThreadId { get; init; }

    /// <summary>The GUID embedded in the PDB that uniquely identifies the symbol file.</summary>
    public Guid Guid { get; init; }

    /// <summary>PDB age (revision counter).</summary>
    public uint Age { get; init; }

    /// <summary>Original file name of the PDB (may be a full path recorded at build time).</summary>
    public string PdbName { get; init; }

    /// <summary>Projects this event into the <see cref="PdbMetaData" /> structure used for symbol server lookups.</summary>
    public PdbMetaData ToPdbMetaData() => new() { Guid = Guid, Age = (int)Age, PdbName = PdbName };
}
