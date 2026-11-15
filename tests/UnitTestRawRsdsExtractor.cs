using System.Text;

using Nefarius.Utilities.ETW.Deserializer.CustomParsers;
using Nefarius.Utilities.ETW.Deserializer.WPP;
using Nefarius.Utilities.ETW.Deserializer.WPP.TMF;
using Nefarius.Utilities.ETW.Events;
using Nefarius.Utilities.ETW.Relay;

namespace Nefarius.Utilities.ETW.Tests;

/// <summary>
///     Tests for <see cref="RawRsdsExtractor" /> and <see cref="DecodingContext.TryExtend" />.
/// </summary>
public class RawRsdsExtractorTests
{
    private static readonly Guid Sig = Guid.Parse("779e56ef-8d24-4145-a64a-3aee304b9de9");

    private static byte[] Name(string name) => [.. Encoding.Latin1.GetBytes(name), 0];

    private static RawEtwRecord ImageId(string name, uint age = 3, bool terminate = true)
    {
        List<byte> payload = [];
        payload.AddRange(BitConverter.GetBytes(0x7ff600000000UL));
        payload.AddRange(BitConverter.GetBytes(1234u));
        payload.AddRange(Sig.ToByteArray());
        payload.AddRange(BitConverter.GetBytes(age));
        payload.AddRange(terminate ? Name(name) : Encoding.Latin1.GetBytes(name));
        return new RawEtwRecord
        {
            ProviderId = CustomParserGuids.KernelTraceControlImageIdGuid, Opcode = 36, Payload = payload.ToArray()
        };
    }

    private static RawEtwRecord Legacy(string name, uint age = 3)
    {
        List<byte> payload = [];
        payload.AddRange(Sig.ToByteArray());
        payload.AddRange(BitConverter.GetBytes(age));
        payload.AddRange(Name(name));
        return new RawEtwRecord
        {
            ProviderId = CustomParserGuids.EventTraceGuid, Opcode = 64, Payload = payload.ToArray()
        };
    }

    [Test]
    [Category("Unit")]
    public void ImageId_Layout_IsExtracted()
    {
        Assert.That(RawRsdsExtractor.TryExtract(ImageId(@"C:\build\HidHide.pdb"), out PdbMetaData pdb), Is.True);
        Assert.That(pdb.Guid, Is.EqualTo(Sig));
        Assert.That(pdb.Age, Is.EqualTo(3));
        Assert.That(pdb.PdbName, Is.EqualTo("HidHide.pdb"));
    }

    [Test]
    [Category("Unit")]
    public void Legacy_Layout_IsExtracted()
    {
        Assert.That(RawRsdsExtractor.TryExtract(Legacy("/src/foo.pdb", 7), out PdbMetaData pdb), Is.True);
        Assert.That(pdb.Guid, Is.EqualTo(Sig));
        Assert.That(pdb.Age, Is.EqualTo(7));
        Assert.That(pdb.PdbName, Is.EqualTo("foo.pdb"));
    }

    [Test]
    [Category("Unit")]
    public void OtherEvents_AreIgnored()
    {
        RawEtwRecord wrongOpcode = ImageId("a.pdb");
        wrongOpcode.Opcode = 0;
        RawEtwRecord wrongProvider = Legacy("a.pdb");
        wrongProvider.ProviderId = Guid.NewGuid();

        Assert.That(RawRsdsExtractor.TryExtract(wrongOpcode, out _), Is.False);
        Assert.That(RawRsdsExtractor.TryExtract(wrongProvider, out _), Is.False);
        Assert.That(RawRsdsExtractor.TryExtract(null, out _), Is.False);
    }

