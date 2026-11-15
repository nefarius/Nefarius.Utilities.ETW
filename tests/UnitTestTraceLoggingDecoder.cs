using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

using Nefarius.Utilities.ETW.Deserializer;

using Windows.Win32.System.Diagnostics.Etw;

namespace Nefarius.Utilities.ETW.Tests;

/// <summary>
///     Feeds hand-built TraceLogging records (schema in extended data type 11) through the decoder.
/// </summary>
public unsafe class TraceLoggingDecoderTests
{
    private static readonly Guid Provider = new("11111111-2222-3333-4444-555555555555");

    private static byte[] Schema(string eventName, params byte[][] fields)
    {
        List<byte> body = [0];
        body.AddRange(Encoding.UTF8.GetBytes(eventName));
        body.Add(0);
        foreach (byte[] field in fields)
        {
            body.AddRange(field);
        }

        ushort size = (ushort)(body.Count + 2);
        return [.. BitConverter.GetBytes(size), .. body];
    }

    private static byte[] Field(string name, byte inType, params byte[] rest)
    {
        return [.. Encoding.UTF8.GetBytes(name), 0, inType, .. rest];
    }

    private static string Decode(byte[] schema, byte[] payload, ushort extType = 11)
    {
        using MemoryStream ms = new();
        bool decoded;

        fixed (byte* schemaPtr = schema)
        fixed (byte* payloadPtr = payload)
        {
            EVENT_HEADER_EXTENDED_DATA_ITEM item = default;
            item.ExtType = extType;
            item.DataSize = (ushort)schema.Length;
            item.DataPtr = (ulong)schemaPtr;

            EVENT_RECORD record = default;
            record.EventHeader.Flags = 0x40;
            record.EventHeader.ProviderId = Provider;
            record.ExtendedDataCount = 1;
            record.ExtendedData = &item;
            record.UserData = payloadPtr;
            record.UserContext = payloadPtr;
            record.UserDataLength = (ushort)payload.Length;

            using Utf8JsonWriter json = new(ms);
            EtwJsonWriter writer = new(json);
            json.WriteStartArray();
            decoded = TraceLoggingDecoder.TryDecode(&record, new EventRecordReader(&record), writer,
                new RuntimeEventMetadata(&record));
            json.WriteEndArray();
        }

        Assert.That(decoded, Is.EqualTo(extType == 11 && schema.Length > 0));
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    [Test]
    public void ScalarsAndStrings_AreDecoded()
    {
        byte[] schema = Schema("MyEvent",
            Field("Count", 8),
            Field("Name", 1),
            Field("Ok", 13 | 0x80, 3),
            Field("Ratio", 12));

        byte[] payload =
        [
            .. BitConverter.GetBytes(42u),
            .. Encoding.Unicode.GetBytes("héllo\0"),
            .. BitConverter.GetBytes(1),
            .. BitConverter.GetBytes(2.5)
        ];

        using JsonDocument doc = JsonDocument.Parse(Decode(schema, payload));
        JsonElement e = doc.RootElement[0].GetProperty("Event");
        JsonElement props = e.GetProperty("Properties")[0];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(e.GetProperty("Name").GetString(), Is.EqualTo("MyEvent"));
            Assert.That(e.GetProperty("ProviderGuid").GetGuid(), Is.EqualTo(Provider));
            Assert.That(props.GetProperty("Count").GetUInt32(), Is.EqualTo(42));
            Assert.That(props.GetProperty("Name").GetString(), Is.EqualTo("héllo"));
            Assert.That(props.GetProperty("Ok").GetBoolean(), Is.True);
            Assert.That(props.GetProperty("Ratio").GetDouble(), Is.EqualTo(2.5));
        }
    }

