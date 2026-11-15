using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Channels;

using Windows.Win32.Foundation;

using Nefarius.Utilities.ETW.Etl;
using Nefarius.Utilities.ETW.Exceptions;
using Nefarius.Utilities.ETW.Relay;

namespace Nefarius.Utilities.ETW;

public static partial class EtwUtil
{
    /// <summary>
    ///     Captures a real-time ETW session and produces raw relay frames: a hello frame, one event frame per
    ///     captured record, loss frames for events ETW dropped, and an end frame. Nothing is decoded, so no symbols are
    ///     needed on the capturing machine.
    /// </summary>
    /// <param name="sessionName">The name of an existing real-time session to attach to.</param>
    /// <param name="options">Optional callback to configure the producer.</param>
    /// <param name="cancellationToken">
    ///     Stops the capture. Frames already captured are still delivered, followed by the end frame, and then the
    ///     enumeration throws <see cref="OperationCanceledException" />.
    /// </param>
    /// <returns>Complete encoded frames in sequence order.</returns>
    /// <remarks>
    ///     <para>
    ///         Records are copied inside the ETW callback, so no native pointer leaves the library. A bounded channel
    ///         couples the capture thread to the consumer and applies backpressure.
    ///     </para>
    ///     <para>
    ///         Disposing the enumerator early cannot deliver an end frame, but still closes the trace handle. The ETW
    ///         session itself belongs to its creator and is not stopped here.
    ///     </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="sessionName" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException"><paramref name="sessionName" /> is empty or whitespace.</exception>
    /// <exception cref="EtwOpenTraceException">The session could not be opened by the ETW API.</exception>
    [SupportedOSPlatform("windows")]
    public static IAsyncEnumerable<byte[]> EnumerateRealtimeRelayFramesAsync(
        string sessionName,
        Action<RawEtwRelayProducerOptions>? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionName);
        if (string.IsNullOrWhiteSpace(sessionName))
        {
            throw new ArgumentException("Session name must not be empty or whitespace.", nameof(sessionName));
        }

        PlatformGuard.ThrowIfNotWindows();

        RawEtwRelayProducerOptions opts = new();
        options?.Invoke(opts);
        opts.Validate();

