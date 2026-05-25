namespace Nefarius.Utilities.ETW;

/// <summary>
///     Specifies the verbosity level of events delivered from a provider to a realtime ETW session.
///     Values match the <c>TRACE_LEVEL_*</c> constants defined in <c>evntprov.h</c>.
/// </summary>
public enum TraceEventLevel : byte
{
    /// <summary>No tracing. The provider is effectively disabled.</summary>
    None = 0,

    /// <summary>Abnormal exit or termination events.</summary>
    Critical = 1,

    /// <summary>Severe errors that need logging.</summary>
    Error = 2,

    /// <summary>Warnings such as resource allocation failures.</summary>
    Warning = 3,

    /// <summary>Non-error informational events, such as entry and exit points.</summary>
    Information = 4,

    /// <summary>Detailed trace events from intermediate processing steps.</summary>
    Verbose = 5
}
