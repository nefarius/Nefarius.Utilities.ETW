using Google.Protobuf;

using Nefarius.Utilities.ETW.Events;

using OpenTelemetry.Proto.Common.V1;

namespace Nefarius.Utilities.ETW.OpenTelemetry;

/// <summary>
///     Small helpers to build OTLP attribute values and to map <see cref="EtwValue" /> to <see cref="AnyValue" />.
/// </summary>
public static class OtlpValues
{
    private static readonly DateTime UnixEpoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Creates a string attribute.</summary>
    public static KeyValue String(string key, string value) =>
        new() { Key = key, Value = new AnyValue { StringValue = value } };

    /// <summary>Creates an integer attribute.</summary>
    public static KeyValue Int(string key, long value) => new() { Key = key, Value = new AnyValue { IntValue = value } };

    /// <summary>
    ///     Converts a UTC time to nanoseconds since the Unix epoch; times before the epoch map to 0.
    /// </summary>
    public static ulong ToUnixNanos(DateTime utc)
    {
        long ticks = utc.ToUniversalTime().Ticks - UnixEpoch.Ticks;
        return ticks <= 0 ? 0UL : (ulong)ticks * 100UL;
    }

    /// <summary>
    ///     Maps a decoded value to its OTLP representation.
    /// </summary>
    /// <remarks>
    ///     OTLP has no unsigned or GUID types: unsigned values above <see cref="long.MaxValue" /> become decimal strings,
    ///     GUIDs become "D"-format strings, and date and time values become ISO 8601 strings.
    /// </remarks>
    public static AnyValue ToAnyValue(EtwValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        switch (value.Kind)
        {
            case EtwValueKind.Boolean:
                return new AnyValue { BoolValue = value.AsBoolean };
            case EtwValueKind.Int64:
                return new AnyValue { IntValue = value.AsInt64 };
            case EtwValueKind.UInt64:
                ulong unsigned = value.AsUInt64;
                return unsigned <= long.MaxValue
                    ? new AnyValue { IntValue = (long)unsigned }
                    : new AnyValue { StringValue = unsigned.ToString(System.Globalization.CultureInfo.InvariantCulture) };
            case EtwValueKind.Double:
                return new AnyValue { DoubleValue = value.AsDouble };
            case EtwValueKind.String:
                return new AnyValue { StringValue = value.AsString };
            case EtwValueKind.Binary:
                return new AnyValue { BytesValue = ByteString.CopyFrom(value.AsBinary) };
            case EtwValueKind.Guid:
                return new AnyValue { StringValue = value.AsGuid.ToString("D") };
            case EtwValueKind.DateTime:
                return new AnyValue
                {
                    StringValue = value.AsDateTime.ToString("o", System.Globalization.CultureInfo.InvariantCulture)
                };
            case EtwValueKind.Array:
                ArrayValue array = new();
                foreach (EtwValue item in value.Items)
                {
                    array.Values.Add(ToAnyValue(item));
                }

                return new AnyValue { ArrayValue = array };
            case EtwValueKind.Object:
                KeyValueList list = new();
                foreach (KeyValuePair<string, EtwValue> member in value.Members)
                {
                    list.Values.Add(new KeyValue { Key = member.Key, Value = ToAnyValue(member.Value) });
                }

                return new AnyValue { KvlistValue = list };
            default:
                // An empty AnyValue is OTLP's null.
                return new AnyValue();
        }
    }
}