        return StreamRelayFramesAsync(sessionName, opts, cancellationToken);
    }

    /// <summary>
    ///     Opens a real-time session and pumps <c>ProcessTrace</c> on the calling thread until the session ends or
    ///     <paramref name="cancellationToken" /> is cancelled. Closing the handle from the cancellation callback makes
    ///     <c>ProcessTrace</c> return promptly instead of waiting for the next flush tick.
    /// </summary>
    /// <returns>The status <c>ProcessTrace</c> returned.</returns>
    /// <exception cref="EtwOpenTraceException">The session could not be opened.</exception>
    private static int RunRealtimeTrace(
        string sessionName,
        bool preserveRawTimestamps,
        PEVENT_RECORD_CALLBACK recordCallback,
        PEVENT_TRACE_BUFFER_CALLBACKW bufferCallback,
        Action<string>? reportError,
        Action? onOpened,
        CancellationToken cancellationToken)
    {
        EVENT_TRACE_LOGFILEW[] sessions = new EVENT_TRACE_LOGFILEW[1];
        ulong[] handles = new ulong[1];

        // Prevents a double close between the cancellation callback and the finally block.
        int handleClosed = 0;

        try
        {
            sessions[0] = new EVENT_TRACE_LOGFILEW
            {
                LoggerName = sessionName,
                EventRecordCallback = recordCallback,
                BufferCallback = bufferCallback,
                LogFileMode = PInvoke.PROCESS_TRACE_MODE_EVENT_RECORD | PInvoke.PROCESS_TRACE_MODE_REAL_TIME
            };

            if (preserveRawTimestamps)
            {
                sessions[0].LogFileMode |= PInvoke.PROCESS_TRACE_MODE_RAW_TIMESTAMP;
            }

            handles[0] = Etw.OpenTrace(ref sessions[0]);
            // Capture immediately: any subsequent Win32 call overwrites the TLS slot.
            int openError = Marshal.GetLastWin32Error();

            if (handles[0] == unchecked((ulong)~0))
            {
                EtwOpenTraceException ex = new((WIN32_ERROR)openError, sessionName);
                reportError?.Invoke("ERROR: " + ex.Message);
                throw ex;
            }

            using CancellationTokenRegistration registration = cancellationToken.Register(() =>
            {
                if (Interlocked.Exchange(ref handleClosed, 1) == 0)
                {
                    Etw.CloseTrace(handles[0]);
                }
            });

            onOpened?.Invoke();

            int status = Etw.ProcessTrace(handles, 1, IntPtr.Zero, IntPtr.Zero);
            GC.KeepAlive(sessions);
            return status;
        }
        finally
        {
            if (Interlocked.Exchange(ref handleClosed, 1) == 0 && handles[0] != 0 &&
                handles[0] != unchecked((ulong)~0))
            {
                Etw.CloseTrace(handles[0]);
            }
        }
    }

    private static async IAsyncEnumerable<byte[]> StreamRelayFramesAsync(
        string sessionName,
        RawEtwRelayProducerOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Created inside the iterator so that nothing is opened unless the enumeration starts.
        Channel<byte[]> channel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(256)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });

        // "stop" ends the capture. "abandon" additionally releases a producer blocked on a full channel, which is only
        // wanted once the consumer is gone: a cancelled caller still drains what was captured.
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using CancellationTokenSource abandon = new();

        RealtimeRelayProducer producer = new(channel.Writer, options, stop.Token, abandon.Token);
        Thread worker = new(() => producer.Run(sessionName))
        {
            IsBackground = true,
            Name = $"EtwUtil.EnumerateRealtimeRelayFramesAsync worker [{sessionName}]"
        };
        worker.Start();

        try
        {
            await foreach (byte[] frame in channel.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
            {
                yield return frame;
            }

            // The end frame was delivered above; only now report that the caller asked to stop.
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            stop.Cancel();
            abandon.Cancel();

            // Handles must be closed before returning; a worker that outlives the wait keeps its tokens alive.
            if (!worker.Join(TimeSpan.FromSeconds(5)))
            {
                worker.Join();
            }
        }
    }

    /// <summary>
    ///     Copies records out of the ETW callbacks and encodes them. Runs entirely on the capture thread, so its
    ///     state needs no locking.
    /// </summary>
    internal sealed unsafe class RealtimeRelayProducer(
        ChannelWriter<byte[]> writer,
        RawEtwRelayProducerOptions options,
        CancellationToken stop,
        CancellationToken abandon)
    {
        internal static readonly int HeaderLostOffset =
            (int)Marshal.OffsetOf<EVENT_TRACE_LOGFILEW>(nameof(EVENT_TRACE_LOGFILEW.LogfileHeader)) +
            (int)Marshal.OffsetOf<TRACE_LOGFILE_HEADER>(nameof(TRACE_LOGFILE_HEADER.EventsLost));

        internal static readonly int LogfileLostOffset =
            (int)Marshal.OffsetOf<EVENT_TRACE_LOGFILEW>(nameof(EVENT_TRACE_LOGFILEW.EventsLost));

        private ulong _sequence;
        private uint _lastLost;

        public void Run(string sessionName)
        {
            Exception? error = null;
            bool opened = false;

            try
            {
                int status = RunRealtimeTrace(sessionName, false, OnRecord, OnBuffer, null, () =>
                {
                    opened = true;
                    Emit(new RawEtwFrame
                    {
                        Type = RawEtwFrameType.Hello,
                        Hello = new RawEtwHello
                        {
                            SessionId = options.SessionId,
                            MaxFrameLength = options.MaxFrameLength,
                            Producer = options.Producer
                        }
                    });
                }, stop);

                // ERROR_CANCELLED (1223) is what ProcessTrace reports after the handle was closed to stop it.
                if (status != 0 && status != 1223 && !stop.IsCancellationRequested)
                {
                    error = new Win32Exception(status, $"ProcessTrace failed with Win32 error 0x{status:X8}.");
                }
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                if (opened && error is null)
                {
                    Emit(new RawEtwFrame { Type = RawEtwFrameType.End });
                }

                writer.TryComplete(error);
            }
        }

        internal void OnRecord(EVENT_RECORD* record)
        {
            // Nothing may escape a native callback.
            try
            {
                RawEtwRecord copy = RelayRecordBuffer.ToRelayRecord(record, default);
                if (!Emit(new RawEtwFrame { Type = RawEtwFrameType.Event, Record = copy }))
                {
                    ReportLoss(1, 0);
                }
            }
            catch (Exception)
            {
                ReportLoss(1, 0);
            }
        }

        internal bool OnBuffer(IntPtr logfile)
        {
            try
            {
                uint lost = Math.Max(
                    unchecked((uint)Marshal.ReadInt32(logfile, HeaderLostOffset)),
                    unchecked((uint)Marshal.ReadInt32(logfile, LogfileLostOffset)));

                // The counters are cumulative; each increase is reported once, before the buffer's events.
                if (lost > _lastLost)
                {
                    uint delta = lost - _lastLost;
                    _lastLost = lost;
                    ReportLoss(delta, 2);
                }
            }
            catch (Exception)
            {
                // A failed loss read must not stop the capture.
            }

            return !stop.IsCancellationRequested;
        }

        private void ReportLoss(ulong lostEvents, uint reason) => Emit(new RawEtwFrame
        {
            Type = RawEtwFrameType.Loss,
            Loss = new RawEtwLoss { LostEvents = lostEvents, Reason = reason }
        });

        /// <summary>Encodes and queues a frame. Returns false when it could not be produced.</summary>
        private bool Emit(RawEtwFrame frame)
        {
            frame.Sequence = _sequence;

            byte[] bytes;
            try
            {
                bytes = RawEtwFrameCodec.Encode(frame, options.WithCrc);
            }
            catch (ArgumentException)
            {
                return false;
            }

            if (bytes.Length > options.MaxFrameLength)
            {
                return false;
            }

            if (!writer.TryWrite(bytes))
            {
                try
                {
                    writer.WriteAsync(bytes, abandon).AsTask().GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
                catch (ChannelClosedException)
                {
                    return false;
                }
            }

            _sequence++;
            return true;
        }
    }
}
