namespace Nefarius.Utilities.ETW.OpenTelemetry;

/// <summary>
///     Names of the OTLP resource and log record attributes this integration reads and writes.
/// </summary>
public static class EtwOtlpAttributes
{
    /// <summary>Resource attribute: the relay session identifier (a GUID in "D" format).</summary>
    public const string RelaySessionId = "etw.relay.session_id";

    /// <summary>Log attribute: the producer's frame sequence number (int).</summary>
    public const string RelaySequence = "etw.relay.sequence";

    /// <summary>Log attribute: the relay frame type: hello, event, loss or end.</summary>
    public const string RelayFrameType = "etw.relay.frame_type";

    /// <summary>Log attribute: the number of events a loss frame reports as dropped (int).</summary>
    public const string RelayLostEvents = "etw.relay.lost_events";

    /// <summary>Log attribute: the provider GUID.</summary>
    public const string ProviderId = "etw.provider.id";

    /// <summary>Log attribute: the event ID (int).</summary>
    public const string EventId = "etw.event.id";

    /// <summary>Log attribute: the event version (int).</summary>
    public const string EventVersion = "etw.event.version";

    /// <summary>Log attribute: the process ID (int).</summary>
    public const string ProcessId = "etw.process.id";

    /// <summary>Log attribute: the thread ID (int).</summary>
    public const string ThreadId = "etw.thread.id";

    /// <summary>Log attribute: the processor number (int).</summary>
    public const string Processor = "etw.processor";

    /// <summary>Log attribute: the activity ID (GUID string), when set.</summary>
    public const string ActivityId = "etw.activity.id";

    /// <summary>Log attribute: the related activity ID (GUID string), when set.</summary>
    public const string RelatedActivityId = "etw.activity.related_id";

    /// <summary>Log attribute: the ETW level (int).</summary>
    public const string Level = "etw.level";

    /// <summary>Log attribute: the opcode (int).</summary>
    public const string Opcode = "etw.opcode";

    /// <summary>Log attribute: the task (int).</summary>
    public const string Task = "etw.task";

    /// <summary>Log attribute: the channel (int).</summary>
    public const string Channel = "etw.channel";

    /// <summary>Log attribute: the keyword bitmask as a 0x-prefixed hexadecimal string.</summary>
    public const string Keyword = "etw.keyword";

    /// <summary>Log attribute: the event header flags (int).</summary>
    public const string Flags = "etw.flags";

    /// <summary>Log attribute: the stack match ID as hexadecimal string, when present.</summary>
    public const string StackMatchId = "etw.stack.match_id";

    /// <summary>Log attribute: the captured stack as an array of hexadecimal address strings, when present.</summary>
    public const string Stack = "etw.stack";

    /// <summary>Prefix of the log attributes holding the properties of a decoded WPP event.</summary>
    public const string WppPrefix = "etw.wpp.";

    /// <summary>The name WPP events carry.</summary>
    public const string WppEventName = "WPP";
}