    [Test]
    public void StructsAndArrays_AreDecoded()
    {
        // "Point" struct with two UInt16 members, then a variable length UInt8 array
        byte[] schema = Schema("Shapes",
            Field("Point", 24 | 0x80, 2),
            Field("X", 6),
            Field("Y", 6),
            Field("Bytes", 4 | 0x40));

        byte[] payload =
        [
            .. BitConverter.GetBytes((ushort)3),
            .. BitConverter.GetBytes((ushort)4),
            .. BitConverter.GetBytes((ushort)2),
            7,
            9
        ];

        using JsonDocument doc = JsonDocument.Parse(Decode(schema, payload));
        JsonElement props = doc.RootElement[0].GetProperty("Event").GetProperty("Properties")[0];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(props.GetProperty("Point").GetProperty("X").GetUInt16(), Is.EqualTo(3));
            Assert.That(props.GetProperty("Point").GetProperty("Y").GetUInt16(), Is.EqualTo(4));
            Assert.That(props.GetProperty("Bytes").EnumerateArray().Select(b => b.GetByte()).ToArray(),
                Is.EqualTo(new byte[] { 7, 9 }));
        }
    }

    [Test]
    public void TagsAreSkipped()
    {
        // event tag (2 byte varint) and field with out type + tag chain
        byte[] body =
        [
            0x81, 0x01,
            .. Encoding.UTF8.GetBytes("Tagged\0"),
            .. Field("V", 8 | 0x80, 0x80 | 2, 0x05)
        ];
        byte[] schema = [.. BitConverter.GetBytes((ushort)(body.Length + 2)), .. body];

        using JsonDocument doc = JsonDocument.Parse(Decode(schema, BitConverter.GetBytes(5u)));
        JsonElement e = doc.RootElement[0].GetProperty("Event");

        Assert.That(e.GetProperty("Name").GetString(), Is.EqualTo("Tagged"));
        Assert.That(e.GetProperty("Properties")[0].GetProperty("V").GetUInt32(), Is.EqualTo(5));
    }

    [Test]
    public void TruncatedPayload_IsRejectedWithoutOutput()
    {
        byte[] schema = Schema("Short", Field("Big", 10));

        using MemoryStream ms = new();
        bool decoded;
        fixed (byte* schemaPtr = schema)
        {
            byte[] payload = new byte[16];
            fixed (byte* payloadPtr = payload)
            {
                EVENT_HEADER_EXTENDED_DATA_ITEM item = default;
                item.ExtType = 11;
                item.DataSize = (ushort)schema.Length;
                item.DataPtr = (ulong)schemaPtr;
                EVENT_RECORD record = default;
                record.ExtendedDataCount = 1;
                record.ExtendedData = &item;
                record.UserData = payloadPtr;
                record.UserContext = payloadPtr;
                record.UserDataLength = 2;

                using Utf8JsonWriter json = new(ms);
                decoded = TraceLoggingDecoder.TryDecode(&record, new EventRecordReader(&record),
                    new EtwJsonWriter(json), new RuntimeEventMetadata(&record));
                json.Flush();
            }
        }

        Assert.That(decoded, Is.False);
        Assert.That(ms.Length, Is.EqualTo(0));
    }

    [Test]
    public void NonTraceLoggingExtendedData_IsIgnored()
    {
        Assert.That(Decode(Schema("Other"), [], 5), Is.EqualTo("[]"));
    }

    [Test]
    public void Boolean8AndNull_AreWrittenAsJsonValues()
    {
        byte[] schema = Schema("Flags",
            Field("On", 4 | 0x80, 3),
            Field("Absent", 0));

        using JsonDocument doc = JsonDocument.Parse(Decode(schema, [1]));
        JsonElement props = doc.RootElement[0].GetProperty("Event").GetProperty("Properties")[0];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(props.GetProperty("On").GetBoolean(), Is.True);
            Assert.That(props.GetProperty("Absent").ValueKind, Is.EqualTo(JsonValueKind.Null));
        }
    }

    [Test]
    public void Utf8OutType_DecodesTerminatedAndCountedStrings()
    {
        byte[] schema = Schema("Text",
            Field("Term", 2 | 0x80, 35),
            Field("Count", 23 | 0x80, 35));
        byte[] hello = "héllo"u8.ToArray();
        byte[] payload = [.. hello, 0, .. BitConverter.GetBytes((ushort)hello.Length), .. hello];

        using JsonDocument doc = JsonDocument.Parse(Decode(schema, payload));
        JsonElement props = doc.RootElement[0].GetProperty("Event").GetProperty("Properties")[0];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(props.GetProperty("Term").GetString(), Is.EqualTo("héllo"));
            Assert.That(props.GetProperty("Count").GetString(), Is.EqualTo("héllo"));
        }
    }

    [Test]
    public void Sid_IsDecodedWithoutTheWindowsApi()
    {
        byte[] schema = Schema("Who", Field("User", 19));
        byte[] sid = [1, 1, 0, 0, 0, 0, 0, 5, 18, 0, 0, 0];

        using JsonDocument doc = JsonDocument.Parse(Decode(schema, sid));
        string? value = doc.RootElement[0].GetProperty("Event").GetProperty("Properties")[0]
            .GetProperty("User").GetString();

        Assert.That(value, Is.EqualTo("S-1-5-18"));
    }

    [Test]
    public void DeeplyNestedSchema_IsRejected()
    {
        List<byte> body = [0];
        body.AddRange(Encoding.UTF8.GetBytes("Deep\0"));
        for (int i = 0; i < 33; i++)
        {
            body.AddRange(Encoding.UTF8.GetBytes("S\0"));
            body.Add(24 | 0x80);
            body.Add(1);
        }

        body.AddRange(Encoding.UTF8.GetBytes("N\0"));
        body.Add(4);
        ushort size = (ushort)(body.Count + 2);
        byte[] schema = [.. BitConverter.GetBytes(size), .. body];

        Assert.That(Try(schema, [0]), Is.False);
    }

    [Test]
    public void HugeArray_IsRejectedBeforeAllocation()
    {
        byte[] schema = Schema("Wide", Field("Items", 4 | 0x40));

        Assert.That(Try(schema, BitConverter.GetBytes((ushort)5000)), Is.False);
    }

    [Test]
    public void CustomSerialization_IsRejected()
    {
        byte[] schema = Schema("C", Field("Blob", 0x66, 3, 0, 0x5A, 0, 0));

        Assert.That(Try(schema, [4, 0, 1, 2, 3, 4]), Is.False);
    }

    [Test]
    public void UnterminatedStrings_AreRejected()
    {
        Assert.That(Try(Schema("U", Field("Name", 1)), [0x41, 0x00]), Is.False);
        Assert.That(Try(Schema("A", Field("Name", 2)), [0x41]), Is.False);
    }

    [Test]
    public void OddCountedString_DoesNotShiftTheNextField()
    {
        byte[] schema = Schema("Pair", Field("Name", 22), Field("Next", 4));
        byte[] payload = [3, 0, 0x41, 0x00, 0x42, 7];

        Assert.That(Try(schema, payload), Is.False);
    }

    [Test]
    public void NonFiniteFloats_AreRejectedBeforeAnyJson()
    {
        Assert.That(Written(Schema("F", Field("V", 11)), BitConverter.GetBytes(float.NaN)), Is.Zero);
        Assert.That(Written(Schema("D", Field("V", 12)), BitConverter.GetBytes(double.PositiveInfinity)), Is.Zero);
    }

    private static int Written(byte[] schema, byte[] payload)
    {
        fixed (byte* schemaPtr = schema)
        fixed (byte* payloadPtr = payload)
        {
            EVENT_HEADER_EXTENDED_DATA_ITEM item = default;
            item.ExtType = 11;
            item.DataSize = (ushort)schema.Length;
            item.DataPtr = (ulong)schemaPtr;
            EVENT_RECORD record = default;
            record.ExtendedDataCount = 1;
            record.ExtendedData = &item;
            record.UserData = payloadPtr;
            record.UserContext = payloadPtr;
            record.UserDataLength = (ushort)payload.Length;
            using MemoryStream ms = new();
            using Utf8JsonWriter json = new(ms);
            bool decoded = TraceLoggingDecoder.TryDecode(&record, new EventRecordReader(&record),
                new EtwJsonWriter(json), new RuntimeEventMetadata(&record));
            json.Flush();
            Assert.That(decoded, Is.False);
            return (int)ms.Length;
        }
    }

    private static bool Try(byte[] schema, byte[] payload)
    {
        fixed (byte* schemaPtr = schema)
        fixed (byte* payloadPtr = payload)
        {
            EVENT_HEADER_EXTENDED_DATA_ITEM item = default;
            item.ExtType = 11;
            item.DataSize = (ushort)schema.Length;
            item.DataPtr = (ulong)schemaPtr;
            EVENT_RECORD record = default;
            record.ExtendedDataCount = 1;
            record.ExtendedData = &item;
            record.UserData = payloadPtr;
            record.UserContext = payloadPtr;
            record.UserDataLength = (ushort)payload.Length;
            using MemoryStream ms = new();
            using Utf8JsonWriter json = new(ms);
            return TraceLoggingDecoder.TryDecode(&record, new EventRecordReader(&record), new EtwJsonWriter(json),
                new RuntimeEventMetadata(&record));
        }
    }
}
