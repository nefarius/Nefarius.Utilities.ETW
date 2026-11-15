namespace Nefarius.Utilities.ETW.Events;

/// <summary>
///     A decoded ETW event with typed metadata and properties, independent of any serialization or telemetry format.
/// </summary>
public sealed class EtwEvent
{
    /// <summary>
    ///     The header timestamp exactly as the JSON output reports it: a FILETIME unless raw timestamps were
    ///     requested.
    /// </summary>
    public long Timestamp { get; init; }

    /// <summary>
    ///     <see cref="Timestamp" /> as UTC time, or <see langword="null" /> when it is not a valid FILETIME.
    /// </summary>
    public DateTime? TimestampUtc
    {
        get
        {
            try
            {
                return Timestamp > 0 ? DateTime.FromFileTimeUtc(Timestamp) : null;
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }
    }

    /// <summary>The provider GUID; for WPP events the message GUID.</summary>
    public Guid ProviderId { get; init; }

    /// <summary>The event name, e.g. <c>WPP</c> or the TraceLogging event name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The event ID.</summary>
    public ushort Id { get; init; }

    /// <summary>The event version.</summary>
    public byte Version { get; init; }

    /// <summary>The process ID.</summary>
    public uint ProcessId { get; init; }

    /// <summary>The thread ID.</summary>
    public uint ThreadId { get; init; }

    /// <summary>The processor number as the JSON output reports it.</summary>
    public ulong ProcessorNumber { get; init; }

    /// <summary>The activity ID, or <see cref="Guid.Empty" />.</summary>
    public Guid ActivityId { get; init; }

    /// <summary>The related activity ID, or <see cref="Guid.Empty" />.</summary>
    public Guid RelatedActivityId { get; init; }

    /// <summary>The event header flags.</summary>
    public ushort Flags { get; init; }

    /// <summary>The channel.</summary>
    public byte Channel { get; init; }

    /// <summary>The level (1 critical to 5 verbose, 0 unspecified).</summary>
    public byte Level { get; init; }

    /// <summary>The opcode.</summary>
    public byte Opcode { get; init; }

    /// <summary>The task.</summary>
    public ushort Task { get; init; }

    /// <summary>The keyword bitmask.</summary>
    public ulong Keyword { get; init; }

    /// <summary>The stack match ID, or 0.</summary>
    public ulong StackMatchId { get; init; }

    /// <summary>The captured stack, or <see langword="null" />.</summary>
    public IReadOnlyList<ulong>? Stacks { get; init; }

    /// <summary>The decoded properties as an <see cref="EtwValueKind.Object" /> value.</summary>
    public EtwValue Properties { get; internal set; } = EtwValue.FromObject([]);
}
