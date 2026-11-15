using Windows.Win32.System.Diagnostics.Etw;

using Nefarius.Utilities.ETW.Etl;

namespace Nefarius.Utilities.ETW.Tests;

/// <summary>
///     Tests for the managed ETL container reader, which runs on every platform.
/// </summary>
[Category("EndToEnd")]
public unsafe class EtlReaderTests
{
    private const ushort TraceMessageFlag = 0x0100;

    private sealed record Stats(long Total, long Wpp, bool Ordered, long First, long Last);

    private static Stats Read(string path, bool raw = false)
    {
        long total = 0, wpp = 0, first = 0, last = 0;
        bool ordered = true;

        TraceOpenFailure? failure = TraceProcessor.ProcessFiles([path], EtwBackend.Managed, raw, default, null,
            (EVENT_RECORD* record, in EtlRecordExtras extras) =>
            {
                total++;
                if ((record->EventHeader.Flags & TraceMessageFlag) != 0)
                {
                    wpp++;
                }

                long stamp = record->EventHeader.TimeStamp;
                if (total == 1)
                {
                    first = stamp;
                }
                else if (stamp < last)
                {
                    ordered = false;
                }

                last = stamp;
            });

        Assert.That(failure, Is.Null);
        return new Stats(total, wpp, ordered, first, last);
    }

    [TestCase("./traces/DsHidMini.etl", 68183, 68179)]
    [TestCase("./traces/BthPS3_0.etl", 663, 658)]
    public void ReadsAllRecords_InTimestampOrder(string path, long expectedTotal, long expectedWpp)
    {
        Stats stats = Read(path);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(stats.Total, Is.EqualTo(expectedTotal));
            Assert.That(stats.Wpp, Is.EqualTo(expectedWpp));
            Assert.That(stats.Ordered, Is.True, "Records must be delivered in timestamp order.");
        }
    }

    [Test]
    public void TimestampsAreConvertedToPlausibleFileTimes()
    {
        Stats stats = Read("./traces/DsHidMini.etl");

        DateTime first = DateTime.FromFileTimeUtc(stats.First);
        DateTime last = DateTime.FromFileTimeUtc(stats.Last);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Year, Is.EqualTo(2025));
            Assert.That(last, Is.GreaterThanOrEqualTo(first));
            Assert.That(last - first, Is.LessThan(TimeSpan.FromHours(1)));
        }
    }

    [Test]
    public void RawTimestamps_AreNotConverted()
    {
        Stats raw = Read("./traces/DsHidMini.etl", true);
        Stats converted = Read("./traces/DsHidMini.etl");

        Assert.That(raw.First, Is.Not.EqualTo(converted.First));
    }

    [Test]
    public void MultipleFiles_AreMerged()
    {
        long total = 0;
        TraceOpenFailure? failure = TraceProcessor.ProcessFiles(
            ["./traces/BthPS3_0.etl", "./traces/DsHidMini.etl"], EtwBackend.Managed, false, default, null,
            (EVENT_RECORD* _, in EtlRecordExtras _) => total++);

        Assert.That(failure, Is.Null);
        Assert.That(total, Is.EqualTo(663 + 68183));
    }

    [Test]
    public void MissingFile_ReportsFailureWithIndex()
    {
        TraceOpenFailure? failure = TraceProcessor.ProcessFiles(
            ["./traces/BthPS3_0.etl", "./traces/__does_not_exist__.etl"], EtwBackend.Managed, false, default, null,
            (EVENT_RECORD* _, in EtlRecordExtras _) => Assert.Fail("No record may be delivered."));

        Assert.That(failure, Is.Not.Null);
        Assert.That(failure!.Value.Index, Is.EqualTo(1));
    }

    [Test]
    public void NonEtlFile_IsRejected()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, new byte[4096]);
            TraceOpenFailure? failure = TraceProcessor.ProcessFiles([path], EtwBackend.Managed, false, default, null,
                (EVENT_RECORD* _, in EtlRecordExtras _) => { });

            Assert.That(failure, Is.Not.Null);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void LogfileHeaderBufferSize_MustMatchBufferHeader()
    {
        string path = Path.GetTempFileName();
        try
        {
            byte[] bytes = File.ReadAllBytes("./traces/BthPS3_0.etl");
            bytes[104] = 0;
            bytes[105] = 0;
            bytes[106] = 0;
            bytes[107] = 0;
            File.WriteAllBytes(path, bytes);

            TraceOpenFailure? failure = TraceProcessor.ProcessFiles([path], EtwBackend.Managed, false, default, null,
                (EVENT_RECORD* _, in EtlRecordExtras _) => Assert.Fail("A mismatched buffer size must not be read."));

            Assert.That(failure, Is.Not.Null);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void Cancellation_StopsProcessing()
    {
        using CancellationTokenSource cts = new();
        long total = 0;

        Assert.Throws<OperationCanceledException>(() => TraceProcessor.ProcessFiles(["./traces/DsHidMini.etl"],
            EtwBackend.Managed, false, cts.Token, null,
            (EVENT_RECORD* _, in EtlRecordExtras _) =>
            {
                if (++total == 2000)
                {
                    cts.Cancel();
                }
            }));

        Assert.That(total, Is.LessThan(68183));
    }
}
