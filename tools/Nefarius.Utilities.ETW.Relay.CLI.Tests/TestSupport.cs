using System.Net;
using System.Threading.Channels;

using Google.Protobuf;

using Nefarius.Utilities.ETW.OpenTelemetry;
using Nefarius.Utilities.ETW.Relay;

using OpenTelemetry.Proto.Collector.Logs.V1;

namespace Nefarius.Utilities.ETW.Relay.CLI.Tests;

internal static class Frames
{
    public static byte[] Hello(Guid session, ulong sequence = 0) => RawEtwFrameCodec.Encode(new RawEtwFrame
    {
        Type = RawEtwFrameType.Hello,
        Sequence = sequence,
        Hello = new RawEtwHello { SessionId = session, MaxFrameLength = 1 << 20, Producer = "tests" }
    });

    public static byte[] Event(ulong sequence, int payloadLength = 8) => RawEtwFrameCodec.Encode(new RawEtwFrame
    {
        Type = RawEtwFrameType.Event,
        Sequence = sequence,
        Record = new RawEtwRecord
        {
            Flags = 0x40,
            ThreadId = 1,
            ProcessId = 2,
            TimeStamp = 133_000_000_000_000_000,
            ProviderId = Guid.Parse("6f3e0f19-6e4c-4a52-8a6b-9a1d0c2b7e11"),
            Payload = new byte[payloadLength]
        }
    });

    public static byte[] End(ulong sequence) =>
        RawEtwFrameCodec.Encode(new RawEtwFrame { Type = RawEtwFrameType.End, Sequence = sequence });

    /// <summary>A whole session: hello, <paramref name="events" /> events, end.</summary>
    public static List<byte[]> Session(Guid session, int events, bool end = true)
    {
        List<byte[]> frames = [Hello(session)];
        for (int i = 0; i < events; i++)
        {
            frames.Add(Event((ulong)(i + 1)));
        }

        if (end)
        {
            frames.Add(End((ulong)(events + 1)));
        }

        return frames;
    }

    public static byte[] Concat(IEnumerable<byte[]> frames) => frames.SelectMany(f => f).ToArray();

    public static byte[] Ndjson(IEnumerable<byte[]> frames, string newline = "\n") =>
        System.Text.Encoding.ASCII.GetBytes(string.Concat(frames.Select(f =>
            "{\"frame\":\"" + Convert.ToBase64String(f) + "\"}" + newline)));
}

/// <summary>A captured request.</summary>
internal sealed record CapturedRequest(
    Uri Uri,
    byte[] Body,
    string? ContentType,
    string? ContentEncoding,
    Dictionary<string, string> Headers)
{
    /// <summary>The OTLP request, undoing gzip when it was applied.</summary>
    public ExportLogsServiceRequest Parse()
    {
        byte[] raw = Body;
        if (ContentEncoding == "gzip")
        {
            using MemoryStream source = new(Body);
            using System.IO.Compression.GZipStream gzip = new(source, System.IO.Compression.CompressionMode.Decompress);
            using MemoryStream plain = new();
            gzip.CopyTo(plain);
            raw = plain.ToArray();
        }

        return ExportLogsServiceRequest.Parser.ParseFrom(raw);
    }

    public Guid SessionId
    {
        get
        {
            Assert.That(RelayLogRecords.TryGetSessionId(Parse().ResourceLogs.Single().Resource, out Guid id), Is.True);
            return id;
        }
    }

    public List<byte[]> FrameBytes() => Parse().ResourceLogs.Single().ScopeLogs.Single().LogRecords
        .Select(r =>
        {
            Assert.That(RelayLogRecords.TryGetFrame(r, out ReadOnlyMemory<byte> frame), Is.True);
            return frame.ToArray();
        }).ToList();
}

internal sealed class RecordingHandler(Func<int, CapturedRequest, Task<HttpResponseMessage>>? responder = null)
    : HttpMessageHandler
{
    private readonly List<CapturedRequest> _requests = [];

    public IReadOnlyList<CapturedRequest> Requests
    {
        get
        {
            lock (_requests)
            {
                return _requests.ToList();
            }
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        byte[] body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
        Dictionary<string, string> headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value),
            StringComparer.OrdinalIgnoreCase);
        CapturedRequest captured = new(request.RequestUri!, body,
            request.Content.Headers.ContentType?.MediaType,
            request.Content.Headers.ContentEncoding.FirstOrDefault(), headers);

        int index;
        lock (_requests)
        {
            index = _requests.Count;
            _requests.Add(captured);
        }

        return responder is null ? Ok() : await responder(index, captured);
    }

    public static HttpResponseMessage Ok() => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(new ExportLogsServiceResponse().ToByteArray())
    };

    public static HttpResponseMessage Rejected(string message) => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(new ExportLogsServiceResponse
        {
            PartialSuccess = new ExportLogsPartialSuccess { RejectedLogRecords = 1, ErrorMessage = message }
        }.ToByteArray())
    };
}

