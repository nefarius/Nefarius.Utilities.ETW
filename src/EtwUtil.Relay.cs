using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

using Nefarius.Utilities.ETW.Relay;

namespace Nefarius.Utilities.ETW;

public static partial class EtwUtil
{
    /// <summary>
    ///     Decodes a stream of raw ETW relay frames as UTF-8 JSON objects, yielding each decoded event as it arrives.
    ///     Decoding, including WPP formatting, happens in this process; the sender only copies raw records.
    /// </summary>
    /// <param name="input">
    ///     A stream of back-to-back relay frames, e.g. an authenticated HTTP request body. The stream is read only as
    ///     fast as the result is enumerated.
    /// </param>
    /// <param name="options">
    ///     Decoding options. Set <see cref="EtwJsonConverterOptions.WppDecodingContext" /> to resolve WPP events from
    ///     PDB/TMF files that exist only on the receiving side.
    /// </param>
    /// <param name="relayOptions">Frame limits and notifications for hello and loss frames.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    ///     An <see cref="IAsyncEnumerable{T}" /> of raw UTF-8 JSON buffers, one per event, in the same shape as
    ///     <see cref="EnumerateEventsAsync" />. The enumeration ends after the end frame or when the stream ends.
    /// </returns>
    /// <remarks>
    ///     <para>
    ///         Each <see cref="ReadOnlyMemory{T}">ReadOnlyMemory&lt;byte&gt;</see> is backed by a pooled buffer that is
    ///         valid only until the next iteration step; copy it before advancing.
    ///     </para>
    ///     <para>
    ///         Authentication, authorization and transport security are the caller's concern: pass in a stream whose
    ///         origin has already been authorized.
    ///     </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="input" /> is <see langword="null" />.</exception>
    /// <exception cref="RawEtwProtocolException">The input violates the protocol or the limits.</exception>
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public static IAsyncEnumerable<ReadOnlyMemory<byte>> EnumerateRelayEventsAsync(
        Stream input,
        Action<EtwJsonConverterOptions>? options = null,
        RawEtwRelayOptions? relayOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        EtwJsonConverterOptions opts = new();
        options?.Invoke(opts);

        return StreamRelayEventsAsync(input, opts, relayOptions ?? new RawEtwRelayOptions(), cancellationToken);
    }

    private static async IAsyncEnumerable<ReadOnlyMemory<byte>> StreamRelayEventsAsync(
        Stream input,
        EtwJsonConverterOptions opts,
        RawEtwRelayOptions relayOptions,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Decoding runs on the consumer's thread, one frame at a time, so the channel only carries the few events
        // one frame can produce. Backpressure comes from not reading the next frame until they are consumed.
        Channel<PooledEventBuffer> channel = Channel.CreateUnbounded<PooledEventBuffer>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

        EtwJsonChannelWriter writer = new(channel.Writer, cancellationToken);
        using RelaySessionCore<EtwJsonChannelWriter> core = new(writer, opts, relayOptions);
        RawEtwRelayLimits limits = relayOptions.Limits ?? RawEtwRelayLimits.Default;

        PooledEventBuffer? previous = null;
        try
        {
            while (true)
            {
                byte[]? frame = await RawEtwFrameCodec.ReadFrameBytesAsync(input, limits, cancellationToken)
                    .ConfigureAwait(false);
                if (frame is null)
                {
                    yield break;
                }

                RawEtwFrameDisposition disposition = core.Process(frame);

                while (channel.Reader.TryRead(out PooledEventBuffer item))
                {
                    if (previous.HasValue)
                    {
                        ArrayPool<byte>.Shared.Return(previous.Value.Rented);
                    }

                    previous = item;
                    yield return item.Memory;
                }

                if (disposition == RawEtwFrameDisposition.End)
                {
                    yield break;
                }
            }
        }
        finally
        {
            if (previous.HasValue)
            {
                ArrayPool<byte>.Shared.Return(previous.Value.Rented);
            }

            // Events produced by a frame that failed halfway are never delivered; give their buffers back.
            while (channel.Reader.TryRead(out PooledEventBuffer leftover))
            {
                ArrayPool<byte>.Shared.Return(leftover.Rented);
            }
        }
    }
}
