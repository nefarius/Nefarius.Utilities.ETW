namespace Nefarius.Utilities.ETW.Relay;

/// <summary>
///     The kind of a relay frame.
/// </summary>
/// <remarks>
///     Receivers ignore (but still sequence) frame types they do not know, so minor protocol versions can add types.
///     Type 2 is reserved.
/// </remarks>
public enum RawEtwFrameType : ushort
{
    /// <summary>Opens a session. Must be the first frame, with sequence number 0.</summary>
    Hello = 0,

    /// <summary>One captured ETW event record.</summary>
    Event = 1,

    /// <summary>Reports events the producer had to drop.</summary>
    Loss = 3,

    /// <summary>Orderly end of the session.</summary>
    End = 4
}

/// <summary>
///     How <see cref="RawEtwRecord.TimeStamp" /> is expressed.
/// </summary>
public enum RawEtwTimestampKind : byte
{
    /// <summary>Not specified. Rejected by version 1 receivers.</summary>
    Unknown = 0,

    /// <summary>100-nanosecond intervals since 1601-01-01 UTC, the <c>ProcessTrace</c> default.</summary>
    FileTime = 1
}

/// <summary>
///     One extended data item of an ETW event, copied by value.
/// </summary>
public sealed class RawEtwExtendedData
{
    /// <summary>The <c>Reserved1</c> field of <c>EVENT_HEADER_EXTENDED_DATA_ITEM</c>.</summary>
    public ushort Reserved1 { get; set; }

    /// <summary>The <c>ExtType</c> field, e.g. 11 for a TraceLogging schema.</summary>
    public ushort ExtType { get; set; }

    /// <summary>The 16 bits holding <c>Linkage</c> and reserved flags.</summary>
    public ushort Linkage { get; set; }

    /// <summary>The item data, exactly <c>DataSize</c> bytes.</summary>
    public byte[] Data { get; set; } = [];
}

/// <summary>
///     A pointer-free copy of a native <c>EVENT_RECORD</c>.
/// </summary>
/// <remarks>
///     The members mirror <c>EVENT_HEADER</c> field by field. GUIDs are held as <see cref="Guid" /> values; the wire
///     format stores them in RFC 4122 byte order.
/// </remarks>
public sealed class RawEtwRecord
{
    /// <summary><c>EVENT_HEADER.Size</c>.</summary>
    public ushort Size { get; set; }

    /// <summary><c>EVENT_HEADER.HeaderType</c>.</summary>
    public ushort HeaderType { get; set; }

    /// <summary><c>EVENT_HEADER.Flags</c>.</summary>
    public ushort Flags { get; set; }

    /// <summary><c>EVENT_HEADER.EventProperty</c>.</summary>
    public ushort EventProperty { get; set; }

    /// <summary><c>EVENT_HEADER.ThreadId</c>.</summary>
    public uint ThreadId { get; set; }

    /// <summary><c>EVENT_HEADER.ProcessId</c>.</summary>
    public uint ProcessId { get; set; }

    /// <summary><c>EVENT_HEADER.TimeStamp</c>, expressed as <see cref="TimestampKind" /> says.</summary>
    public long TimeStamp { get; set; }

    /// <summary><c>EVENT_HEADER.ProviderId</c>.</summary>
    public Guid ProviderId { get; set; }

    /// <summary><c>EVENT_DESCRIPTOR.Id</c>.</summary>
    public ushort Id { get; set; }

    /// <summary><c>EVENT_DESCRIPTOR.Version</c>.</summary>
    public byte Version { get; set; }

    /// <summary><c>EVENT_DESCRIPTOR.Channel</c>.</summary>
    public byte Channel { get; set; }

    /// <summary><c>EVENT_DESCRIPTOR.Level</c>.</summary>
    public byte Level { get; set; }

    /// <summary><c>EVENT_DESCRIPTOR.Opcode</c>.</summary>
    public byte Opcode { get; set; }

    /// <summary><c>EVENT_DESCRIPTOR.Task</c>.</summary>
    public ushort Task { get; set; }

    /// <summary><c>EVENT_DESCRIPTOR.Keyword</c>.</summary>
    public ulong Keyword { get; set; }

    /// <summary>The 8-byte union of <c>KernelTime</c>/<c>UserTime</c> and <c>ProcessorTime</c>.</summary>
    public ulong ProcessorTime { get; set; }

    /// <summary><c>EVENT_HEADER.ActivityId</c>.</summary>
    public Guid ActivityId { get; set; }

    /// <summary>The raw 32-bit <c>ETW_BUFFER_CONTEXT</c>; its first byte is the processor number.</summary>
    public uint BufferContext { get; set; }

    /// <summary>How <see cref="TimeStamp" /> is expressed. Version 1 only defines <see cref="RawEtwTimestampKind.FileTime" />.</summary>
    public RawEtwTimestampKind TimestampKind { get; set; } = RawEtwTimestampKind.FileTime;

    /// <summary>Pointer size of the producing process or system: 4 or 8.</summary>
    public byte PointerSize { get; set; } = 8;

    /// <summary>True when <see cref="WppSequenceNumber" /> carries a value.</summary>
    public bool WppSequenceValid { get; set; }

    /// <summary>The WPP message sequence number, valid only when <see cref="WppSequenceValid" /> is set.</summary>
    public uint WppSequenceNumber { get; set; }

    /// <summary>The event payload (<c>UserData</c>), exactly <c>UserDataLength</c> bytes.</summary>
    public byte[] Payload { get; set; } = [];

    /// <summary>The extended data items, in record order.</summary>
    public List<RawEtwExtendedData> ExtendedData { get; } = [];
}

/// <summary>
///     Content of a hello frame.
/// </summary>
public sealed class RawEtwHello
{
    /// <summary>
    ///     Identifies the capture session. Decoder state is tied to this value. A producer that restarts its capture
    ///     must pick a new one.
    /// </summary>
    public Guid SessionId { get; set; }

    /// <summary>Producer capability bits. None are defined in version 1; send 0.</summary>
    public uint Capabilities { get; set; }

    /// <summary>The largest frame the producer will send.</summary>
    public uint MaxFrameLength { get; set; }

    /// <summary>A free-form UTF-8 description of the producer, for diagnostics.</summary>
    public string Producer { get; set; } = string.Empty;
}

/// <summary>
///     Content of a loss frame.
/// </summary>
public sealed class RawEtwLoss
{
    /// <summary>The number of events dropped immediately before this frame.</summary>
    public ulong LostEvents { get; set; }

    /// <summary>A producer defined reason code. 0 means unspecified, 1 a full producer queue, 2 ETW buffer loss.</summary>
    public uint Reason { get; set; }
}

/// <summary>
///     A decoded relay frame.
/// </summary>
public sealed class RawEtwFrame
{
    /// <summary>The frame kind.</summary>
    public RawEtwFrameType Type { get; set; }

    /// <summary>The producer's monotonic frame sequence number; the hello frame is 0.</summary>
    public ulong Sequence { get; set; }

    /// <summary>Set for <see cref="RawEtwFrameType.Hello" />.</summary>
    public RawEtwHello? Hello { get; set; }

    /// <summary>Set for <see cref="RawEtwFrameType.Event" />.</summary>
    public RawEtwRecord? Record { get; set; }

    /// <summary>Set for <see cref="RawEtwFrameType.Loss" />.</summary>
    public RawEtwLoss? Loss { get; set; }
}
