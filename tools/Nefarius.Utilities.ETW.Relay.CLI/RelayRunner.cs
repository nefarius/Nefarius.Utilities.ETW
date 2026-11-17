using System.Diagnostics;
using System.Threading.Channels;

using Nefarius.Utilities.ETW.OpenTelemetry;

namespace Nefarius.Utilities.ETW.Relay.CLI;

/// <summary>
///     Moves frames from the input stream to the gateway: one reader, a bounded queue, one ordered sender.
/// </summary>
internal sealed class RelayRunner(RelaySettings settings, OtlpSender sender, TextWriter log, TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    private readonly RawEtwRelayLimits _limits = new() { MaxFrameLength = settings.MaxFrameBytes };

    /// <summary>Frames sent so far.</summary>
    public long FramesSent { get; private set; }

    /// <summary>Requests sent so far, including idle heartbeats.</summary>
    public long Batches { get; private set; }

    /// <summary>The latest acknowledged request of the session that is still open, resent verbatim while it is idle.</summary>
    private byte[]? _heartbeat;

    private Guid _heartbeatSession;
    private long _heartbeatDue;

    /// <summary>
    ///     Runs until the input ends.
    /// </summary>
    /// <param name="input">The frame stream.</param>
    /// <param name="abort">
    ///     Stops reading and gives the sender a short grace period for what is already queued. Not cancelling it lets
    ///     the producer finish and close the stream, which is what a clean shutdown looks like.
    /// </param>
    /// <returns>0 on success.</returns>
    /// <exception cref="InputException">The input is not a valid relay stream.</exception>
    /// <exception cref="DeliveryException">Delivery failed for good.</exception>
    public async Task<int> RunAsync(Stream input, CancellationToken abort)
    {
        Channel<Item> channel = Channel.CreateBounded<Item>(new BoundedChannelOptions(settings.QueueFrames)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });

        // Stops whichever side is still running once the other has failed.
        using CancellationTokenSource fatal = CancellationTokenSource.CreateLinkedTokenSource(abort);
        using CancellationTokenSource senderStop = new();
        await using CancellationTokenRegistration graceRegistration = abort.Register(() =>
            senderStop.CancelAfter(TimeSpan.FromSeconds(5)));

        Exception? readError = null;
        Task reader = Task.Run(async () =>
        {
            try
            {
                await ReadAsync(input, channel.Writer, fatal.Token).ConfigureAwait(false);
                channel.Writer.TryComplete();
            }
            catch (OperationCanceledException) when (abort.IsCancellationRequested)
            {
                channel.Writer.TryComplete();
            }
            catch (Exception ex)
            {
                // Frames read before the failure are valid; let the sender deliver them, then report the failure.
                readError = ex;
                channel.Writer.TryComplete();
            }
        });

        Task send = Task.Run(async () =>
        {
            try
            {
                await SendAsync(channel.Reader, senderStop.Token).ConfigureAwait(false);
            }
            catch
            {
                await fatal.CancelAsync().ConfigureAwait(false);
                throw;
            }
        });

        try
        {
            await send.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (abort.IsCancellationRequested)
        {
            // The grace period ended before the queued frames were acknowledged.
            log.WriteLine("[*] Interrupted before everything was delivered.");
            throw new DeliveryException("Interrupted before everything was delivered.");
        }

        // A blocked console read cannot always be cancelled, and the sender has finished anyway.
        await Task.WhenAny(reader, Task.Delay(500)).ConfigureAwait(false);
        if (reader.IsCompleted && readError is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(readError).Throw();
        }

        return 0;
    }

    private async Task ReadAsync(Stream input, ChannelWriter<Item> writer, CancellationToken token)
    {
        Guid session = Guid.Empty;
        bool open = false;
        bool ended = false;
        long frames = 0;

        try
        {
            await foreach (byte[] bytes in FrameInput.ReadAsync(input, settings.InputFormat, settings.MaxFrameBytes,
                               token).ConfigureAwait(false))
            {
                RawEtwFrame frame = RawEtwFrameCodec.Decode(bytes, _limits);
                frames++;

                if (frame.Type == RawEtwFrameType.Hello)
                {
                    session = frame.Hello!.SessionId;
                    open = true;
                    ended = false;
                }
                else if (!open)
                {
                    throw new InputException(ended
                        ? $"Frame {frames} follows the end of its session; a new session must start with a hello frame."
                        : $"Frame {frames} arrives before any hello frame; a session must start with one.");
                }

                await writer.WriteAsync(new Item(session, bytes, frame.Type == RawEtwFrameType.End), token)
                    .ConfigureAwait(false);

                if (frame.Type == RawEtwFrameType.End)
                {
                    open = false;
                    ended = true;
                }
            }
        }
        catch (RawEtwProtocolException ex)
        {
            string hint = frames == 0 && ex.Error == RawEtwProtocolError.BadMagic
                ? " If this was piped through PowerShell 7.3 or older, or Windows PowerShell, the bytes were re-encoded as " +
                  "text: use PowerShell 7.4+ for native-to-native pipes, or 'etwutils --format relay-ndjson' with " +
                  "'etwrelay --input-format base64-ndjson'."
                : string.Empty;
            throw new InputException($"Invalid relay input after {frames} frame(s): {ex.Message}{hint}", ex);
        }

        if (open)
        {
            log.WriteLine("[*] Input ended without an end frame; the producer probably stopped abruptly.");
        }
    }

    private async Task SendAsync(ChannelReader<Item> reader, CancellationToken token)
    {
        List<byte[]> batch = [];
        Guid batchSession = Guid.Empty;
        long batchBytes = 0;
        long deadline = 0;
        Item? carry = null;

        while (true)
        {
            Item item;
            if (carry is { } held)
            {
                item = held;
                carry = null;
            }
            else
            {
                bool more;
                if (batch.Count == 0)
                {
                    more = await WaitForFrameAsync(reader, token).ConfigureAwait(false);
                }
                else
                {
                    TimeSpan remaining = Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp(), deadline);
                    using CancellationTokenSource wait = CancellationTokenSource.CreateLinkedTokenSource(token);
                    wait.CancelAfter(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
                    try
                    {
                        more = await reader.WaitToReadAsync(wait.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested)
                    {
                        await FlushAsync(batchSession, batch, armHeartbeat: true, token).ConfigureAwait(false);
                        batchBytes = 0;
                        continue;
                    }
                }

                if (!more)
                {
                    break;
                }

                if (!reader.TryRead(out item))
                {
                    continue;
                }
            }

            if (batch.Count > 0 && item.Session != batchSession)
            {
                // Never mix sessions in one request. The new session has not been acknowledged yet, so the previous
                // session is no longer kept alive either.
                await FlushAsync(batchSession, batch, armHeartbeat: false, token).ConfigureAwait(false);
                batchBytes = 0;
                carry = item;
                continue;
            }

            if (batch.Count == 0)
            {
                // A frame from another session ends the previous heartbeat immediately, before this batch is acknowledged.
                if (_heartbeat is not null && item.Session != _heartbeatSession)
                {
                    _heartbeat = null;
                }

                batchSession = item.Session;
                deadline = Stopwatch.GetTimestamp() +
                           (long)(settings.FlushInterval.TotalSeconds * Stopwatch.Frequency);
            }

            batch.Add(item.Frame);
            batchBytes += item.Frame.Length;

            if (item.IsEnd || batch.Count >= settings.BatchFrames || batchBytes >= settings.BatchBytes)
            {
                await FlushAsync(batchSession, batch, armHeartbeat: !item.IsEnd, token).ConfigureAwait(false);
                batchBytes = 0;
            }
        }

        if (batch.Count > 0)
        {
            // Input has finished, so there is no open session left to keep alive.
            await FlushAsync(batchSession, batch, armHeartbeat: false, token).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Waits for the next frame. Returns false when the input is finished. An idle open session is refreshed by
    ///     replaying its latest acknowledged frames. A leading hello is left out of that replay: it would open a new
    ///     session after a gateway restart and the following events would be exported again.
    /// </summary>
    private async Task<bool> WaitForFrameAsync(ChannelReader<Item> reader, CancellationToken token)
    {
        while (true)
        {
            if (_heartbeat is null || settings.HeartbeatInterval <= TimeSpan.Zero)
            {
                return await reader.WaitToReadAsync(token).ConfigureAwait(false);
            }

            TimeSpan remaining = _clock.GetElapsedTime(_clock.GetTimestamp(), _heartbeatDue);
            if (remaining < TimeSpan.Zero)
            {
                remaining = TimeSpan.Zero;
            }

            using CancellationTokenSource idleCancel = CancellationTokenSource.CreateLinkedTokenSource(token);
            using CancellationTokenSource readCancel = CancellationTokenSource.CreateLinkedTokenSource(token);
            Task<bool> incoming = reader.WaitToReadAsync(readCancel.Token).AsTask();
            Task idle = remaining <= TimeSpan.Zero
                ? Task.CompletedTask
                : Task.Delay(remaining, _clock, idleCancel.Token);
            await Task.WhenAny(incoming, idle).ConfigureAwait(false);
            if (incoming.IsCompleted)
            {
                await idleCancel.CancelAsync().ConfigureAwait(false);
                await ObserveAsync(idle).ConfigureAwait(false);
                return await incoming.ConfigureAwait(false);
            }

            await readCancel.CancelAsync().ConfigureAwait(false);
            await ObserveAsync(incoming).ConfigureAwait(false);
            await idle.ConfigureAwait(false);
            await sender.SendAsync(_heartbeat, token).ConfigureAwait(false);
            Batches++;
            _heartbeatDue = NextHeartbeatDue();
        }
    }

    private async Task FlushAsync(Guid session, List<byte[]> batch, bool armHeartbeat, CancellationToken token)
    {
        if (batch.Count == 0)
        {
            return;
        }

        byte[] body = sender.Serialize(RelayLogRecords.CreateRequest(session,
            batch.Select(f => (ReadOnlyMemory<byte>)f), settings.Resources));

        await sender.SendAsync(body, token).ConfigureAwait(false);

        FramesSent += batch.Count;
        Batches++;
        if (armHeartbeat && settings.HeartbeatInterval > TimeSpan.Zero &&
            TryCreateHeartbeat(session, batch, body, out byte[]? heartbeat))
        {
            // A batch that does not start with hello is replayed from the bytes just sent, so a gzip stream stays
            // identical. A leading hello is omitted; the remaining frames are what a live session can accept as
            // duplicates, and what a restarted gateway rejects instead of exporting again.
            _heartbeat = heartbeat;
            _heartbeatSession = session;
            _heartbeatDue = NextHeartbeatDue();
        }
        else
        {
            _heartbeat = null;
        }

        batch.Clear();
    }

    private long NextHeartbeatDue() =>
        _clock.GetTimestamp() + (long)(settings.HeartbeatInterval.TotalSeconds * _clock.TimestampFrequency);

    private static async Task ObserveAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>
    ///     Builds the idle replay. Returns false when the only frame is a hello, which cannot refresh a live session
    ///     without also being able to create one.
    /// </summary>
    private bool TryCreateHeartbeat(Guid session, List<byte[]> batch, byte[] sentBody, out byte[]? heartbeat)
    {
        if (RawEtwFrameCodec.Decode(batch[0], _limits).Type != RawEtwFrameType.Hello)
        {
            heartbeat = sentBody;
            return true;
        }

        if (batch.Count == 1)
        {
            heartbeat = null;
            return false;
        }

        heartbeat = sender.Serialize(RelayLogRecords.CreateRequest(session,
            batch.Skip(1).Select(static frame => (ReadOnlyMemory<byte>)frame), settings.Resources));
        return true;
    }

    private readonly record struct Item(Guid Session, byte[] Frame, bool IsEnd);
}
