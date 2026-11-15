using System.Globalization;

using Nefarius.Utilities.ETW.Events;

using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;

namespace Nefarius.Utilities.ETW.OpenTelemetry;

/// <summary>
///     Maps decoded ETW events to OTLP log records.
/// </summary>
/// <remarks>
///     <list type="bullet">
///         <item>
///             Non-WPP events: <c>body</c> is a key-value list of the decoded properties; header fields are log
///             attributes (see <see cref="EtwOtlpAttributes" />).
///         </item>
///         <item>
///             WPP events: <c>body</c> is the formatted message string, the readable text a log backend displays; the
///             remaining WPP properties (function, level, flags, component, ...) become <c>etw.wpp.*</c> attributes.
///         </item>
///         <item>
///             <c>time_unix_nano</c> is the event time when it is a valid FILETIME, otherwise 0;
///             <c>event_name</c> is the ETW event name.
///         </item>
///     </list>
/// </remarks>
public static class EtwLogRecordMapper
{
    /// <summary>
    ///     Creates the log record for a decoded event.
    /// </summary>
    /// <param name="etwEvent">The decoded event.</param>
    /// <param name="relaySequence">The relay frame sequence number the event came from, when known.</param>
    public static LogRecord ToLogRecord(EtwEvent etwEvent, ulong? relaySequence = null)
    {
        ArgumentNullException.ThrowIfNull(etwEvent);

        LogRecord record = new() { EventName = etwEvent.Name };

        if (etwEvent.TimestampUtc is { } time)
        {
            record.TimeUnixNano = OtlpValues.ToUnixNanos(time);
        }

        record.ObservedTimeUnixNano = OtlpValues.ToUnixNanos(DateTime.UtcNow);

        bool isWpp = etwEvent.Name == EtwOtlpAttributes.WppEventName;
        string? wppLevelName = null;

        if (isWpp)
        {
            foreach (KeyValuePair<string, EtwValue> member in etwEvent.Properties.Members)
            {
                if (member.Key == "FormattedString" && member.Value.Kind == EtwValueKind.String)
                {
                    record.Body = new AnyValue { StringValue = member.Value.AsString };
                }
                else
                {
                    if (member.Key == "LevelName" && member.Value.Kind == EtwValueKind.String)
                    {
                        wppLevelName = member.Value.AsString;
                    }

                    record.Attributes.Add(new KeyValue
                    {
                        Key = EtwOtlpAttributes.WppPrefix + ToSnakeCase(member.Key),
                        Value = OtlpValues.ToAnyValue(member.Value)
                    });
                }
            }
        }
        else
        {
            record.Body = OtlpValues.ToAnyValue(etwEvent.Properties);
        }

        (SeverityNumber severity, string text) = MapSeverity(etwEvent.Level, wppLevelName);
        record.SeverityNumber = severity;
        record.SeverityText = text;

        record.Attributes.Add(OtlpValues.String(EtwOtlpAttributes.ProviderId, etwEvent.ProviderId.ToString("D")));
        record.Attributes.Add(OtlpValues.Int(EtwOtlpAttributes.EventId, etwEvent.Id));
        record.Attributes.Add(OtlpValues.Int(EtwOtlpAttributes.EventVersion, etwEvent.Version));
        record.Attributes.Add(OtlpValues.Int(EtwOtlpAttributes.ProcessId, etwEvent.ProcessId));
        record.Attributes.Add(OtlpValues.Int(EtwOtlpAttributes.ThreadId, etwEvent.ThreadId));
        record.Attributes.Add(OtlpValues.Int(EtwOtlpAttributes.Processor, unchecked((long)etwEvent.ProcessorNumber)));
        record.Attributes.Add(OtlpValues.Int(EtwOtlpAttributes.Level, etwEvent.Level));
        record.Attributes.Add(OtlpValues.Int(EtwOtlpAttributes.Opcode, etwEvent.Opcode));
        record.Attributes.Add(OtlpValues.Int(EtwOtlpAttributes.Task, etwEvent.Task));
        record.Attributes.Add(OtlpValues.Int(EtwOtlpAttributes.Channel, etwEvent.Channel));
        record.Attributes.Add(OtlpValues.Int(EtwOtlpAttributes.Flags, etwEvent.Flags));
        record.Attributes.Add(OtlpValues.String(EtwOtlpAttributes.Keyword,
            "0x" + etwEvent.Keyword.ToString("x16", CultureInfo.InvariantCulture)));

        if (etwEvent.ActivityId != Guid.Empty)
        {
            record.Attributes.Add(OtlpValues.String(EtwOtlpAttributes.ActivityId, etwEvent.ActivityId.ToString("D")));
        }

        if (etwEvent.RelatedActivityId != Guid.Empty)
        {
            record.Attributes.Add(OtlpValues.String(EtwOtlpAttributes.RelatedActivityId,
                etwEvent.RelatedActivityId.ToString("D")));
        }

        if (etwEvent.StackMatchId != 0)
        {
            record.Attributes.Add(OtlpValues.String(EtwOtlpAttributes.StackMatchId,
                "0x" + etwEvent.StackMatchId.ToString("x", CultureInfo.InvariantCulture)));
        }

        if (etwEvent.Stacks is { Count: > 0 } stacks)
        {
            ArrayValue array = new();
            foreach (ulong address in stacks)
            {
                array.Values.Add(new AnyValue
                {
                    StringValue = "0x" + address.ToString("x", CultureInfo.InvariantCulture)
                });
            }

            record.Attributes.Add(new KeyValue
            {
                Key = EtwOtlpAttributes.Stack,
                Value = new AnyValue { ArrayValue = array }
            });
        }

        if (relaySequence is { } sequence)
        {
            record.Attributes.Add(OtlpValues.Int(EtwOtlpAttributes.RelaySequence, unchecked((long)sequence)));
        }

        return record;
    }

