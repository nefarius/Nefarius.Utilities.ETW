using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace Nefarius.Utilities.ETW.Deserializer.WPP.TMF;

/// <summary>
///     Describes a single WPP <c>WPP_DEFINE_CONTROL_GUID</c> declaration recovered from a PDB.
///     The <see cref="ControlGuid" /> is the GUID passed to <c>EnableTraceEx2</c> to enable
///     the WPP provider on an ETW session.
/// </summary>
/// <remarks>
///     <para>
///         In the PDB symbol stream WPP places one <c>S_ANNOTATION</c> record per
///         <c>WPP_DEFINE_CONTROL_GUID</c> declaration. The annotation strings are laid out as:
///     </para>
///     <list type="number">
///         <item><c>"TMC:"</c> — marker that identifies this as a trace control record</item>
///         <item><c>"&lt;control-guid&gt;"</c> — the WPP control GUID in textual form</item>
///         <item><c>"&lt;control-name&gt;"</c> — the friendly control GUID name</item>
///         <item><c>"&lt;flag-bit-N&gt;"</c> — zero or more <c>WPP_DEFINE_BIT</c> flag names</item>
///     </list>
/// </remarks>
[SuppressMessage("ReSharper", "UnusedMember.Global")]
[SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
public sealed record WppTraceControl
{
    /// <summary>
    ///     The WPP trace control GUID. This is the GUID that identifies the provider in ETW
    ///     and that must be passed to <c>EnableTraceEx2</c> to enable the provider on a session.
    /// </summary>
    public required Guid ControlGuid { get; init; }

    /// <summary>
    ///     The friendly name of the control GUID as declared via the first argument of the
    ///     <c>WPP_DEFINE_CONTROL_GUID</c> macro (for example <c>BthPS3TraceGuid</c>).
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    ///     The ordered list of <c>WPP_DEFINE_BIT</c> flag names declared for this control GUID.
    ///     Bit index 0 corresponds to ETW keyword <c>0x1</c>, bit index 1 to <c>0x2</c>, and so on.
    /// </summary>
    public required IReadOnlyList<string> BitFlags { get; init; } = new ReadOnlyCollection<string>([]);

    /// <summary>
    ///     The name of the original symbol file the control GUID was extracted from, if known.
    /// </summary>
    public string? OriginalSymbolFileName { get; init; }
}
