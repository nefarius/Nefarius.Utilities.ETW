using System.Diagnostics.CodeAnalysis;

namespace Nefarius.Utilities.ETW.Events;

/// <summary>
///     Strongly-typed representation of a <c>KernelTraceControl/ImageID/FileVersion</c> event (opcode 64,
///     provider <c>b3e675d7-2554-4f18-830b-2762732560de</c>).
/// </summary>
[SuppressMessage("ReSharper", "UnusedMember.Global")]
[SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
public readonly record struct ImageIdFileVersionEventInfo
{
    /// <summary>Raw event timestamp (100-nanosecond intervals since January 1, 1601).</summary>
    public long Timestamp { get; init; }

    /// <summary>Process ID of the process that logged the event.</summary>
    public uint ProcessId { get; init; }

    /// <summary>Thread ID of the thread that logged the event.</summary>
    public uint ThreadId { get; init; }

    /// <summary>Size of the image in bytes.</summary>
    public uint ImageSize { get; init; }

    /// <summary>PE timestamp / date-stamp from the image header.</summary>
    public uint TimeDateStamp { get; init; }

    /// <summary>Original file name of the image.</summary>
    public string OrigFileName { get; init; }

    /// <summary>Human-readable file description from the version resource.</summary>
    public string FileDescription { get; init; }

    /// <summary>File version string from the version resource.</summary>
    public string FileVersion { get; init; }

    /// <summary>Binary (numeric) file version from the version resource.</summary>
    public string BinFileVersion { get; init; }

    /// <summary>Language identifier of the version resource.</summary>
    public string VerLanguage { get; init; }

    /// <summary>Product name from the version resource.</summary>
    public string ProductName { get; init; }

    /// <summary>Company name from the version resource.</summary>
    public string CompanyName { get; init; }

    /// <summary>Product version string from the version resource.</summary>
    public string ProductVersion { get; init; }

    /// <summary>File identifier.</summary>
    public string FileId { get; init; }

    /// <summary>Program identifier.</summary>
    public string ProgramId { get; init; }
}
