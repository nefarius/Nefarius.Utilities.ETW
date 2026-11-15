using Windows.Win32.Foundation;
using Windows.Win32.System.Diagnostics.Etw;

using Nefarius.Utilities.ETW.Etl;

namespace Nefarius.Utilities.ETW.Tests;

/// <summary>
///     Inventory of the fixtures produced by <c>tests/fixtures/New-EtlFixtures.ps1</c>: what the native reader sees
///     in each file and how much of it the managed reader delivers. Expected counts describe the fixtures that are
///     committed; regenerating them requires updating the numbers.
/// </summary>
[Category("EndToEnd")]
public unsafe class EtlFixtureTests
{
    private static readonly Guid TraceLoggingProvider = new("6f4e9d3a-0b1c-4a52-9b0e-4d7a1e2c3b01");
    private static readonly Guid ManifestProvider = new("6f4e9d3a-0b1c-4a52-9b0e-4d7a1e2c3b02");

    /// <summary>The event trace header event every ETL file starts with.</summary>
    private static readonly Guid EventTraceEvent = new("68fdd900-4a3e-11d1-84f4-0000f80464e3");

    /// <summary>The kernel image/process classic event groups seen in <c>kernel-classic.etl</c>.</summary>
    private static readonly Guid ProcessGroup = new("3d6fa8d0-fe05-11d0-9dda-00c04fd7ba7c");

    private static readonly Guid ThreadGroup = new("3d6fa8d1-fe05-11d0-9dda-00c04fd7ba7c");
    internal sealed record Inventory(
        long Total,
        Dictionary<Guid, long> ByProvider,
        List<string> Warnings,
        TraceOpenFailure? Failure)
    {
        public long Of(Guid provider) => ByProvider.GetValueOrDefault(provider);
    }

    internal static Inventory Count(string name, EtwBackend backend)
    {
        long total = 0;
        Dictionary<Guid, long> byProvider = [];
        List<string> warnings = [];

        TraceOpenFailure? failure = TraceProcessor.ProcessFiles([$"./traces/fixtures/{name}.etl"], backend, false,
            default, warnings.Add, (EVENT_RECORD* record, in EtlRecordExtras _) =>
            {
                total++;
                Guid provider = record->EventHeader.ProviderId;
                byProvider[provider] = byProvider.GetValueOrDefault(provider) + 1;
            });

        return new Inventory(total, byProvider, warnings, failure);
    }

