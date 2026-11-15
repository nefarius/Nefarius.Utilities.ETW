using System.Runtime.CompilerServices;

using Nefarius.Utilities.ETW.Events;

namespace Nefarius.Utilities.ETW.Relay;

/// <summary>
///     Decodes a stream of raw ETW relay frames into structured <see cref="EtwEvent" /> objects, entirely on the
///     receiving side.
/// </summary>
/// <remarks>
///     <para>
///         The producer only copies <c>EVENT_RECORD</c>s; this class resolves everything that needs symbols or
///         formats (WPP PDB/TMF files via <see cref="EtwJsonConverterOptions.WppDecodingContext" />, custom manifests
///         via <see cref="EtwJsonConverterOptions.CustomProviderManifest" />), so none of it has to exist on the
///         capturing machine.
///     </para>
///     <para>
///         An instance represents one capture session and keeps parser and manifest state between frames, so frames
///         must be passed in sequence order and an instance must not be shared between sessions. Members are
///         serialized internally, but ordering across threads is the caller's responsibility.
///     </para>
///     <para>
///         Relayed records are decoded without TDH. Manifest-based events whose manifest is not supplied through
///         <see cref="EtwJsonConverterOptions.CustomProviderManifest" /> or embedded in the stream (EventSource
///         manifests, TraceLogging metadata) are reported as unknown events.
///     </para>
/// </remarks>
public sealed class RawEtwRelayDecoder : IDisposable
{
    private readonly RelaySessionCore<EtwStructuredWriter> _core;
    private readonly object _gate = new();
    private readonly EtwStructuredWriter _writer = new();
    private readonly RawEtwRelayLimits _limits;

    /// <summary>
    ///     Creates a decoder for one capture session.
    /// </summary>
    /// <param name="decodingOptions">Decoding options, notably the server-side WPP context and manifests.</param>
    /// <param name="relayOptions">Frame limits and notifications.</param>
    public RawEtwRelayDecoder(Action<EtwJsonConverterOptions>? decodingOptions = null,
        RawEtwRelayOptions? relayOptions = null)
    {
        EtwJsonConverterOptions converterOptions = new();
        decodingOptions?.Invoke(converterOptions);
        _limits = relayOptions?.Limits ?? RawEtwRelayLimits.Default;
        _core = new RelaySessionCore<EtwStructuredWriter>(_writer, converterOptions, relayOptions);
    }

    /// <summary>The session identifier announced by the hello frame, or <see langword="null" /> before it.</summary>
    public Guid? SessionId
    {
        get
        {
            lock (_gate)
            {
                return _core.SessionId;
            }
        }
    }

    /// <summary>The sequence number the decoder expects next.</summary>
    public ulong NextSequence
    {
        get
        {
            lock (_gate)
            {
                return _core.NextSequence;
            }
        }
    }

    /// <summary>True once the producer's end frame was accepted.</summary>
    public bool Ended
    {
        get
        {
            lock (_gate)
            {
                return _core.Ended;
            }
        }
    }

    /// <summary>The number of events the producer reported as dropped.</summary>
    public ulong LostEvents
    {
        get
        {
            lock (_gate)
            {
                return _core.LostEvents;
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            _core.Dispose();
        }
    }

    /// <summary>
    ///     Processes one frame and reports the events it decoded.
    /// </summary>
    /// <param name="frame">The complete frame bytes, e.g. one OTLP log record body.</param>
    /// <param name="onEvent">Called for each decoded event, before this method returns.</param>
    /// <returns>What happened to the frame.</returns>
    /// <exception cref="RawEtwProtocolException">The frame violates the protocol, the limits or the sequencing.</exception>
    public RawEtwFrameDisposition Process(ReadOnlySpan<byte> frame, Action<EtwEvent> onEvent)
    {
        ArgumentNullException.ThrowIfNull(onEvent);

        lock (_gate)
        {
            _writer.Sink = onEvent;
            try
            {
                return _core.Process(frame);
            }
            finally
            {
                _writer.Sink = null;
            }
        }
    }

    /// <summary>
    ///     Processes one frame object by serializing it first. Prefer the span overload when the wire bytes exist.
    /// </summary>
    public RawEtwFrameDisposition Process(RawEtwFrame frame, Action<EtwEvent> onEvent)
    {
        return Process(RawEtwFrameCodec.Encode(frame), onEvent);
    }

    /// <summary>
    ///     Reads frames from <paramref name="input" /> and yields the decoded events as they arrive.
    /// </summary>
    /// <param name="input">A stream of back-to-back frames, e.g. an HTTP request body.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <remarks>
    ///     Enumeration ends after the end frame or when the stream ends; check <see cref="Ended" /> to tell the two
    ///     apart. The stream is read only as fast as the consumer enumerates, which applies backpressure to the sender.
    /// </remarks>
    /// <exception cref="RawEtwProtocolException">The input violates the protocol.</exception>
    public async IAsyncEnumerable<EtwEvent> DecodeAsync(Stream input,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        List<EtwEvent> batch = [];

        while (true)
        {
            byte[]? bytes = await RawEtwFrameCodec.ReadFrameBytesAsync(input, _limits, cancellationToken)
                .ConfigureAwait(false);
            if (bytes is null)
            {
                yield break;
            }

            batch.Clear();
            RawEtwFrameDisposition disposition = Process(bytes, batch.Add);
            foreach (EtwEvent decoded in batch)
            {
                yield return decoded;
            }

            if (disposition == RawEtwFrameDisposition.End)
            {
                yield break;
            }
        }
    }
}