    [Test]
    [Category("Unit")]
    public void Malformed_Records_AreRejectedWithoutThrowing()
    {
        RawEtwRecord truncated = ImageId("a.pdb");
        truncated.Payload = truncated.Payload[..20];
        RawEtwRecord emptyName = ImageId("");
        RawEtwRecord bigAge = ImageId("a.pdb", 0x80000000u);
        RawEtwRecord unterminated = ImageId("a.pdb", terminate: false);
        RawEtwRecord dirOnly = ImageId(@"C:\dir\");
        RawEtwRecord dotDot = ImageId("..");
        RawEtwRecord nonAscii = ImageId("caf\u00e9.pdb");
        RawEtwRecord queryChars = ImageId("a?b#.pdb");
        RawEtwRecord empty = new() { ProviderId = CustomParserGuids.EventTraceGuid, Opcode = 64 };

        foreach (RawEtwRecord record in new[]
                 {
                     truncated, emptyName, bigAge, unterminated, dirOnly, dotDot, nonAscii, queryChars, empty
                 })
        {
            Assert.That(RawRsdsExtractor.TryExtract(record, out _), Is.False);
        }
    }

    [Test]
    [Category("Unit")]
    public void Identity_DeduplicatesAcrossLayouts()
    {
        RawRsdsExtractor.TryExtract(ImageId(@"C:\x\A.PDB"), out PdbMetaData first);
        RawRsdsExtractor.TryExtract(Legacy("a.pdb"), out PdbMetaData second);

        Assert.That(new HashSet<PdbMetaData> { first, second }, Has.Count.EqualTo(1));
    }

    private sealed class StubSource : DecodingContextType
    {
        public StubSource(params TraceMessageFormat[] formats) => TraceMessageFormats = formats;
    }

    private static TraceMessageFormat Format(Guid guid, int id, string text) => new()
    {
        MessageGuid = guid,
        Provider = "p",
        FileName = "f.c",
        Opcode = "op",
        Id = id,
        MessageFormat = text,
        Level = "l",
        Flags = "f",
        Function = "fn"
    };

    [Test]
    [Category("Unit")]
    public void TryExtend_MakesFormatsVisibleOnSameInstance()
    {
        Guid guid = Guid.NewGuid();
        DecodingContext context = new(new StubSource());

        Assert.That(context.GetTraceMessageFormatFor(guid, 1), Is.Null);
        Assert.That(context.TryExtend(new StubSource(Format(guid, 1, "x"))), Is.True);
        Assert.That(context.GetTraceMessageFormatFor(guid, 1)?.MessageFormat, Is.EqualTo("x"));
    }

    [Test]
    [Category("Unit")]
    public void TryExtend_ConflictingKey_IsRejectedAndKeepsSnapshot()
    {
        Guid guid = Guid.NewGuid();
        Guid other = Guid.NewGuid();
        DecodingContext context = new(new StubSource(Format(guid, 1, "original")));

        bool added = context.TryExtend(
            new StubSource(Format(other, 2, "fresh")),
            new StubSource(Format(guid, 1, "conflict")));

        Assert.That(added, Is.False);
        Assert.That(context.GetTraceMessageFormatFor(guid, 1)?.MessageFormat, Is.EqualTo("original"));
        Assert.That(context.GetTraceMessageFormatFor(other, 2), Is.Null);
    }

    [Test]
    [Category("Unit")]
    public void TryExtend_DifferentParameterSchema_IsAConflict()
    {
        Guid guid = Guid.NewGuid();
        TraceMessageFormat original = Format(guid, 1, "%1");
        original.FunctionParameters =
        [
            new FunctionParameter { Expression = "n", Type = ItemType.ItemLong, Index = 1 }
        ];
        TraceMessageFormat incoming = Format(guid, 1, "%1");
        incoming.FunctionParameters =
        [
            new FunctionParameter { Expression = "n", Type = ItemType.ItemString, Index = 1 }
        ];
        DecodingContext context = new(new StubSource(original));

        Assert.That(context.TryExtend(new StubSource(incoming)), Is.False);
        Assert.That(context.GetTraceMessageFormatFor(guid, 1)!.FunctionParameters.Single().Type,
            Is.EqualTo(ItemType.ItemLong));
    }

    [Test]
    [Category("Unit")]
    public void TryExtend_IsSafeUnderConcurrentLookups()
    {
        DecodingContext context = new(new StubSource());
        Guid[] guids = Enumerable.Range(0, 50).Select(_ => Guid.NewGuid()).ToArray();
        using CancellationTokenSource stop = new();

        Task reader = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                foreach (Guid g in guids)
                {
                    _ = context.GetTraceMessageFormatFor(g, 1);
                }
            }
        });

        Parallel.ForEach(guids, g => Assert.That(context.TryExtend(new StubSource(Format(g, 1, "v"))), Is.True));
        stop.Cancel();
        reader.Wait();

        Assert.That(guids.All(g => context.GetTraceMessageFormatFor(g, 1) is not null), Is.True);
    }
}
