namespace Nefarius.Utilities.ETW.Relay;

/// <summary>
///     Settings of a relay decoding session that are independent of how events are decoded
///     (see <see cref="EtwJsonConverterOptions" /> for that).
/// </summary>
public sealed class RawEtwRelayOptions
{
    /// <summary>
    ///     The limits enforced on incoming frames. Defaults to <see cref="RawEtwRelayLimits.Default" />.
    /// </summary>
    public RawEtwRelayLimits Limits { get; set; } = RawEtwRelayLimits.Default;

    /// <summary>
    ///     Invoked when the session's hello frame is accepted.
    /// </summary>
    public Action<RawEtwHello>? OnHello { get; set; }

    /// <summary>
    ///     Invoked when the producer reports dropped events.
    /// </summary>
    public Action<RawEtwLoss>? OnLoss { get; set; }
}

/// <summary>
///     What happened to a frame handed to a relay decoder.
/// </summary>
public enum RawEtwFrameDisposition
{
    /// <summary>The hello frame was accepted.</summary>
    Hello,

    /// <summary>An event frame was decoded. The decoder may have produced zero events, e.g. for metadata records.</summary>
    Event,

    /// <summary>A loss report was recorded.</summary>
    Loss,

    /// <summary>The end frame was accepted; no further frames are valid.</summary>
    End,

    /// <summary>The frame of an unknown type was skipped.</summary>
    Ignored,

    /// <summary>The frame exactly repeated one already processed (a retry) and was dropped.</summary>
    Duplicate
}
