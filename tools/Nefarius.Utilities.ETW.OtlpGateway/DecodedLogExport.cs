using System.Threading.Channels;

using Google.Protobuf;

using Microsoft.Extensions.Options;

using Nefarius.Utilities.ETW.OpenTelemetry;

using OpenTelemetry.Proto.Collector.Logs.V1;

namespace Nefarius.Utilities.ETW.OtlpGateway;

/// <summary>
///     Delivers decoded log records to one destination.
/// </summary>
public interface IDecodedLogSink
{
    /// <summary>
    ///     Sends one batch. Throw <see cref="SinkRetryableException" /> for failures worth retrying; any other
    ///     exception drops the batch for this sink.
    /// </summary>
    Task SendAsync(ExportLogsServiceRequest batch, CancellationToken cancellationToken);
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
    Func<ExportLogsServiceRequest, IMessage?> convert) : IDecodedLogSink
{
    private readonly Uri _uri = new(new Uri(baseUrl.TrimEnd('/') + "/"), path);

    public async Task SendAsync(ExportLogsServiceRequest batch, CancellationToken cancellationToken)
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
            yield return new OtlpHttpSink(client, export.Endpoint, "v1/logs", export.Headers, batch => batch);
        }

        // Trace backends such as Jaeger cannot store logs; this renders each decoded event as a span instead.
        if (!string.IsNullOrWhiteSpace(export.TracesEndpoint))
        {
            yield return new OtlpHttpSink(client, export.TracesEndpoint, "v1/traces", export.Headers,
                batch => EtwSpanMapper.ToTraceRequest(batch) is { ResourceSpans.Count: > 0 } traces ? traces : null);
        }
    }
}

/// <summary>
///     A bounded buffer between request handling and delivery, so a slow or briefly unavailable downstream receiver
///     does not turn into lost frames: the sender is told to retry before any frame is consumed.
/// </summary>
public sealed class DecodedLogQueue
{
    private readonly Channel<ExportLogsServiceRequest> _channel;
    private readonly int _capacity;

    public DecodedLogQueue(IOptions<GatewayOptions> options)
    {
        _capacity = Math.Max(1, options.Value.Export.QueueCapacity);
        _channel = Channel.CreateBounded<ExportLogsServiceRequest>(new BoundedChannelOptions(_capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true
        });
    }

    public int Count => _channel.Reader.Count;

    /// <summary>True when at least one more batch fits.</summary>
    public bool HasCapacity => Count < _capacity;

    public ChannelReader<ExportLogsServiceRequest> Reader => _channel.Reader;

    public ValueTask EnqueueAsync(ExportLogsServiceRequest batch, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(batch, cancellationToken);
}

/// <summary>
///     Drains <see cref="DecodedLogQueue" /> into every configured <see cref="IDecodedLogSink" />, each with its own
///     bounded retries, so one failing destination neither repeats nor blocks delivery to the others.
/// </summary>
public sealed class DecodedLogExporter(
    DecodedLogQueue queue,
    IEnumerable<IDecodedLogSink> sinks,
    IOptions<GatewayOptions> options,
    GatewayMetrics metrics,
    ILogger<DecodedLogExporter> logger) : BackgroundService
{
    private readonly IDecodedLogSink[] _sinks = sinks.ToArray();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_sinks.Length == 0)
        {
            logger.LogWarning(
                "No Gateway:Export:Endpoint or Gateway:Export:TracesEndpoint is configured; decoded records are discarded.");
        }

        await foreach (ExportLogsServiceRequest batch in queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            await Task.WhenAll(_sinks.Select(sink => DeliverAsync(sink, batch, stoppingToken))).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);

        // Give batches that are still queued one last attempt each, within the shutdown budget.
        while (queue.Reader.TryRead(out ExportLogsServiceRequest? batch) && !cancellationToken.IsCancellationRequested)
        {
            foreach (IDecodedLogSink sink in _sinks)
            {
                try
                {
                    await sink.SendAsync(batch, cancellationToken).ConfigureAwait(false);
                    metrics.BatchesExported.Add(1);
                }
                catch (Exception ex)
                {
                    metrics.BatchesDropped.Add(1);
                    logger.LogWarning(ex, "A decoded batch was dropped during shutdown.");
                }
            }
        }
    }

    private async Task DeliverAsync(IDecodedLogSink sink, ExportLogsServiceRequest batch, CancellationToken token)
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
