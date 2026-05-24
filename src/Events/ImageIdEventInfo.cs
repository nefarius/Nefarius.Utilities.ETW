using System.Diagnostics.CodeAnalysis;

namespace Nefarius.Utilities.ETW.Events;

/// <summary>
///     Strongly-typed representation of a <c>KernelTraceControl/ImageID</c> event (opcode 0,
///     provider <c>b3e675d7-2554-4f18-830b-2762732560de</c>).
/// </summary>
[SuppressMessage("ReSharper", "UnusedMember.Global")]
[SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
public readonly record struct ImageIdEventInfo
{
    /// <summary>Raw event timestamp (100-nanosecond intervals since January 1, 1601).</summary>
    public long Timestamp { get; init; }

    /// <summary>Process ID of the process that logged the event.</summary>
    public uint ProcessId { get; init; }

    /// <summary>Thread ID of the thread that logged the event.</summary>
    public uint ThreadId { get; init; }

    /// <summary>Base virtual address at which the image was loaded.</summary>
    public ulong ImageBase { get; init; }

    /// <summary>Size of the image in bytes.</summary>
    public uint ImageSize { get; init; }

    /// <summary>PE timestamp / date-stamp from the image header.</summary>
    public uint TimeDateStamp { get; init; }

    /// <summary>Original file name of the image as recorded in the trace.</summary>
    public string OriginalFileName { get; init; }
}
