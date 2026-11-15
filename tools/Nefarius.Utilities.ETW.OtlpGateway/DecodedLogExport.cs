using System.Threading.Channels;

using Google.Protobuf;

using Microsoft.Extensions.Options;

using OpenTelemetry.Proto.Collector.Logs.V1;

namespace Nefarius.Utilities.ETW.OtlpGateway;

/// <summary>
///     Delivers decoded log records to their destination.
/// </summary>
public interface IDecodedLogSink
{
    /// <summary>
    ///     Sends one batch. Throw <see cref="SinkRetryableException" /> for failures worth retrying; any other
    ///     exception drops the batch.
    /// </summary>
    Task SendAsync(ExportLogsServiceRequest batch, CancellationToken cancellationToken);
}

/// <summary>
///     A delivery failure that may succeed when repeated.
/// </summary>
public sealed class SinkRetryableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
///     Posts batches to a downstream OTLP/HTTP receiver as <c>application/x-protobuf</c>.
/// </summary>
public sealed class OtlpHttpSink(HttpClient client, IOptions<GatewayOptions> options) : IDecodedLogSink
{
    public async Task SendAsync(ExportLogsServiceRequest batch, CancellationToken cancellationToken)
    {
        GatewayOptions.ExportOptions export = options.Value.Export;
        Uri uri = new(new Uri(export.Endpoint.TrimEnd('/') + "/"), "v1/logs");

        using ByteArrayContent content = new(batch.ToByteArray());
        content.Headers.ContentType = new("application/x-protobuf");

        using HttpRequestMessage request = new(HttpMethod.Post, uri) { Content = content };
        foreach (KeyValuePair<string, string> header in export.Headers)
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
///     Discards batches. Used when no downstream endpoint is configured.
/// </summary>
public sealed class DiscardSink : IDecodedLogSink
{
    public Task SendAsync(ExportLogsServiceRequest batch, CancellationToken cancellationToken) => Task.CompletedTask;
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
///     Drains <see cref="DecodedLogQueue" /> into the configured <see cref="IDecodedLogSink" /> with bounded retries.
/// </summary>
public sealed class DecodedLogExporter(
    DecodedLogQueue queue,
    IDecodedLogSink sink,
    IOptions<GatewayOptions> options,
    GatewayMetrics metrics,
    ILogger<DecodedLogExporter> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (ExportLogsServiceRequest batch in queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            await DeliverAsync(batch, stoppingToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);

        // Give batches that are still queued one last attempt each, within the shutdown budget.
        while (queue.Reader.TryRead(out ExportLogsServiceRequest? batch) && !cancellationToken.IsCancellationRequested)
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

    private async Task DeliverAsync(ExportLogsServiceRequest batch, CancellationToken token)
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
