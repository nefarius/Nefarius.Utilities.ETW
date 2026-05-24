using Nefarius.Utilities.ETW.Deserializer.WPP;
using Nefarius.Utilities.ETW.Events;

namespace Nefarius.Utilities.ETW.Tests;

[Category("Unit")]
public class EventInfoConversionTests
{
    private static readonly Guid SampleGuid = Guid.Parse("12345678-1234-1234-1234-123456789abc");
    private const uint SampleAge = 3;
    private const string SamplePdbName = "sample.pdb";

    // -----------------------------------------------------------------------
    // DbgIdRsdsEventInfo
    // -----------------------------------------------------------------------

    [Test]
    public void DbgIdRsdsEventInfo_ToPdbMetaData_RoundTripsGuidAgeName()
    {
        DbgIdRsdsEventInfo info = new()
        {
            GuidSig      = SampleGuid,
            Age          = SampleAge,
            PdbFileName  = SamplePdbName,
            Timestamp    = 12345,
            ProcessId    = 100,
            ThreadId     = 200,
            ImageBase    = 0xFFFF000000000000UL
        };

        PdbMetaData meta = info.ToPdbMetaData();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(meta.Guid,    Is.EqualTo(SampleGuid));
            Assert.That(meta.Age,     Is.EqualTo((int)SampleAge));
            Assert.That(meta.PdbName, Is.EqualTo(SamplePdbName));
        }
    }

    [Test]
    public void DbgIdRsdsEventInfo_ToPdbMetaData_Throws_WhenAgeExceedsInt32Max()
    {
        DbgIdRsdsEventInfo info = new()
        {
            GuidSig     = SampleGuid,
            Age         = (uint)int.MaxValue + 1,
            PdbFileName = SamplePdbName,
            Timestamp   = 0,
            ProcessId   = 0,
            ThreadId    = 0,
            ImageBase   = 0
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => info.ToPdbMetaData());
    }

    [Test]
    public void DbgIdRsdsEventInfo_ToPdbMetaData_AcceptsMaxInt32Age()
    {
        DbgIdRsdsEventInfo info = new()
        {
            GuidSig     = SampleGuid,
            Age         = (uint)int.MaxValue,
            PdbFileName = SamplePdbName,
            Timestamp   = 0,
            ProcessId   = 0,
            ThreadId    = 0,
            ImageBase   = 0
        };

        Assert.DoesNotThrow(() =>
        {
            PdbMetaData meta = info.ToPdbMetaData();
            Assert.That(meta.Age, Is.EqualTo(int.MaxValue));
        });
    }

    // -----------------------------------------------------------------------
    // KernelDbgIdRsdsEventInfo
    // -----------------------------------------------------------------------

    [Test]
    public void KernelDbgIdRsdsEventInfo_ToPdbMetaData_RoundTripsGuidAgeName()
    {
        KernelDbgIdRsdsEventInfo info = new()
        {
            Guid      = SampleGuid,
            Age       = SampleAge,
            PdbName   = SamplePdbName,
            Timestamp = 99999,
            ProcessId = 42,
            ThreadId  = 1
        };

        PdbMetaData meta = info.ToPdbMetaData();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(meta.Guid,    Is.EqualTo(SampleGuid));
            Assert.That(meta.Age,     Is.EqualTo((int)SampleAge));
            Assert.That(meta.PdbName, Is.EqualTo(SamplePdbName));
        }
    }

    [Test]
    public void KernelDbgIdRsdsEventInfo_ToPdbMetaData_Throws_WhenAgeExceedsInt32Max()
    {
        KernelDbgIdRsdsEventInfo info = new()
        {
            Guid      = SampleGuid,
            Age       = (uint)int.MaxValue + 1,
            PdbName   = SamplePdbName,
            Timestamp = 0,
            ProcessId = 0,
            ThreadId  = 0
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => info.ToPdbMetaData());
    }

    [Test]
    public void KernelDbgIdRsdsEventInfo_ToPdbMetaData_AcceptsMaxInt32Age()
    {
        KernelDbgIdRsdsEventInfo info = new()
        {
            Guid      = SampleGuid,
            Age       = (uint)int.MaxValue,
            PdbName   = SamplePdbName,
            Timestamp = 0,
            ProcessId = 0,
            ThreadId  = 0
        };

        Assert.DoesNotThrow(() =>
        {
            PdbMetaData meta = info.ToPdbMetaData();
            Assert.That(meta.Age, Is.EqualTo(int.MaxValue));
        });
    }
}
