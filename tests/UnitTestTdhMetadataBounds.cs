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

    [Test]
    public void AStructRangeThatStartsInsideTheArrayAndRunsPastItIsRejected()
    {
        WithSchema(4, 2, (info, properties) =>
        {
            properties[1].Flags = PROPERTY_FLAGS.PropertyStruct;
            properties[1].Anonymous1.structType.StructStartIndex = 2;
            properties[1].Anonymous1.structType.NumOfStructMembers = 3;
        }, accepted => Assert.That(accepted, Is.False));
    }

    [Test]
    public void AStructRangeThatStartsAtALaterIndexAndFitsIsAccepted()
    {
        WithSchema(4, 1, (info, properties) =>
        {
            properties[0].Flags = PROPERTY_FLAGS.PropertyStruct;
            properties[0].Anonymous1.structType.StructStartIndex = 2;
            properties[0].Anonymous1.structType.NumOfStructMembers = 2;
        }, accepted => Assert.That(accepted, Is.True));
    }

    [Test]
    public void ACountIndexPastTheOperandsVisitedSoFarIsRejected()
    {
        WithSchema(1, 1, (info, properties) =>
        {
            properties[0].Flags = PROPERTY_FLAGS.PropertyParamCount;
            properties[0].Anonymous2.countPropertyIndex = 1;
        }, accepted => Assert.That(accepted, Is.False));
    }

    [Test]
    public void ACountIndexOfAnEarlierOperandIsAccepted()
    {
        WithSchema(2, 2, (info, properties) =>
        {
            properties[1].Flags = PROPERTY_FLAGS.PropertyParamCount;
            properties[1].Anonymous2.countPropertyIndex = 0;
        }, accepted => Assert.That(accepted, Is.True));
    }

    [Test]
    public void ALengthIndexPastTheOperandsVisitedSoFarIsRejected()
    {
        WithSchema(1, 1, (info, properties) =>
        {
            properties[0].Flags = PROPERTY_FLAGS.PropertyParamLength;
            properties[0].Anonymous3.lengthPropertyIndex = 1;
        }, accepted => Assert.That(accepted, Is.False));
    }

    [Test]
    public void CyclicStructRangesAreRejectedAndNestedStructsAreAccepted()
    {
        WithSchema(2, 1, (info, properties) =>
        {
            properties[0].Flags = PROPERTY_FLAGS.PropertyStruct;
            properties[0].Anonymous1.structType.StructStartIndex = 1;
            properties[0].Anonymous1.structType.NumOfStructMembers = 1;
            properties[1].Flags = PROPERTY_FLAGS.PropertyStruct;
            properties[1].Anonymous1.structType.StructStartIndex = 0;
            properties[1].Anonymous1.structType.NumOfStructMembers = 1;
        }, accepted => Assert.That(accepted, Is.False));

        WithSchema(3, 1, (info, properties) =>
        {
            properties[0].Flags = PROPERTY_FLAGS.PropertyStruct;
            properties[0].Anonymous1.structType.StructStartIndex = 1;
            properties[0].Anonymous1.structType.NumOfStructMembers = 2;
            properties[1].Flags = PROPERTY_FLAGS.PropertyStruct;
            properties[1].Anonymous1.structType.StructStartIndex = 2;
            properties[1].Anonymous1.structType.NumOfStructMembers = 1;
        }, accepted => Assert.That(accepted, Is.True));
    }

    private unsafe delegate void SchemaFill(TRACE_EVENT_INFO* info, EVENT_PROPERTY_INFO* properties);

    private static unsafe void WithSchema(
        int propertyCount,
        int topLevel,
        SchemaFill fill,
        Action<bool> assert)
    {
        int size = sizeof(TRACE_EVENT_INFO) + (propertyCount - 1) * sizeof(EVENT_PROPERTY_INFO);
        byte[] raw = new byte[size];
        fixed (byte* pointer = raw)
        {
            TRACE_EVENT_INFO* info = (TRACE_EVENT_INFO*)pointer;
            info->PropertyCount = (uint)propertyCount;
            info->TopLevelPropertyCount = (uint)topLevel;
            EVENT_PROPERTY_INFO* properties = (EVENT_PROPERTY_INFO*)&info->EventPropertyInfoArray;
            fill(info, properties);
            assert(EventTraceOperandBuilder.IsDecodableSchema(info, (uint)size));
        }
    }

}
