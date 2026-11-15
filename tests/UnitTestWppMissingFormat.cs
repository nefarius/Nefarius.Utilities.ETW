using System.Buffers;
using System.Text.Json;

using Nefarius.Utilities.ETW.Deserializer;
using Nefarius.Utilities.ETW.Deserializer.WPP;

using Windows.Win32.System.Diagnostics.Etw;

namespace Nefarius.Utilities.ETW.Tests;

[Category("Unit")]
public unsafe class WppMissingFormatTests
{
    [Test]
    public void AnUndecodedRecordKeepsFormattedStringUnavailable()
    {
        EVENT_RECORD record = default;
        WppEventRecord decoded = new(new EventRecordReader(&record));

        Assert.That(decoded.GuidName, Is.Null);
        Assert.That(decoded.FlagsName, Is.Null);
        Assert.That(decoded.FunctionName, Is.Null);
        Assert.Throws<InvalidOperationException>(() => _ = decoded.FormattedString);
    }

    [Test]
    public void MissingTmfMetadataIsJsonNullWhileFormattedStringStaysPresent()
    {
        byte[] storage = new byte[256];
        fixed (byte* raw = storage)
        {
            EVENT_RECORD* record = (EVENT_RECORD*)raw;
            record->EventHeader.TimeStamp = DateTime.UnixEpoch.ToFileTimeUtc();
            record->EventHeader.ProviderId = new Guid("00112233-4455-6677-8899-AABBCCDDEEFF");
            record->EventHeader.EventDescriptor.Version = 3;
            record->EventHeader.EventDescriptor.Id = 7;

            WppEventRecord decoded = new(new EventRecordReader(record));
            decoded.DecodeManaged(new DecodingContext(), default);

            Assert.That(decoded.GuidName, Is.Null);
            Assert.That(decoded.GuidTypeName, Is.Null);
            Assert.That(decoded.FlagsName, Is.Null);
            Assert.That(decoded.LevelName, Is.Null);
            Assert.That(decoded.FunctionName, Is.Null);
            Assert.That(decoded.ComponentName, Is.Null);
            Assert.That(decoded.SubComponentName, Is.Null);
            Assert.That(decoded.FormattedString, Does.Contain("No format information found."));

            ArrayBufferWriter<byte> buffer = new();
            using Utf8JsonWriter json = new(buffer);
            json.WriteStartArray();
            EtwJsonWriter writer = new(json);
            writer.WriteUnicodeString(decoded.GuidName);
            writer.WriteUnicodeString(decoded.FormattedString);
            json.WriteEndArray();
            json.Flush();

            using JsonDocument document = JsonDocument.Parse(buffer.WrittenMemory);
            Assert.That(document.RootElement[0].ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(document.RootElement[1].GetString(), Does.Contain("No format information found."));
        }
    }
}
