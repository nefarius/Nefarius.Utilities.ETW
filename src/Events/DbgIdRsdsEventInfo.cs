using System.Diagnostics.CodeAnalysis;

using Nefarius.Utilities.ETW.Deserializer.WPP;

namespace Nefarius.Utilities.ETW.Events;

/// <summary>
///     Strongly-typed representation of a <c>KernelTraceControl/ImageID/DbgID_RSDS</c> event (opcode 36,
///     provider <c>b3e675d7-2554-4f18-830b-2762732560de</c>).
/// </summary>
[SuppressMessage("ReSharper", "UnusedMember.Global")]
[SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
public readonly record struct DbgIdRsdsEventInfo
{
    /// <summary>Raw event timestamp (100-nanosecond intervals since January 1, 1601).</summary>
    public long Timestamp { get; init; }

    /// <summary>Process ID of the process that logged the event.</summary>
    public uint ProcessId { get; init; }

    /// <summary>Thread ID of the thread that logged the event.</summary>
    public uint ThreadId { get; init; }

    /// <summary>Base address of the image at the time the debug information was recorded.</summary>
    public ulong ImageBase { get; init; }

    /// <summary>The GUID embedded in the PDB that uniquely identifies the symbol file.</summary>
    public Guid GuidSig { get; init; }

    /// <summary>PDB age (revision counter).</summary>
    public uint Age { get; init; }

    /// <summary>Original file name of the PDB (may be a full path recorded at build time).</summary>
    public string PdbFileName { get; init; }

    /// <summary>Projects this event into the <see cref="PdbMetaData" /> structure used for symbol server lookups.</summary>
    public PdbMetaData ToPdbMetaData() => new() { Guid = GuidSig, Age = (int)Age, PdbName = PdbFileName };
}
