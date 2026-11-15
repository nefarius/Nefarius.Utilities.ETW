namespace Nefarius.Utilities.ETW.Relay;

/// <summary>
///     Resource limits the relay codec enforces before it allocates memory for, or decodes, a frame received from an
///     untrusted producer.
/// </summary>
/// <remarks>
///     All limits are checked against the lengths a frame declares, before any buffer sized from them is allocated.
/// </remarks>
public sealed class RawEtwRelayLimits
{
    /// <summary>
    ///     Largest value any limit can be raised to.
    /// </summary>
    public const int AbsoluteMaxFrameLength = 16 * 1024 * 1024;

    /// <summary>
    ///     Largest number of extended data items a single event may carry.
    /// </summary>
    public const int AbsoluteMaxExtendedItems = 256;

    /// <summary>
    ///     Largest number of distinct event keys one session may cache parsers for.
    /// </summary>
    public const int AbsoluteMaxCachedEventKeys = 65536;

    /// <summary>
    ///     Largest assembled manifest a session may retain.
    /// </summary>
    public const int AbsoluteMaxManifestBytes = 16 * 1024 * 1024;

    /// <summary>
    ///     The limits applied when none are supplied.
    /// </summary>
    public static RawEtwRelayLimits Default { get; } = new();

    /// <summary>
    ///     Maximum total frame length in bytes, prefix included. Default: 1 MiB.
    /// </summary>
    public int MaxFrameLength { get; init; } = 1024 * 1024;

    /// <summary>
    ///     Maximum number of extended data items per event. Default: 64.
    /// </summary>
    public int MaxExtendedItems { get; init; } = 64;

    /// <summary>
    ///     Maximum size of one extended data item in bytes. Default and maximum: 65535, the width of the ETW
    ///     <c>DataSize</c> field.
    /// </summary>
    public int MaxExtendedItemSize { get; init; } = ushort.MaxValue;

    /// <summary>
    ///     Maximum combined size of all extended data items of one event. Default: 512 KiB.
    /// </summary>
    public int MaxTotalExtendedBytes { get; init; } = 512 * 1024;

    /// <summary>
    ///     Maximum length of the producer description string in a hello frame. Default: 256.
    /// </summary>
    public int MaxProducerStringBytes { get; init; } = 256;

    /// <summary>
    ///     Maximum event payload size in bytes. Default and maximum: 65535, the width of the ETW
    ///     <c>UserDataLength</c> field.
    /// </summary>
    public int MaxPayloadLength { get; init; } = ushort.MaxValue;

    /// <summary>
    ///     When <see langword="true" />, frames without a CRC32C are rejected. Default: <see langword="false" />.
    /// </summary>
    public bool RequireCrc { get; init; }

    /// <summary>
    ///     Maximum distinct (provider, id, version, opcode) keys whose parsers one session retains. Further new keys are
    ///     rejected. Default: 8192.
    /// </summary>
    public int MaxCachedEventKeys { get; init; } = 8192;

    /// <summary>
    ///     Maximum total EventSource manifest text one session retains. Further manifest bytes are rejected.
    ///     Default: 1 MiB.
    /// </summary>
    public int MaxManifestBytes { get; init; } = 1024 * 1024;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxFrameLength, RawEtwFrameCodec.PrefixLength + 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxFrameLength, AbsoluteMaxFrameLength);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxExtendedItems);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxExtendedItems, AbsoluteMaxExtendedItems);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxExtendedItemSize);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxExtendedItemSize, ushort.MaxValue);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxTotalExtendedBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxProducerStringBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxPayloadLength);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxPayloadLength, ushort.MaxValue);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxCachedEventKeys);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxCachedEventKeys, AbsoluteMaxCachedEventKeys);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxManifestBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxManifestBytes, AbsoluteMaxManifestBytes);
    }
}
