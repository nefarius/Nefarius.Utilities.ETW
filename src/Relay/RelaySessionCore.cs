using System.Security.Cryptography;

using Nefarius.Utilities.ETW.Deserializer;

namespace Nefarius.Utilities.ETW.Relay;

/// <summary>
///     The transport independent state machine of one relay session: sequencing, replay handling and decoding. One
///     instance owns one <see cref="Deserializer{T}" /> so its parser and manifest caches live as long as the
///     session. Not thread-safe.
/// </summary>
internal sealed unsafe class RelaySessionCore<TWriter> : IDisposable where TWriter : IEtwWriter
{
    /// <summary>How many already processed frames can still be recognized as exact retries.</summary>
    private const int DuplicateWindow = 64;

    private const int HashLength = 32;

    private readonly Deserializer<TWriter> _deserializer;
    private readonly HashSet<EventKey> _eventKeys = [];
    private readonly byte[] _hashes = new byte[DuplicateWindow * HashLength];
    private readonly ulong[] _hashedSequence = new ulong[DuplicateWindow];
    private readonly RawEtwRelayLimits _limits;
    private readonly RawEtwRelayOptions _options;
    private readonly RelayRecordBuffer _recordBuffer = new();

    private bool _disposed;

    public RelaySessionCore(TWriter writer, EtwJsonConverterOptions converterOptions, RawEtwRelayOptions? options)
    {
        _options = options ?? new RawEtwRelayOptions();
        _limits = _options.Limits ?? RawEtwRelayLimits.Default;
        _limits.Validate();

        _deserializer = new Deserializer<TWriter>(
            writer,
            converterOptions.CustomProviderManifest,
            converterOptions.WppDecodingContext,
            converterOptions.OnWppFormatMissing,
            converterOptions.RewriteWppProviderName,
            _limits.MaxManifestBytes);
    }

    /// <summary>The session identifier from the hello frame, or <see langword="null" /> before it.</summary>
    public Guid? SessionId { get; private set; }

    /// <summary>The next sequence number the session accepts.</summary>
    public ulong NextSequence { get; private set; }

    /// <summary>True once the end frame was accepted.</summary>
    public bool Ended { get; private set; }

    /// <summary>The total number of events the producer reported as dropped.</summary>
    public ulong LostEvents { get; private set; }

    /// <summary>The number of event frames decoded.</summary>
    public ulong EventFrames { get; private set; }

    public void Dispose()
    {
        _disposed = true;
        _recordBuffer.Dispose();
    }

    /// <summary>
    ///     Validates and applies one frame. Decoded events reach the writer synchronously.
    /// </summary>
    /// <exception cref="RawEtwProtocolException">The frame violates the protocol or the limits.</exception>
    public RawEtwFrameDisposition Process(ReadOnlySpan<byte> wire)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        RawEtwFrame frame = RawEtwFrameCodec.Decode(wire, _limits);

        Span<byte> hash = stackalloc byte[HashLength];
        SHA256.HashData(wire, hash);

        if (frame.Sequence < NextSequence)
        {
            return IsKnownRetry(frame.Sequence, hash)
                ? RawEtwFrameDisposition.Duplicate
                : throw new RawEtwProtocolException(RawEtwProtocolError.ConflictingReplay,
                    $"Frame {frame.Sequence} repeats a processed sequence number with different or unverifiable content.");
        }

        if (frame.Sequence > NextSequence)
        {
            throw new RawEtwProtocolException(RawEtwProtocolError.OutOfOrder,
                $"Expected sequence {NextSequence} but received {frame.Sequence}.");
        }

        if (Ended)
        {
            throw new RawEtwProtocolException(RawEtwProtocolError.AfterEnd, "The session already ended.");
        }

        if (SessionId is null && frame.Type != RawEtwFrameType.Hello)
        {
            throw new RawEtwProtocolException(RawEtwProtocolError.HelloRequired,
                "The first frame of a session must be a hello frame.");
        }

        if (SessionId is not null && frame.Type == RawEtwFrameType.Hello)
        {
            throw new RawEtwProtocolException(RawEtwProtocolError.UnexpectedHello,
                "The session is already established.");
        }

        if (frame.Type == RawEtwFrameType.Hello &&
            frame.Hello!.MaxFrameLength > (uint)_limits.MaxFrameLength)
        {
            throw new RawEtwProtocolException(RawEtwProtocolError.ProducerLimitExceeded,
                $"The producer may send frames of {frame.Hello.MaxFrameLength} bytes; the limit is {_limits.MaxFrameLength}.");
        }

        // The frame is valid and consumed: advance first, so a failure while decoding it does not make the
        // producer's retry look like a new frame.
        int slot = (int)(frame.Sequence % DuplicateWindow);
        hash.CopyTo(_hashes.AsSpan(slot * HashLength, HashLength));
        _hashedSequence[slot] = frame.Sequence;
        NextSequence = frame.Sequence + 1;

        switch (frame.Type)
        {
            case RawEtwFrameType.Hello:
                SessionId = frame.Hello!.SessionId;
                _options.OnHello?.Invoke(frame.Hello);
                return RawEtwFrameDisposition.Hello;

            case RawEtwFrameType.Event:
                ReserveEventKey(frame.Record!);
                EventFrames++;
                EVENT_RECORD* record = _recordBuffer.Fill(frame.Record!, out Etl.EtlRecordExtras extras);
                try
                {
                    _deserializer.Deserialize(record, extras);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    throw new RawEtwProtocolException(RawEtwProtocolError.DecodeFailed,
                        $"Event frame {frame.Sequence} could not be decoded: {ex.Message}", ex);
                }

                return RawEtwFrameDisposition.Event;

            case RawEtwFrameType.Loss:
                LostEvents = unchecked(LostEvents + frame.Loss!.LostEvents);
                _options.OnLoss?.Invoke(frame.Loss);
                return RawEtwFrameDisposition.Loss;

            case RawEtwFrameType.End:
                Ended = true;
                return RawEtwFrameDisposition.End;

            default:
                return RawEtwFrameDisposition.Ignored;
        }
    }

    private bool IsKnownRetry(ulong sequence, ReadOnlySpan<byte> hash)
    {
        if (NextSequence - sequence > DuplicateWindow)
        {
            return false;
        }

        int slot = (int)(sequence % DuplicateWindow);
        return _hashedSequence[slot] == sequence && _hashes.AsSpan(slot * HashLength, HashLength).SequenceEqual(hash);
    }

    /// <summary>
    ///     Counts a new (provider, id, version, opcode) key against <see cref="RawEtwRelayLimits.MaxCachedEventKeys" />.
    ///     The deserializer caches a parser per key for the life of the session; this stops that cache growing without a bound.
    /// </summary>
    private void ReserveEventKey(RawEtwRecord record)
    {
        EventKey key = new(record.ProviderId, record.Id, record.Version, record.Opcode);
        if (_eventKeys.Contains(key))
        {
            return;
        }

        if (_eventKeys.Count >= _limits.MaxCachedEventKeys)
        {
            throw new RawEtwProtocolException(RawEtwProtocolError.InvalidLength,
                $"The session already caches {_limits.MaxCachedEventKeys} event keys.");
        }

        _eventKeys.Add(key);
    }

    private readonly record struct EventKey(Guid Provider, ushort Id, byte Version, byte Opcode);
}
