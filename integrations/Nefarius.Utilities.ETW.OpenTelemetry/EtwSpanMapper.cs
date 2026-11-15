using System.Security.Cryptography;
using System.Text;

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
///         Each event becomes one instantaneous span. The trace identifier is derived from the tenant and the relay
///         session, so two tenants that reuse a session identifier do not share a trace. Span identifiers are derived
///         from that trace identifier and the frame sequence number, so re-exporting the same event produces the same
///         span inside its trace.
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
    /// <param name="decoded">The decoded log batch.</param>
    /// <param name="tenant">The authenticated tenant that produced the batch. Combined with the session id for the trace id.</param>
    public static ExportTraceServiceRequest ToTraceRequest(ExportLogsServiceRequest decoded, string tenant)
    {
        ArgumentNullException.ThrowIfNull(decoded);
        ArgumentNullException.ThrowIfNull(tenant);

        ExportTraceServiceRequest request = new();
        foreach (ResourceLogs resourceLogs in decoded.ResourceLogs)
        {
            if (!RelayLogRecords.TryGetSessionId(resourceLogs.Resource, out Guid session))
            {
                continue;
            }

            byte[] traceId = TraceId(tenant, session);
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

    /// <summary>16-byte trace id for one tenant and relay session. Stable for the same pair.</summary>
    internal static byte[] TraceId(string tenant, Guid session)
    {
        byte[] tenantBytes = Encoding.UTF8.GetBytes(tenant);
        byte[] sessionBytes = session.ToByteArray(true);
        byte[] input = new byte[tenantBytes.Length + sessionBytes.Length];
        tenantBytes.CopyTo(input, 0);
        sessionBytes.CopyTo(input, tenantBytes.Length);
        return SHA256.HashData(input)[..16];
    }

    /// <summary>8-byte span id from the trace id and the relay sequence. Stable inside one trace.</summary>
    internal static byte[] SpanId(byte[] traceId, ulong sequence)
    {
        Span<byte> input = stackalloc byte[24];
        traceId.CopyTo(input);
        BitConverter.TryWriteBytes(input[16..], sequence);
        return SHA256.HashData(input)[..8];
    }
}
