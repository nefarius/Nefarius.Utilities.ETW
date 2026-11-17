using System.CommandLine;
using System.Reflection;

namespace Nefarius.Utilities.ETW.Relay.CLI;

/// <summary>
///     Builds the <c>etwrelay</c> root command.
/// </summary>
internal static class RelayCommand
{
    /// <summary>
    ///     How long an interrupted relay keeps delivering after the first Ctrl+C before it gives up on its input.
    /// </summary>
    private static readonly TimeSpan InterruptGrace = TimeSpan.FromSeconds(10);

    internal static RootCommand Build(Func<Stream> openStdin, TextWriter log,
        HttpMessageHandler? handler = null, Func<TimeSpan, CancellationToken, Task>? delay = null,
        CancellationToken? abort = null)
    {
        Option<string> endpointOpt = new("--endpoint")
        {
            Description = "Base URL of the OTLP/HTTP gateway; '/v1/logs' is appended unless already present.",
            DefaultValueFactory = _ => "http://localhost:4318"
        };

        Option<string> inputFormatOpt = new("--input-format")
        {
            Description = "How frames arrive: 'binary' (default, concatenated frames, for 'etwutils realtime --format relay') " +
                          "or 'base64-ndjson' ({\"frame\":\"<base64>\"} lines, for 'etwutils realtime --format relay-ndjson'). " +
                          "The format is never guessed.",
            DefaultValueFactory = _ => "binary"
        };

        Option<string?> inputOpt = new("--input")
        {
            Description = "Read frames from this file instead of standard input."
        };

        Option<string[]> headerOpt = new("--header")
        {
            Description = "HTTP header as name=value. Repeatable. Credential-like headers require an https endpoint.",
            AllowMultipleArgumentsPerToken = false
        };

        Option<string[]> resourceOpt = new("--resource")
        {
            Description = "OTLP resource attribute as name=value, e.g. service.name=my-host. Repeatable.",
            AllowMultipleArgumentsPerToken = false
        };

        Option<bool> gzipOpt = new("--gzip") { Description = "Compress request bodies with gzip." };

        Option<int> batchFramesOpt = new("--batch-frames")
        {
            Description = $"Most frames per request (1-{RelaySettings.MaxFramesPerBatch}).",
            DefaultValueFactory = _ => RelaySettings.MaxFramesPerBatch
        };

        Option<int> batchBytesOpt = new("--batch-bytes")
        {
            Description = "Send a request once its frames add up to this many bytes.",
            DefaultValueFactory = _ => 1024 * 1024
        };

        Option<int> flushMsOpt = new("--flush-ms")
        {
            Description = "Longest a frame waits for its batch to fill, in milliseconds.",
            DefaultValueFactory = _ => 250
        };

        Option<int> heartbeatOpt = new("--heartbeat-seconds")
        {
            Description =
                "Resend the latest acknowledged batch of an open session after this many idle seconds, so the gateway " +
                "does not expire it. 0 disables the heartbeat.",
            DefaultValueFactory = _ => 300
        };

        Option<int> queueOpt = new("--queue-frames")
        {
            Description = "Frames buffered between reading and sending. When full, the producer is slowed down.",
            DefaultValueFactory = _ => 1024
        };

        Option<int> timeoutOpt = new("--timeout-seconds")
        {
            Description = "Timeout of a single request.",
            DefaultValueFactory = _ => 30
        };

        Option<int> retriesOpt = new("--max-retries")
        {
            Description = "Retries of a request after connection failures, timeouts, 408, 429 and 5xx, before giving up.",
            DefaultValueFactory = _ => 8
        };

        Option<int> maxFrameOpt = new("--max-frame-bytes")
        {
            Description = "Largest frame accepted from the input.",
            DefaultValueFactory = _ => 1024 * 1024
        };

        RootCommand root = new(
            "Reads raw ETW relay frames (from 'etwutils realtime --format relay') and delivers them to an OpenTelemetry " +
            "decoding gateway as OTLP/HTTP logs.\n" +
            "Exit codes: 0 delivered, 1 delivery failed, 2 invalid usage or input.")
        {
            endpointOpt, inputFormatOpt, inputOpt, headerOpt, resourceOpt, gzipOpt, batchFramesOpt, batchBytesOpt,
            flushMsOpt, heartbeatOpt, queueOpt, timeoutOpt, retriesOpt, maxFrameOpt
        };

        root.SetAction(async (ParseResult result, CancellationToken libraryToken) =>
        {
            RelaySettings settings;
            try
            {
                settings = CreateSettings(result, endpointOpt, inputFormatOpt, headerOpt, resourceOpt, gzipOpt,
                    batchFramesOpt, batchBytesOpt, flushMsOpt, heartbeatOpt, queueOpt, timeoutOpt, retriesOpt,
                    maxFrameOpt);
            }
            catch (UsageException ex)
            {
                log.WriteLine($"[!] {ex.Message}");
                return 2;
            }

            string? inputPath = result.GetValue(inputOpt);
            using CancellationTokenSource interrupt = new();

            int interrupts = 0;
            ConsoleCancelEventHandler onCancel = (_, e) =>
            {
                e.Cancel = true;
                // First Ctrl+C: let the producer finish and close the stream. Second one, or the grace expiring: stop.
                if (Interlocked.Increment(ref interrupts) == 1)
                {
                    log.WriteLine("[*] Interrupt received; delivering what is queued (Ctrl+C again to stop now).");
                    interrupt.CancelAfter(InterruptGrace);
                }
                else
                {
                    interrupt.Cancel();
                }
            };
            Console.CancelKeyPress += onCancel;

            try
            {
                using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
                    interrupt.Token, abort ?? CancellationToken.None);

                await using Stream input = inputPath is null || inputPath == "-"
                    ? openStdin()
                    : new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);

                using OtlpSender sender = new(settings, log, handler, delay);
                RelayRunner runner = new(settings, sender, log);

                int code = await runner.RunAsync(input, linked.Token);
                log.WriteLine($"[*] Delivered {runner.FramesSent} frame(s) in {runner.Batches} request(s).");
                return code;
            }
            catch (InputException ex)
            {
                log.WriteLine($"[!] {ex.Message}");
                return 2;
            }
            catch (UsageException ex)
            {
                log.WriteLine($"[!] {ex.Message}");
                return 2;
            }
            catch (DeliveryException ex)
            {
                log.WriteLine($"[!] {ex.Message}");
                return 1;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log.WriteLine($"[!] Cannot read the input: {ex.Message}");
                return 2;
            }
            finally
            {
                Console.CancelKeyPress -= onCancel;
            }
        });

        return root;
    }

    private static RelaySettings CreateSettings(ParseResult result, Option<string> endpoint, Option<string> format,
        Option<string[]> header, Option<string[]> resource, Option<bool> gzip, Option<int> batchFrames,
        Option<int> batchBytes, Option<int> flushMs, Option<int> heartbeatSeconds, Option<int> queue,
        Option<int> timeout, Option<int> retries, Option<int> maxFrame)
    {
        InputFormat inputFormat = result.GetValue(format)!.ToLowerInvariant() switch
        {
            "binary" => InputFormat.Binary,
            "base64-ndjson" => InputFormat.Base64Ndjson,
            var other => throw new UsageException(
                $"Unknown --input-format '{other}'. Expected 'binary' or 'base64-ndjson'.")
        };

        List<KeyValuePair<string, string>> headers = RelaySettings.ParsePairs(result.GetValue(header) ?? [], "--header");
        List<KeyValuePair<string, string>> resources =
            RelaySettings.ParsePairs(result.GetValue(resource) ?? [], "--resource");

        if (resources.Any(r => r.Key == Nefarius.Utilities.ETW.OpenTelemetry.EtwOtlpAttributes.RelaySessionId))
        {
            throw new UsageException(
                $"--resource '{Nefarius.Utilities.ETW.OpenTelemetry.EtwOtlpAttributes.RelaySessionId}' is set from the " +
                "relay session and cannot be supplied.");
        }

        int frames = Range(result.GetValue(batchFrames), 1, RelaySettings.MaxFramesPerBatch, "--batch-frames");
        int bytes = Range(result.GetValue(batchBytes), 1, 16 * 1024 * 1024, "--batch-bytes");
        int flush = Range(result.GetValue(flushMs), 1, 60_000, "--flush-ms");
        int heartbeat = Range(result.GetValue(heartbeatSeconds), 0, 86_400, "--heartbeat-seconds");
        int queueFrames = Range(result.GetValue(queue), 1, 1_000_000, "--queue-frames");
        int timeoutSeconds = Range(result.GetValue(timeout), 1, 3600, "--timeout-seconds");
        int maxRetries = Range(result.GetValue(retries), 0, 1000, "--max-retries");
        int frameBytes = Range(result.GetValue(maxFrame), RawEtwFrameCodec.PrefixLength + 1,
            RawEtwRelayLimits.AbsoluteMaxFrameLength, "--max-frame-bytes");

        return new RelaySettings
        {
            Endpoint = RelaySettings.BuildEndpoint(result.GetValue(endpoint)!, headers),
            InputFormat = inputFormat,
            Headers = headers,
            Resources = resources,
            Gzip = result.GetValue(gzip),
            BatchFrames = frames,
            BatchBytes = bytes,
            FlushInterval = TimeSpan.FromMilliseconds(flush),
            HeartbeatInterval = TimeSpan.FromSeconds(heartbeat),
            QueueFrames = queueFrames,
            RequestTimeout = TimeSpan.FromSeconds(timeoutSeconds),
            MaxRetries = maxRetries,
            MaxFrameBytes = frameBytes
        };
    }

    private static int Range(int value, int min, int max, string name) =>
        value < min || value > max
            ? throw new UsageException($"{name} must be between {min} and {max}.")
            : value;
}
