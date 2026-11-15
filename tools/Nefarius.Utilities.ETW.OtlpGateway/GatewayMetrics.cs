using System.Diagnostics.Metrics;

namespace Nefarius.Utilities.ETW.OtlpGateway;

/// <summary>
///     Operational metrics of the gateway, published through <see cref="System.Diagnostics.Metrics" /> so any
///     OpenTelemetry metrics pipeline can pick them up under the meter name <see cref="MeterName" />.
/// </summary>
public sealed class GatewayMetrics : IDisposable
{
    public const string MeterName = "Nefarius.Utilities.ETW.OtlpGateway";

    private readonly Meter _meter = new(MeterName);

    public GatewayMetrics()
    {
        FramesAccepted = _meter.CreateCounter<long>("etw.gateway.frames.accepted", "{frame}",
            "Relay frames applied or recognized as retries.");
        FramesRejected = _meter.CreateCounter<long>("etw.gateway.frames.rejected", "{frame}",
            "Relay frames rejected.");
        EventsDecoded = _meter.CreateCounter<long>("etw.gateway.events.decoded", "{event}",
            "Decoded events handed to the export queue.");
        EventsLost = _meter.CreateCounter<long>("etw.gateway.events.lost", "{event}",
            "Events the producers reported as dropped.");
        BatchesExported = _meter.CreateCounter<long>("etw.gateway.export.batches", "{batch}",
            "Decoded batches delivered downstream.");
        BatchesDropped = _meter.CreateCounter<long>("etw.gateway.export.dropped", "{batch}",
            "Decoded batches dropped after the retries were exhausted.");
        RequestsRefused = _meter.CreateCounter<long>("etw.gateway.requests.refused", "{request}",
            "Requests answered with a retryable error because the export queue was full.");
    }

    public Counter<long> FramesAccepted { get; }

    public Counter<long> FramesRejected { get; }

    public Counter<long> EventsDecoded { get; }

    public Counter<long> EventsLost { get; }

    public Counter<long> BatchesExported { get; }

    public Counter<long> BatchesDropped { get; }

    public Counter<long> RequestsRefused { get; }

    /// <summary>Registers observable gauges that read live values.</summary>
    public void Observe(Func<int> activeSessions, Func<int> queuedBatches)
    {
        _meter.CreateObservableGauge("etw.gateway.sessions.active", activeSessions, "{session}",
            "Relay sessions currently tracked.");
        _meter.CreateObservableGauge("etw.gateway.export.queued", queuedBatches, "{batch}",
            "Decoded batches waiting for export.");
    }

    public void Dispose() => _meter.Dispose();
}
