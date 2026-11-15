namespace Nefarius.Utilities.ETW.Relay;

/// <summary>
///     The reason a relay frame or stream was rejected.
/// </summary>
public enum RawEtwProtocolError
{
    /// <summary>The frame is shorter than its declared or minimum length, or the stream ended inside a frame.</summary>
    Truncated,

    /// <summary>The frame does not start with the relay magic.</summary>
    BadMagic,

    /// <summary>The frame has a major version this implementation does not support.</summary>
    UnsupportedVersion,

    /// <summary>A length field is inconsistent with the frame, or exceeds a configured limit.</summary>
    InvalidLength,

    /// <summary>A reserved field is non-zero or a field holds a value the version does not define.</summary>
    InvalidField,

    /// <summary>The CRC32C does not match, or is required and absent.</summary>
    ChecksumMismatch,

    /// <summary>The first frame of a session was not a hello frame.</summary>
    HelloRequired,

    /// <summary>A hello frame arrived after the session was established.</summary>
    UnexpectedHello,

    /// <summary>A frame skipped one or more sequence numbers.</summary>
    OutOfOrder,

    /// <summary>A frame repeated a sequence number with different content, or one too old to verify.</summary>
    ConflictingReplay,

    /// <summary>A frame arrived after the end frame.</summary>
    AfterEnd,

    /// <summary>The producer announced a maximum frame length above the receiver's limit.</summary>
    ProducerLimitExceeded,

    /// <summary>
    ///     The frame was well-formed but its event could not be decoded. The frame is consumed, so the session can
    ///     continue with the next one.
    /// </summary>
    DecodeFailed
}

/// <summary>
///     Thrown when relay input violates the protocol or the configured <see cref="RawEtwRelayLimits" />.
/// </summary>
public sealed class RawEtwProtocolException : Exception
{
    /// <summary>
    ///     Creates the exception.
    /// </summary>
    public RawEtwProtocolException(RawEtwProtocolError error, string message) : base(message)
    {
        Error = error;
    }

    /// <summary>
    ///     Creates the exception with the failure that caused it.
    /// </summary>
    public RawEtwProtocolException(RawEtwProtocolError error, string message, Exception innerException) : base(
        message, innerException)
    {
        Error = error;
    }

    /// <summary>
    ///     The reason the input was rejected.
    /// </summary>
    public RawEtwProtocolError Error { get; }
}
