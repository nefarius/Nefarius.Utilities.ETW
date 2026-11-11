using System.Text;

using Nefarius.Utilities.ETW.Deserializer;

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
