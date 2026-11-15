using System.Security.Cryptography;

using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Trace.V1;

namespace Nefarius.Utilities.ETW.OpenTelemetry;

/// <summary>
///     Re-expresses decoded ETW log records as spans, for trace backends such as Jaeger that cannot store logs.
/// </summary>
/// <remarks>
///     <para>
///         Each event becomes one instantaneous span. The trace identifier is the relay session identifier, so all
///         events of a capture session appear in one trace; span identifiers are derived from the session and the
///         frame sequence number, so re-exporting the same event produces the same span.
///     </para>
///     <para>
///         The mapping is a visualization aid, not a replacement for logs: spans have no parent and zero duration.
///     </para>
/// </remarks>
public static class EtwSpanMapper
{
    /// <summary>
    ///     Converts decoded log records to spans. Records of resources without a relay session identifier are skipped.
    /// </summary>
    public static ExportTraceServiceRequest ToTraceRequest(ExportLogsServiceRequest decoded)
    {
        ArgumentNullException.ThrowIfNull(decoded);

        ExportTraceServiceRequest request = new();
        foreach (ResourceLogs resourceLogs in decoded.ResourceLogs)
        {
            if (!RelayLogRecords.TryGetSessionId(resourceLogs.Resource, out Guid session))
            {
                continue;
            }

            byte[] traceId = session.ToByteArray(true);
            ResourceSpans resourceSpans = new() { Resource = resourceLogs.Resource?.Clone() };
            ScopeSpans scope = new()
            {
                Scope = new InstrumentationScope { Name = "Nefarius.Utilities.ETW" }
            };

            ulong fallback = 0;
            foreach (LogRecord record in resourceLogs.ScopeLogs.SelectMany(s => s.LogRecords))
            {
                scope.Spans.Add(ToSpan(record, traceId, fallback++));
            }

            if (scope.Spans.Count > 0)
            {
                resourceSpans.ScopeSpans.Add(scope);
                request.ResourceSpans.Add(resourceSpans);
            }
        }

        return request;
    }

    private static Span ToSpan(LogRecord record, byte[] traceId, ulong fallback)
    {
        ulong sequence = fallback;
        foreach (KeyValue attribute in record.Attributes)
        {
            if (attribute.Key == EtwOtlpAttributes.RelaySequence)
            {
                sequence = unchecked((ulong)attribute.Value.IntValue);
            }
        }

        ulong time = record.TimeUnixNano != 0 ? record.TimeUnixNano : record.ObservedTimeUnixNano;
        Span span = new()
        {
            TraceId = Google.Protobuf.ByteString.CopyFrom(traceId),
            SpanId = Google.Protobuf.ByteString.CopyFrom(SpanId(traceId, sequence)),
            Name = string.IsNullOrEmpty(record.EventName) ? "etw.event" : record.EventName,
            Kind = Span.Types.SpanKind.Internal,
            StartTimeUnixNano = time,
            EndTimeUnixNano = time
        };

        span.Attributes.AddRange(record.Attributes);
        span.Attributes.Add(OtlpValues.String("etw.severity", record.SeverityText));

        switch (record.Body?.ValueCase)
        {
            case AnyValue.ValueOneofCase.StringValue:
                span.Attributes.Add(OtlpValues.String("etw.message", record.Body.StringValue));
                break;
            case AnyValue.ValueOneofCase.KvlistValue:
                // Event properties become attributes, so they can be searched in the trace UI.
                foreach (KeyValue member in record.Body.KvlistValue.Values)
                {
                    span.Attributes.Add(new KeyValue { Key = "etw.property." + member.Key, Value = member.Value });
                }

                break;
        }

        if (record.SeverityNumber >= SeverityNumber.Error)
        {
            span.Status = new Status { Code = Status.Types.StatusCode.Error };
        }

        return span;
    }

    private static byte[] SpanId(byte[] traceId, ulong sequence)
    {
        Span<byte> input = stackalloc byte[24];
        traceId.CopyTo(input);
        BitConverter.TryWriteBytes(input[16..], sequence);
        return SHA256.HashData(input)[..8];
    }
}
