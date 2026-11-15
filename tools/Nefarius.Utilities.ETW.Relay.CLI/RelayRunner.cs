using System.Diagnostics;
using System.Threading.Channels;

using Nefarius.Utilities.ETW.OpenTelemetry;

namespace Nefarius.Utilities.ETW.Relay.CLI;

/// <summary>
///     Moves frames from the input stream to the gateway: one reader, a bounded queue, one ordered sender.
/// </summary>
internal sealed class RelayRunner(RelaySettings settings, OtlpSender sender, TextWriter log)
{
    private readonly RawEtwRelayLimits _limits = new() { MaxFrameLength = settings.MaxFrameBytes };

    /// <summary>Frames sent so far.</summary>
    public long FramesSent { get; private set; }

    /// <summary>Requests sent so far.</summary>
    public long Batches { get; private set; }

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
                    more = await reader.WaitToReadAsync(token).ConfigureAwait(false);
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
                        await FlushAsync(batchSession, batch, token).ConfigureAwait(false);
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
                // Never mix sessions in one request.
                await FlushAsync(batchSession, batch, token).ConfigureAwait(false);
                batchBytes = 0;
                carry = item;
                continue;
            }

            if (batch.Count == 0)
            {
                batchSession = item.Session;
                deadline = Stopwatch.GetTimestamp() +
                           (long)(settings.FlushInterval.TotalSeconds * Stopwatch.Frequency);
            }

            batch.Add(item.Frame);
            batchBytes += item.Frame.Length;

            if (item.IsEnd || batch.Count >= settings.BatchFrames || batchBytes >= settings.BatchBytes)
            {
                await FlushAsync(batchSession, batch, token).ConfigureAwait(false);
                batchBytes = 0;
            }
        }

        if (batch.Count > 0)
        {
            await FlushAsync(batchSession, batch, token).ConfigureAwait(false);
        }
    }

    private async Task FlushAsync(Guid session, List<byte[]> batch, CancellationToken token)
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
        batch.Clear();
    }

    private readonly record struct Item(Guid Session, byte[] Frame, bool IsEnd);
}
