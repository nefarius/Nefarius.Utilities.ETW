namespace Nefarius.Utilities.ETW.Relay.CLI.Tests;

public class BinaryInputTests
{
    [Test]
    public async Task OneByteReads_ReassembleEveryFrame()
    {
        List<byte[]> frames = Frames.Session(Guid.NewGuid(), 3);
        List<byte[]> read = [];

        await foreach (byte[] frame in FrameInput.ReadBinaryAsync(new TinyReadStream(Frames.Concat(frames)),
                           1 << 20, CancellationToken.None))
        {
            read.Add(frame);
        }

        Assert.That(read, Has.Count.EqualTo(frames.Count));
        for (int i = 0; i < frames.Count; i++)
        {
            Assert.That(read[i], Is.EqualTo(frames[i]));
        }
    }

    [Test]
    public void DeclaredLengthAboveTheLimit_IsRejectedBeforeAllocation()
    {
        byte[] frame = Frames.Event(1, payloadLength: 4096);

        RawEtwProtocolException? ex = Assert.ThrowsAsync<RawEtwProtocolException>(async () =>
        {
            await foreach (byte[] _ in FrameInput.ReadBinaryAsync(new MemoryStream(frame), 1024,
                               CancellationToken.None))
            {
            }
        });

        Assert.That(ex!.Error, Is.EqualTo(RawEtwProtocolError.InvalidLength));
    }

    [Test]
    public void TruncatedFrame_Throws()
    {
        byte[] frame = Frames.Event(1);

        Assert.ThrowsAsync<RawEtwProtocolException>(async () =>
        {
            await foreach (byte[] _ in FrameInput.ReadBinaryAsync(new MemoryStream(frame[..^8]), 1 << 20,
                               CancellationToken.None))
            {
            }
        });
    }
}

public class Base64NdjsonInputTests
{
    private static async Task<List<byte[]>> ReadAll(byte[] data, int max = 1 << 20)
    {
        List<byte[]> read = [];
        await foreach (byte[] frame in FrameInput.ReadBase64NdjsonAsync(new TinyReadStream(data), max,
                           CancellationToken.None))
        {
            read.Add(frame);
        }

        return read;
    }

    [Test]
    public async Task CrLfBlankLinesAndMissingFinalNewline_AreAccepted()
    {
        List<byte[]> frames = Frames.Session(Guid.NewGuid(), 2);
        string text = System.Text.Encoding.ASCII.GetString(Frames.Ndjson(frames, "\r\n"))
            .Replace("\r\n", "\r\n\r\n").TrimEnd();

        List<byte[]> read = await ReadAll(System.Text.Encoding.ASCII.GetBytes(text));

        Assert.That(read.Select(Convert.ToBase64String), Is.EqualTo(frames.Select(Convert.ToBase64String)));
    }

    [Test]
    public async Task ByteOrderMark_IsSkipped()
    {
        byte[] frame = Frames.Hello(Guid.NewGuid());
        byte[] data = [0xEF, 0xBB, 0xBF, .. Frames.Ndjson([frame])];

        Assert.That((await ReadAll(data)).Single(), Is.EqualTo(frame));
    }

    [TestCase("not json")]
    [TestCase("[1,2]")]
    [TestCase("{\"frame\":5}")]
    [TestCase("{\"other\":\"AAAA\"}")]
    [TestCase("{\"frame\":\"AAAA\",\"extra\":1}")]
    [TestCase("{\"frame\":\"%%%\"}")]
    public void MalformedLines_AreRejected(string line)
    {
        Assert.ThrowsAsync<InputException>(async () =>
            await ReadAll(System.Text.Encoding.ASCII.GetBytes(line + "\n")));
    }

    [Test]
    public void FrameAboveTheLimit_IsRejected()
    {
        byte[] data = Frames.Ndjson([Frames.Event(1, payloadLength: 4096)]);

        Assert.ThrowsAsync<InputException>(async () => await ReadAll(data, max: 1024));
    }

    [Test]
    public void EndlessLineWithoutNewline_IsRejectedWhileReading()
    {
        // A megabyte of 'a' with no newline must fail at the small limit instead of being buffered.
        byte[] data = new byte[1024 * 1024];
        Array.Fill(data, (byte)'a');

        InputException? ex = Assert.ThrowsAsync<InputException>(async () => await ReadAll(data, max: 1024));
        Assert.That(ex!.Message, Does.Contain("refusing to buffer"));
    }
}

public class SettingsTests
{
    private static readonly KeyValuePair<string, string>[] NoHeaders = [];

    [Test]
    public void LogsPath_IsAppendedOnce()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RelaySettings.BuildEndpoint("http://h:4318", NoHeaders).AbsoluteUri,
                Is.EqualTo("http://h:4318/v1/logs"));
            Assert.That(RelaySettings.BuildEndpoint("http://h:4318/", NoHeaders).AbsoluteUri,
                Is.EqualTo("http://h:4318/v1/logs"));
            Assert.That(RelaySettings.BuildEndpoint("https://h/otlp/v1/logs", NoHeaders).AbsoluteUri,
                Is.EqualTo("https://h/otlp/v1/logs"));
            Assert.That(RelaySettings.BuildEndpoint("https://h:4318/ingest?keep=1", NoHeaders).AbsoluteUri,
                Is.EqualTo("https://h:4318/ingest/v1/logs?keep=1"));
        });
    }

    [TestCase("Cookie")]
    [TestCase("Authorization")]
    [TestCase("proxy-authorization")]
    [TestCase("X-Api-Key")]
    [TestCase("x-apikey")]
    [TestCase("X-Auth-Token")]
    [TestCase("client-secret")]
    [TestCase("my-password")]
    public void CredentialHeaders_AreRefusedOverHttp(string name)
    {
        KeyValuePair<string, string>[] headers = [new(name, "v")];

        Assert.Throws<UsageException>(() => RelaySettings.BuildEndpoint("http://h", headers));
        Assert.DoesNotThrow(() => RelaySettings.BuildEndpoint("https://h", headers));
    }

    [Test]
    public void PlainHeadersAreFineOverHttp() =>
        Assert.DoesNotThrow(() => RelaySettings.BuildEndpoint("http://h", [new("X-Tenant", "a")]));

    [TestCase("ftp://h")]
    [TestCase("not a url")]
    [TestCase("http://user:pw@h")]
    [TestCase("http://h:4318/?api_key=secret")]
    public void BadEndpoints_AreRefused(string endpoint) =>
        Assert.Throws<UsageException>(() => RelaySettings.BuildEndpoint(endpoint, NoHeaders));

    [Test]
    public void Pairs_AreSplitOnTheFirstEquals()
    {
        List<KeyValuePair<string, string>> pairs = RelaySettings.ParsePairs(["a=b=c", "d="], "--resource");

        Assert.That(pairs, Is.EqualTo(new[] { new KeyValuePair<string, string>("a", "b=c"), new("d", "") }));
    }

    [TestCase("novalue")]
    [TestCase("=x")]
    public void MalformedPairs_AreRefused(string pair) =>
        Assert.Throws<UsageException>(() => RelaySettings.ParsePairs([pair], "--header"));

    [Test]
    public void DuplicatePairs_AreRefused() =>
        Assert.Throws<UsageException>(() => RelaySettings.ParsePairs(["a=1", "A=2"], "--header"));

    [Test]
    public void ControlCharactersInHeaders_AreRefused() =>
        Assert.Throws<UsageException>(() => RelaySettings.ParsePairs(["a=1\r\nInjected: yes"], "--header"));
}
