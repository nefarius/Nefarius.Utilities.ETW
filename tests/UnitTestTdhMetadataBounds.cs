using System.Text;

using Nefarius.Utilities.ETW.Deserializer;

using Windows.Win32.System.Diagnostics.Etw;

namespace Nefarius.Utilities.ETW.Tests;

[Category("Unit")]
public class TdhMetadataBoundsTests
{
    private static unsafe string? Read(byte[] bytes, uint offset)
    {
        fixed (byte* pointer = bytes)
        {
            return EventTraceOperandBuilder.ReadBoundedString(pointer, (uint)bytes.Length, offset);
        }
    }

    [Test]
    public void ReadsATerminatedStringAtAByteOffset()
    {
        byte[] bytes = [.. new byte[8], .. Encoding.Unicode.GetBytes("Present\0")];

        Assert.That(Read(bytes, 8), Is.EqualTo("Present"));
    }

    [Test]
    public void RejectsAStringWithoutATerminatorInsideTheBuffer()
    {
        byte[] bytes = [.. new byte[8], .. Encoding.Unicode.GetBytes("Unterminated")];

        Assert.That(Read(bytes, 8), Is.Null);
    }

    [Test]
    public void RejectsZeroOutOfRangeAndMisalignedOffsets()
    {
        byte[] bytes = [.. new byte[8], .. Encoding.Unicode.GetBytes("Name\0")];

        Assert.That(Read(bytes, 0), Is.Null);
        Assert.That(Read(bytes, (uint)bytes.Length), Is.Null);
        Assert.That(Read(bytes, uint.MaxValue - 1), Is.Null);
        Assert.That(Read(bytes, 9), Is.Null);
    }
}

[Category("Unit")]
public unsafe class TdhMetadataDecodingTests
{
    [Test]
    public void InconsistentPropertyCountsNeverParseTrailingBytesAsPropertyRecords()
    {
        // The buffer has room for a second, zeroed property record, so only the record counts can stop the iteration.
        // The name sits after that spare capacity; the header claims many more top-level properties.
        int headerBytes = sizeof(TRACE_EVENT_INFO);
        int recordBytes = sizeof(EVENT_PROPERTY_INFO);
        byte[] name = Encoding.Unicode.GetBytes("Only\0");
        int size = headerBytes + recordBytes + name.Length;
        byte[] raw = new byte[size];
        fixed (byte* pointer = raw)
        {
            TRACE_EVENT_INFO* info = (TRACE_EVENT_INFO*)pointer;
            info->PropertyCount = 1;
            info->TopLevelPropertyCount = 50;
            EVENT_PROPERTY_INFO* first = (EVENT_PROPERTY_INFO*)&info->EventPropertyInfoArray;
            first->NameOffset = (uint)(headerBytes + recordBytes);
            name.CopyTo(raw, (int)first->NameOffset);

            IEventTraceOperand operand = EventTraceOperandBuilder.Build(info, (uint)size, 0);

            Assert.That(operand.Metadata.Properties, Has.Length.EqualTo(1));
            Assert.That(operand.Metadata.Properties[0].Name, Is.EqualTo("Only"));
        }
    }

    [Test]
    public void AnOutOfBoundsPropertyNameOffsetFallsBackInsteadOfReadingPastTheBuffer()
    {
        int size = sizeof(TRACE_EVENT_INFO);
        byte[] raw = new byte[size];
        fixed (byte* pointer = raw)
        {
            TRACE_EVENT_INFO* info = (TRACE_EVENT_INFO*)pointer;
            info->PropertyCount = 1;
            info->TopLevelPropertyCount = 1;
            ((EVENT_PROPERTY_INFO*)&info->EventPropertyInfoArray)->NameOffset = uint.MaxValue - 1;

            IEventTraceOperand operand = EventTraceOperandBuilder.Build(info, (uint)size, 0);

            Assert.That(operand.Metadata.Properties[0].Name, Is.EqualTo("Property0"));
        }
    }
}