    [TestCase("tracelogging-x64", 11)]
    [TestCase("tracelogging-x86", 11)]
    [TestCase("bulk-uncompressed", 402)]
    public void TraceLoggingFixtures_AreReadByTheManagedReader(string name, long expected)
    {
        Inventory inv = Count(name, EtwBackend.Managed);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(inv.Failure, Is.Null);
            Assert.That(inv.Of(TraceLoggingProvider), Is.EqualTo(expected));
            Assert.That(inv.Of(EventTraceEvent), Is.EqualTo(2));
            Assert.That(inv.Warnings, Is.Empty);
        }
    }

    [Test]
    public void ManifestFixture_IsReadByTheManagedReader()
    {
        Inventory inv = Count("manifest-x64", EtwBackend.Managed);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(inv.Failure, Is.Null);
            // Seven events plus the in-band manifest and enable-rundown events.
            Assert.That(inv.Of(ManifestProvider), Is.EqualTo(9));
            Assert.That(inv.Warnings, Is.Empty);
        }
    }

    [Test]
    public void ClassicKernelFixture_IsReadByTheManagedReader()
    {
        Inventory inv = Count("kernel-classic", EtwBackend.Managed);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(inv.Failure, Is.Null);
            Assert.That(inv.Of(ProcessGroup), Is.EqualTo(93));
            Assert.That(inv.Of(ThreadGroup), Is.GreaterThan(0));
        }
    }

    [Test]
    public void CompressedFixture_IsReadByTheManagedReader()
    {
        Inventory compressed = Count("bulk-compressed", EtwBackend.Managed);
        Inventory plain = Count("bulk-uncompressed", EtwBackend.Managed);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(compressed.Failure, Is.Null);
            Assert.That(compressed.Warnings, Is.Empty);
            Assert.That(compressed.Total, Is.EqualTo(plain.Total));
            Assert.That(compressed.Of(TraceLoggingProvider), Is.EqualTo(402));
        }
    }

    [Test]
    public void CompressedAndUncompressedFixtures_CarryTheSamePayloads()
    {
        static List<string> Payloads(string name)
        {
            List<string> payloads = [];
            TraceProcessor.ProcessFiles([$"./traces/fixtures/{name}.etl"], EtwBackend.Managed, false, default, null,
                (EVENT_RECORD* r, in EtlRecordExtras _) =>
                {
                    if (r->EventHeader.ProviderId == TraceLoggingProvider && r->UserDataLength > 0)
                    {
                        payloads.Add(Convert.ToHexString(new ReadOnlySpan<byte>(r->UserData, r->UserDataLength)));
                    }
                });
            payloads.Sort(StringComparer.Ordinal);
            return payloads;
        }

        List<string> compressed = Payloads("bulk-compressed");
        Assert.That(compressed, Is.Not.Empty);
        Assert.That(compressed, Is.EqualTo(Payloads("bulk-uncompressed")));
    }

    private static TraceOpenFailure? OpenModified(Action<byte[]> modify, out long delivered)
    {
        byte[] bytes = File.ReadAllBytes("./traces/fixtures/bulk-compressed.etl");
        modify(bytes);
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, bytes);
            long count = 0;
            TraceOpenFailure? failure = TraceProcessor.ProcessFiles([path], EtwBackend.Managed, false, default, null,
                (EVENT_RECORD* _, in EtlRecordExtras _) => count++);
            delivered = count;
            return failure;
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void CompressedFixture_WithCorruptData_IsRejectedBeforeAnyRecordIsDelivered()
    {
        // Overwrite the flags and literals of the first compressed buffer with an impossible back reference.
        TraceOpenFailure? failure = OpenModified(b =>
        {
            for (int i = 65536 + 72; i < 65536 + 72 + 64; i++)
            {
                b[i] = 0xFF;
            }
        }, out long delivered);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure, Is.Not.Null);
            Assert.That(failure!.Value.Error, Is.EqualTo(WIN32_ERROR.ERROR_INVALID_DATA));
            Assert.That(delivered, Is.Zero);
        }
    }

    [Test]
    public void CompressedFixture_WithOversizedBufferRecord_IsRejected()
    {
        TraceOpenFailure? failure = OpenModified(b => BitConverter.TryWriteBytes(b.AsSpan(65536), uint.MaxValue),
            out long delivered);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure, Is.Not.Null);
            Assert.That(failure!.Value.Error, Is.EqualTo(WIN32_ERROR.ERROR_INVALID_DATA));
            Assert.That(delivered, Is.Zero);
        }
    }

    [Test]
    public void CompressedFixture_Truncated_IsRejected()
    {
        string path = Path.GetTempFileName();
        try
        {
            byte[] bytes = File.ReadAllBytes("./traces/fixtures/bulk-compressed.etl");
            File.WriteAllBytes(path, bytes.AsSpan(0, bytes.Length - 100).ToArray());

            TraceOpenFailure? failure = TraceProcessor.ProcessFiles([path], EtwBackend.Managed, false, default, null,
                (EVENT_RECORD* _, in EtlRecordExtras _) => Assert.Fail("A truncated compressed file must not be read."));

            Assert.That(failure, Is.Not.Null);
            Assert.That(failure!.Value.Error, Is.EqualTo(WIN32_ERROR.ERROR_INVALID_DATA));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void XpressLz77_DecodesLiteralsOverlappingMatchesAndRejectsBadReferences()
    {
        // Flags 0x40000000 (little endian): a literal 'a', then a match with offset 1 and length 3 + 4. The match
        // word is ((offset - 1) << 3) | (length - 3).
        byte[] input = [0x00, 0x00, 0x00, 0x40, (byte)'a', 0x04, 0x00];
        byte[] output = new byte[16];

        bool ok = XpressLz77.TryDecompress(input, output, out int written);
        Assert.That(ok, Is.True);
        Assert.That(output.AsSpan(0, written).ToArray(), Is.EqualTo("aaaaaaaa"u8.ToArray()));

        // A match that reaches before the start of the output is invalid.
        byte[] bad = [0x00, 0x00, 0x00, 0x80, 0x08, 0x00];
        Assert.That(XpressLz77.TryDecompress(bad, output, out _), Is.False);

        // The output must not be exceeded.
        Assert.That(XpressLz77.TryDecompress(input, new byte[4], out _), Is.False);
    }

    [Test]
    public void XpressLz77_RejectsExtendedMatchLengthBelowTheMinimum()
    {
        // Extended length 1 is below 15 + 7 and must not wrap into a valid match (MS-XCA 2.4.4).
        byte[] input = [0xFF, 0xFF, 0xFF, 0x7F, 0x61, 0x07, 0x00, 0x0F, 0xFF, 0x01, 0x00];
        Assert.That(XpressLz77.TryDecompress(input, new byte[64], out _), Is.False);
    }

    [Test]
    public void CompressedFixture_TruncatedInsideTheFirstBuffer_IsRejected()
    {
        string path = Path.GetTempFileName();
        try
        {
            byte[] bytes = File.ReadAllBytes("./traces/fixtures/bulk-compressed.etl");
            File.WriteAllBytes(path, bytes.AsSpan(0, 65536 - 100).ToArray());

            TraceOpenFailure? failure = TraceProcessor.ProcessFiles([path], EtwBackend.Managed, false, default, null,
                (EVENT_RECORD* _, in EtlRecordExtras _) => Assert.Fail("A truncated compressed file must not be read."));

            Assert.That(failure, Is.Not.Null);
            Assert.That(failure!.Value.Error, Is.EqualTo(WIN32_ERROR.ERROR_INVALID_DATA));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void CompressedAndUncompressedFixtures_ShareTheirWorkload()
    {
        FileInfo compressed = new("./traces/fixtures/bulk-compressed.etl");
        FileInfo plain = new("./traces/fixtures/bulk-uncompressed.etl");

        Assert.That(compressed.Length, Is.LessThan(plain.Length), "Compression must shrink the file.");
    }

    [Test]
    [Platform("Win")]
    [TestCase("tracelogging-x64")]
    [TestCase("tracelogging-x86")]
    [TestCase("manifest-x64")]
    [TestCase("bulk-uncompressed")]
    public void NativeAndManagedReaders_SeeTheSameRecords(string name)
    {
        Inventory native = Count(name, EtwBackend.Native);
        Inventory managed = Count(name, EtwBackend.Managed);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(native.Failure, Is.Null);
            Assert.That(managed.Total, Is.EqualTo(native.Total));
            Assert.That(managed.ByProvider, Is.EquivalentTo(native.ByProvider));
        }
    }

    [Test]
    [Platform("Win")]
    public void CompressedRecords_MatchTheNativeReaderFieldByField()
    {
        Assert.That(Records("bulk-compressed", EtwBackend.Managed),
            Is.EqualTo(Records("bulk-compressed", EtwBackend.Native)));
    }

    [Test]
    [Platform("Win")]
    public void NativeReader_ReadsTheCompressedFixtureLikeTheUncompressedOne()
    {
        Inventory compressed = Count("bulk-compressed", EtwBackend.Native);
        Inventory plain = Count("bulk-uncompressed", EtwBackend.Native);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(compressed.Failure, Is.Null);
            Assert.That(compressed.Total, Is.EqualTo(plain.Total));
            Assert.That(compressed.Of(TraceLoggingProvider), Is.EqualTo(plain.Of(TraceLoggingProvider)));
        }
    }

    /// <summary>
    ///     Every header field and the payload of every record, so the backends can be compared record by record.
    /// </summary>
    private static List<string> Records(string name, EtwBackend backend)
    {
        List<string> records = [];
        TraceOpenFailure? failure = TraceProcessor.ProcessFiles([$"./traces/fixtures/{name}.etl"], backend, false,
            default, null, (EVENT_RECORD* r, in EtlRecordExtras _) =>
            {
                EVENT_HEADER h = r->EventHeader;
                records.Add(string.Join('|', h.TimeStamp, h.ProviderId, h.EventDescriptor.Id, h.EventDescriptor.Version,
                    h.EventDescriptor.Opcode, h.EventDescriptor.Level, h.ProcessId, h.ThreadId,
                    // Only the NO_CPUTIME bit: ProcessTrace also sets header size and flags the managed reader
                    // does not mirror.
                    h.Flags & 0x0010, r->UserDataLength, Convert.ToHexString(new ReadOnlySpan<byte>(r->UserData, r->UserDataLength))));
            });
        Assert.That(failure, Is.Null);
        records.Sort(StringComparer.Ordinal);
        return records;
    }

    [Test]
    public void CompactSystemHeaders_AreDecoded()
    {
        Inventory managed = Count("kernel-classic", EtwBackend.Managed);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(managed.Total, Is.EqualTo(5317));
            Assert.That(managed.Of(ThreadGroup), Is.EqualTo(909));
            Assert.That(managed.Warnings, Is.Empty);
        }
    }

    [Test]
    public void UnknownRecordHeader_ReportsWhereTheRestOfTheBufferWasDropped()
    {
        string path = Path.GetTempFileName();
        try
        {
            byte[] bytes = File.ReadAllBytes("./traces/fixtures/kernel-classic.etl");
            const int bufferOffset = 65536 + 72;
            bytes[bufferOffset + 2] = 0x7F; // header type that does not exist

            File.WriteAllBytes(path, bytes);
            List<string> warnings = [];
            long total = 0;
            TraceOpenFailure? failure = TraceProcessor.ProcessFiles([path], EtwBackend.Managed, false, default,
                warnings.Add, (EVENT_RECORD* _, in EtlRecordExtras _) => total++);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(failure, Is.Null);
                Assert.That(total, Is.LessThan(5317));
                Assert.That(warnings, Has.Some.Contains("0x7F").And.Contains("buffer 1").And.Contains("offset 72")
                    .And.Contains("dropped"));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    [Platform("Win")]
    public void ClassicKernelRecords_MatchTheNativeReaderFieldByField()
    {
        List<string> managed = Records("kernel-classic", EtwBackend.Managed);
        List<string> native = Records("kernel-classic", EtwBackend.Native);

        List<string> differences = managed.Zip(native, (m, n) => (m, n)).Where(p => p.m != p.n)
            .Select(p => $"managed {Head(p.m)} / native {Head(p.n)}").Take(5).ToList();

        Assert.That(managed.Count, Is.EqualTo(native.Count));
        Assert.That(differences, Is.Empty);

        static string Head(string record) => record[..Math.Min(record.Length, 140)];
    }
}