/// <summary>Never answers the first request, so only the client timeout can end it.</summary>
internal sealed class HangFirstHandler : HttpMessageHandler
{
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref _calls) == 1)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        return RecordingHandler.Ok();
    }
}

/// <summary>Hands out data one byte per read to exercise reassembly.</summary>
internal sealed class TinyReadStream(byte[] data) : Stream
{
    private int _position;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => data.Length;

    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_position >= data.Length || count == 0)
        {
            return 0;
        }

        buffer[offset] = data[_position++];
        return 1;
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>A stream that stays open until <see cref="Complete" /> is called, like a pipe.</summary>
internal sealed class PipeLikeStream : Stream
{
    private readonly Channel<byte[]> _channel = Channel.CreateUnbounded<byte[]>();
    private byte[] _current = [];
    private int _offset;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public void Push(byte[] bytes) => _channel.Writer.TryWrite(bytes);

    public void Complete() => _channel.Writer.TryComplete();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        while (_offset >= _current.Length)
        {
            if (!await _channel.Reader.WaitToReadAsync(cancellationToken))
            {
                return 0;
            }

            _channel.Reader.TryRead(out byte[]? next);
            _current = next ?? [];
            _offset = 0;
        }

        int n = Math.Min(buffer.Length, _current.Length - _offset);
        _current.AsMemory(_offset, n).CopyTo(buffer);
        _offset += n;
        return n;
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

internal static class Run
{
    public static RelaySettings Settings(Action<SettingsBuilder>? configure = null)
    {
        SettingsBuilder b = new();
        configure?.Invoke(b);
        return new RelaySettings
        {
            Endpoint = new Uri("http://gateway.test/v1/logs"),
            InputFormat = b.InputFormat,
            Headers = b.Headers,
            Resources = b.Resources,
            Gzip = b.Gzip,
            BatchFrames = b.BatchFrames,
            BatchBytes = b.BatchBytes,
            FlushInterval = b.FlushInterval,
            QueueFrames = b.QueueFrames,
            RequestTimeout = b.RequestTimeout,
            MaxRetries = b.MaxRetries,
            RetryBaseDelay = TimeSpan.FromMilliseconds(1),
            RetryMaxDelay = TimeSpan.FromMilliseconds(5),
            RetryAfterCap = b.RetryAfterCap,
            MaxFrameBytes = b.MaxFrameBytes
        };
    }

    /// <summary>Runs the pipeline over <paramref name="input" />; delays are recorded, not waited.</summary>
    public static async Task<(int Code, RelayRunner Runner, List<TimeSpan> Delays)> RunAsync(RelaySettings settings,
        Stream input, HttpMessageHandler handler, CancellationToken abort = default)
    {
        List<TimeSpan> delays = [];
        using OtlpSender sender = new(settings, TextWriter.Null, handler, (span, _) =>
        {
            lock (delays)
            {
                delays.Add(span);
            }

            return Task.CompletedTask;
        });
        RelayRunner runner = new(settings, sender, TextWriter.Null);
        int code = await runner.RunAsync(input, abort);
        return (code, runner, delays);
    }
}

internal sealed class SettingsBuilder
{
    public InputFormat InputFormat { get; set; } = InputFormat.Binary;
    public List<KeyValuePair<string, string>> Headers { get; set; } = [];
    public List<KeyValuePair<string, string>> Resources { get; set; } = [];
    public bool Gzip { get; set; }
    public int BatchFrames { get; set; } = 64;
    public int BatchBytes { get; set; } = 1024 * 1024;
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromHours(1);
    public int QueueFrames { get; set; } = 1024;
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public int MaxRetries { get; set; } = 3;
    public TimeSpan RetryAfterCap { get; set; } = TimeSpan.FromSeconds(60);
    public int MaxFrameBytes { get; set; } = 1024 * 1024;
}
