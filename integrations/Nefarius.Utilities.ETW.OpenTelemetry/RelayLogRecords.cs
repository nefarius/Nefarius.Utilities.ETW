using Google.Protobuf;

using Nefarius.Utilities.ETW.Relay;

using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Resource.V1;

namespace Nefarius.Utilities.ETW.OpenTelemetry;

/// <summary>
///     Carries raw ETW relay frames in OTLP Logs and reads them back.
/// </summary>
/// <remarks>
///     <para>
///         Each frame travels as the <c>bytes_value</c> body of one log record, unmodified. The relay session
///         identifier is a resource attribute (<see cref="EtwOtlpAttributes.RelaySessionId" />); the frame sequence
///         number and type are log attributes for routing and diagnostics only. The authoritative values are inside
///         the frame.
///     </para>
///     <para>
///         Collector pipelines between producer and decoder must preserve log record bodies and the order of records
///         of one session. Processors that rewrite or reorder them break the sequencing.
///     </para>
/// </remarks>
public static class RelayLogRecords
{
    /// <summary>The instrumentation scope name used for relay envelopes.</summary>
    public const string ScopeName = "Nefarius.Utilities.ETW.Relay";

    /// <summary>The <c>event_name</c> of relay envelope log records.</summary>
    public const string EventName = "etw.relay.frame";

    /// <summary>
    ///     Wraps encoded relay frames into an export request for one session.
    /// </summary>
    /// <param name="sessionId">The relay session identifier from the hello frame.</param>
    /// <param name="frames">Complete encoded frames, in sequence order.</param>
    /// <param name="resourceAttributes">Additional resource attributes, e.g. <c>service.name</c> and <c>host.name</c>.</param>
    public static ExportLogsServiceRequest CreateRequest(Guid sessionId, IEnumerable<ReadOnlyMemory<byte>> frames,
        IEnumerable<KeyValuePair<string, string>>? resourceAttributes = null)
    {
        ArgumentNullException.ThrowIfNull(frames);

        Resource resource = new();
        resource.Attributes.Add(OtlpValues.String(EtwOtlpAttributes.RelaySessionId, sessionId.ToString("D")));
        if (resourceAttributes is not null)
        {
            foreach (KeyValuePair<string, string> attribute in resourceAttributes)
            {
                resource.Attributes.Add(OtlpValues.String(attribute.Key, attribute.Value));
            }
        }

        ScopeLogs scope = new() { Scope = new InstrumentationScope { Name = ScopeName } };
        foreach (ReadOnlyMemory<byte> frame in frames)
        {
            scope.LogRecords.Add(CreateLogRecord(frame.Span));
        }

        ResourceLogs resourceLogs = new() { Resource = resource };
        resourceLogs.ScopeLogs.Add(scope);

        ExportLogsServiceRequest request = new();
        request.ResourceLogs.Add(resourceLogs);
        return request;
    }

    /// <summary>
    ///     Wraps one encoded frame into a log record.
    /// </summary>
    public static LogRecord CreateLogRecord(ReadOnlySpan<byte> frame)
    {
        LogRecord record = new()
        {
            EventName = EventName,
            Body = new AnyValue { BytesValue = ByteString.CopyFrom(frame) },
            ObservedTimeUnixNano = OtlpValues.ToUnixNanos(DateTime.UtcNow)
        };

        // The envelope attributes mirror the prefix of the frame; they are advisory and only set for valid prefixes.
        if (TryReadPrefix(frame, out RawEtwFrameType type, out ulong sequence))
        {
            record.Attributes.Add(OtlpValues.String(EtwOtlpAttributes.RelayFrameType, FrameTypeName(type)));
            record.Attributes.Add(OtlpValues.Int(EtwOtlpAttributes.RelaySequence, unchecked((long)sequence)));
        }

        return record;
    }

    /// <summary>
    ///     Extracts the relay frame from a log record, if it carries one.
    /// </summary>
    public static bool TryGetFrame(LogRecord record, out ReadOnlyMemory<byte> frame)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (record.Body is { ValueCase: AnyValue.ValueOneofCase.BytesValue } body)
        {
            frame = body.BytesValue.Memory;
            return true;
        }

        frame = default;
        return false;
    }

    /// <summary>
    ///     Reads the relay session identifier from resource attributes.
    /// </summary>
    public static bool TryGetSessionId(Resource? resource, out Guid sessionId)
    {
        sessionId = Guid.Empty;
        if (resource is null)
        {
            return false;
        }

        foreach (KeyValue attribute in resource.Attributes)
        {
            if (attribute.Key == EtwOtlpAttributes.RelaySessionId &&
                attribute.Value is { ValueCase: AnyValue.ValueOneofCase.StringValue } value)
            {
                return Guid.TryParse(value.StringValue, out sessionId);
            }
        }

        return false;
    }

    internal static bool TryReadPrefix(ReadOnlySpan<byte> frame, out RawEtwFrameType type, out ulong sequence)
    {
        type = default;
        sequence = 0;

        if (frame.Length < RawEtwFrameCodec.PrefixLength || !frame[..4].SequenceEqual("NER1"u8))
        {
            return false;
        }

        type = (RawEtwFrameType)System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(frame[6..]);
        sequence = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(frame[16..]);
        return true;
    }

    private static string FrameTypeName(RawEtwFrameType type) => type switch
    {
        RawEtwFrameType.Hello => "hello",
        RawEtwFrameType.Event => "event",
        RawEtwFrameType.Loss => "loss",
        RawEtwFrameType.End => "end",
        _ => ((ushort)type).ToString()
    };
}