    /// <summary>
    ///     Maps an ETW level to an OTLP severity. WPP records carry level 0 in the header; their level name
    ///     (e.g. <c>TRACE_LEVEL_ERROR</c>) is used instead.
    /// </summary>
    public static (SeverityNumber Number, string Text) MapSeverity(byte level, string? wppLevelName = null)
    {
        if (level == 0 && !string.IsNullOrEmpty(wppLevelName))
        {
            string name = wppLevelName.ToUpperInvariant();
            if (name.Contains("FATAL") || name.Contains("CRITICAL"))
            {
                return (SeverityNumber.Fatal, "FATAL");
            }

            if (name.Contains("ERROR"))
            {
                return (SeverityNumber.Error, "ERROR");
            }

            if (name.Contains("WARN"))
            {
                return (SeverityNumber.Warn, "WARN");
            }

            if (name.Contains("INFO"))
            {
                return (SeverityNumber.Info, "INFO");
            }

            if (name.Contains("VERBOSE") || name.Contains("DEBUG"))
            {
                return (SeverityNumber.Debug, "DEBUG");
            }
        }

        return level switch
        {
            1 => (SeverityNumber.Fatal, "FATAL"),
            2 => (SeverityNumber.Error, "ERROR"),
            3 => (SeverityNumber.Warn, "WARN"),
            4 => (SeverityNumber.Info, "INFO"),
            >= 5 => (SeverityNumber.Debug, "DEBUG"),
            _ => (SeverityNumber.Unspecified, string.Empty)
        };
    }

    private static string ToSnakeCase(string name)
    {
        System.Text.StringBuilder builder = new(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (char.IsUpper(c))
            {
                bool boundary = i > 0 && (char.IsLower(name[i - 1]) ||
                                          (i + 1 < name.Length && char.IsLower(name[i + 1]) && char.IsUpper(name[i - 1])));
                if (boundary)
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
