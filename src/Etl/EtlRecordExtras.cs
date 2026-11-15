namespace Nefarius.Utilities.ETW.Etl;

/// <summary>
///     Header details of a record that the native <c>EVENT_RECORD</c> layout cannot carry. The native
///     <c>ProcessTrace</c> path hands these to TDH; the managed path supplies them directly.
/// </summary>
internal readonly struct EtlRecordExtras
{
    /// <summary>
    ///     True when the record came from the managed ETL reader.
    /// </summary>
    public bool IsManaged { get; init; }

    /// <summary>
    ///     WPP message sequence number, 0 when the record carries none.
    /// </summary>
    public uint SequenceNumber { get; init; }

    /// <summary>
    ///     Event time as a FILETIME (UTC), regardless of <c>PreserveRawTimestamps</c>.
    /// </summary>
    public long FileTime { get; init; }

    /// <summary>
    ///     Raw header version byte (for WPP, the message header version).
    /// </summary>
    public byte HeaderVersion { get; init; }
}
