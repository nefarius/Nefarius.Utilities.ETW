using System.Threading.Channels;

using Google.Protobuf;

using Microsoft.Extensions.Options;

using Nefarius.Utilities.ETW.OpenTelemetry;

using OpenTelemetry.Proto.Collector.Logs.V1;

namespace Nefarius.Utilities.ETW.OtlpGateway;

/// <summary>
///     One decoded batch plus the tenant it was decoded for. The tenant is not part of the OTLP payload: the client
///     could claim any session id, so the gateway keeps the authenticated tenant beside the batch.
/// </summary>
public readonly record struct DecodedBatch(string Tenant, ExportLogsServiceRequest Request);

/// <summary>Delivers decoded log records to one destination.</summary>
public interface IDecodedLogSink
{
    /// <summary>
    ///     Sends one batch. Throw <see cref="SinkRetryableException" /> for failures worth retrying; any other
    ///     exception drops the batch for this sink.
    /// </summary>
    Task SendAsync(DecodedBatch batch, CancellationToken cancellationToken);
}

/// <summary>
///     A delivery failure that may succeed when repeated.
/// </summary>
public sealed class SinkRetryableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
///     Posts a payload derived from each batch to a downstream OTLP/HTTP receiver as <c>application/x-protobuf</c>.
/// </summary>
/// <param name="client">The HTTP client.</param>
/// <param name="baseUrl">Base URL of the receiver, e.g. <c>http://collector:4318</c>.</param>
/// <param name="path">Signal path, e.g. <c>v1/logs</c> or <c>v1/traces</c>.</param>
/// <param name="headers">Extra request headers.</param>
/// <param name="convert">Turns a decoded batch into the protobuf message to send, or <see langword="null" /> to skip.</param>
public sealed class OtlpHttpSink(
    HttpClient client,
    string baseUrl,
    string path,
    IReadOnlyDictionary<string, string> headers,
    Func<DecodedBatch, IMessage?> convert) : IDecodedLogSink
{
    private readonly Uri _uri = EndpointAddress(baseUrl, path, headers);

    /// <summary>
    ///     Checks the export URL. An <c>http</c> URL is accepted only when the headers carry no credentials, which is
    ///     how the plaintext development stack (no auth headers) stays usable. Credentialed exports require <c>https</c>.
    /// </summary>
    internal static Uri EndpointAddress(string baseUrl, string path, IReadOnlyDictionary<string, string> headers)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? baseUri) || baseUri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException($"Export endpoint '{baseUrl}' is not an absolute http(s) URL.");
        }

        if (baseUri.Scheme == "http" && headers.Keys.Any(IsCredentialHeader))
        {
            throw new InvalidOperationException(
                "Export credentials cannot be sent to an http:// endpoint. Use https://.");
        }

        return new Uri(new Uri(baseUrl.TrimEnd('/') + "/"), path);
    }

    private static bool IsCredentialHeader(string name) =>
        name.Equals("Authorization", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("api-key", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("apikey", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("token", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("password", StringComparison.OrdinalIgnoreCase);

    public async Task SendAsync(DecodedBatch batch, CancellationToken cancellationToken)
    {
        IMessage? message = convert(batch);
        if (message is null)
        {
            return;
        }

        using ByteArrayContent content = new(message.ToByteArray());
        content.Headers.ContentType = new("application/x-protobuf");

        using HttpRequestMessage request = new(HttpMethod.Post, _uri) { Content = content };
        foreach (KeyValuePair<string, string> header in headers)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new SinkRetryableException("The downstream receiver is unreachable.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SinkRetryableException("The downstream receiver timed out.", ex);
        }

        using (response)
        {
            int status = (int)response.StatusCode;
            if (status is >= 200 and < 300)
            {
                return;
            }

            if (status is 408 or 429 or >= 500)
            {
                throw new SinkRetryableException($"The downstream receiver answered {status}.");
            }

            throw new InvalidOperationException($"The downstream receiver rejected the batch with {status}.");
        }
    }
}

/// <summary>
///     Creates the sinks configured in <see cref="GatewayOptions.ExportOptions" />.
/// </summary>
public static class ConfiguredSinks
{
    public const string HttpClientName = "etw-otlp-export";

    public static int Count(GatewayOptions.ExportOptions export) =>
        (string.IsNullOrWhiteSpace(export.Endpoint) ? 0 : 1) + (string.IsNullOrWhiteSpace(export.TracesEndpoint) ? 0 : 1);

    public static IEnumerable<IDecodedLogSink> Create(GatewayOptions.ExportOptions export, HttpClient client)
    {
        if (!string.IsNullOrWhiteSpace(export.Endpoint))
        {
            yield return new OtlpHttpSink(client, export.Endpoint, "v1/logs", export.Headers, batch => batch.Request);
        }

        // Trace backends such as Jaeger cannot store logs; this renders each decoded event as a span instead.
        if (!string.IsNullOrWhiteSpace(export.TracesEndpoint))
        {
            yield return new OtlpHttpSink(client, export.TracesEndpoint, "v1/traces", export.Headers,
                batch => EtwSpanMapper.ToTraceRequest(batch.Request, batch.Tenant) is { ResourceSpans.Count: > 0 } traces
                    ? traces
                    : null);
        }
    }
}

/// <summary>
///     The sinks built from one read of the export options. Created once so each destination keeps the endpoint it was
///     configured with.
/// </summary>
public sealed class ConfiguredExportSinks(IReadOnlyList<IDecodedLogSink> sinks)
{
    public IReadOnlyList<IDecodedLogSink> Sinks { get; } = sinks;
}

/// <summary>
///     A bounded buffer between request handling and delivery, so a slow or briefly unavailable downstream receiver
///     does not turn into lost frames: the sender is told to retry before any frame is consumed.
/// </summary>
public sealed class DecodedLogQueue
{
    private readonly Channel<DecodedBatch> _channel = Channel.CreateUnbounded<DecodedBatch>(
        new UnboundedChannelOptions { SingleReader = true });

    private readonly SemaphoreSlim _slots;

    public DecodedLogQueue(IOptions<GatewayOptions> options)
    {
        int capacity = Math.Max(1, options.Value.Export.QueueCapacity);
        _slots = new SemaphoreSlim(capacity, capacity);
    }

    public int Count => _channel.Reader.Count;

    public ChannelReader<DecodedBatch> Reader => _channel.Reader;

    /// <summary>
    ///     Takes one queue slot. The slot covers the request until the batch is dequeued, so two requests cannot pass
    ///     the capacity check against the same free slot.
    /// </summary>
    public bool TryReserve() => _slots.Wait(0);

    /// <summary>Gives back a slot that will not become a queued batch, or one whose batch was dequeued.</summary>
    public void Release() => _slots.Release();

    /// <summary>
    ///     Queues a batch whose slot was reserved. The write cannot be cancelled: the frames are already consumed, so
    ///     dropping the batch here would lose them. The reserved slot stays held until <see cref="Release" />.
    /// </summary>
    public void EnqueueReserved(DecodedBatch batch) => _channel.Writer.TryWrite(batch);
}

/// <summary>
///     Drains <see cref="DecodedLogQueue" /> into every configured <see cref="IDecodedLogSink" />, each with its own
///     bounded retries, so one failing destination neither repeats nor blocks delivery to the others.
/// </summary>
public sealed class DecodedLogExporter(
    DecodedLogQueue queue,
    ConfiguredExportSinks configured,
    IEnumerable<IDecodedLogSink> additionalSinks,
    IOptions<GatewayOptions> options,
    GatewayMetrics metrics,
    ILogger<DecodedLogExporter> logger) : BackgroundService
{
    private readonly IDecodedLogSink[] _sinks = [.. configured.Sinks, .. additionalSinks];
    private readonly object _interruptedGate = new();
    private readonly List<(IDecodedLogSink Sink, DecodedBatch Batch)> _interrupted = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_sinks.Length == 0)
        {
            logger.LogWarning(
                "No Gateway:Export:Endpoint or Gateway:Export:TracesEndpoint is configured; decoded records are discarded.");
        }

        await foreach (DecodedBatch batch in queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            queue.Release();
            await Task.WhenAll(_sinks.Select(sink => DeliverAsync(sink, batch, stoppingToken))).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);

        // A delivery cancelled by shutdown kept its batch. Send it, then whatever is still queued, until the
        // shutdown budget runs out. A batch already confirmed delivered is not in this list, so it is not sent twice.
        List<(IDecodedLogSink Sink, DecodedBatch Batch)> pending;
        lock (_interruptedGate)
        {
            pending = _interrupted.ToList();
            _interrupted.Clear();
        }

        foreach ((IDecodedLogSink sink, DecodedBatch batch) in pending)
        {
            if (!await DrainOneAsync(sink, batch, cancellationToken).ConfigureAwait(false))
            {
                return;
            }
        }

        while (queue.Reader.TryRead(out DecodedBatch batch))
        {
            queue.Release();
            foreach (IDecodedLogSink sink in _sinks)
            {
                if (!await DrainOneAsync(sink, batch, cancellationToken).ConfigureAwait(false))
                {
                    return;
                }
            }
        }
    }

    /// <summary>One last attempt. Returns false when the shutdown budget has expired.</summary>
    private async Task<bool> DrainOneAsync(IDecodedLogSink sink, DecodedBatch batch,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        try
        {
            await sink.SendAsync(batch, cancellationToken).ConfigureAwait(false);
            metrics.BatchesExported.Add(1);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            metrics.BatchesDropped.Add(1);
            logger.LogWarning(ex, "A decoded batch was dropped during shutdown.");
            return true;
        }
    }

    private async Task DeliverAsync(IDecodedLogSink sink, DecodedBatch batch, CancellationToken token)
    {
        GatewayOptions.ExportOptions export = options.Value.Export;
        TimeSpan delay = export.InitialRetryDelay;

        for (int attempt = 1; ; attempt++)
        {
            try
            {
                await sink.SendAsync(batch, token).ConfigureAwait(false);
                metrics.BatchesExported.Add(1);
                return;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // Confirmed deliveries return before this point, so retaining the batch does not send it twice.
                lock (_interruptedGate)
                {
                    _interrupted.Add((sink, batch));
                }

                return;
            }
            catch (SinkRetryableException ex) when (attempt < export.MaxAttempts)
            {
                logger.LogWarning("Export attempt {Attempt} failed: {Message}. Retrying in {Delay}.", attempt,
                    ex.Message, delay);
                await Task.Delay(delay, token).ConfigureAwait(false);
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, export.MaxRetryDelay.Ticks));
            }
            catch (Exception ex)
            {
                metrics.BatchesDropped.Add(1);
                logger.LogError(ex, "A decoded batch was dropped after {Attempts} attempt(s).", attempt);
                return;
            }
        }
    }
}
