using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;

using Google.Protobuf;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NUnit.Framework;

using Nefarius.Utilities.ETW.Etl;
using Nefarius.Utilities.ETW.OpenTelemetry;
using Nefarius.Utilities.ETW.OtlpGateway;
using Nefarius.Utilities.ETW.Relay;

using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Logs.V1;

using Windows.Win32.System.Diagnostics.Etw;

namespace Nefarius.Utilities.ETW.OpenTelemetry.Tests;

/// <summary>
///     Builders for relay frames shared by the adapter and gateway tests.
/// </summary>
internal static class Frames
{
    public static readonly Guid Provider = new("11111111-2222-3333-4444-555555555555");

    public static byte[] Hello(Guid session, ulong sequence = 0) => RawEtwFrameCodec.Encode(new RawEtwFrame
    {
        Type = RawEtwFrameType.Hello,
        Sequence = sequence,
        Hello = new RawEtwHello { SessionId = session, MaxFrameLength = 1 << 20, Producer = "unit-test" }
    });

    public static byte[] End(ulong sequence) =>
        RawEtwFrameCodec.Encode(new RawEtwFrame { Type = RawEtwFrameType.End, Sequence = sequence });

    public static byte[] Event(ulong sequence, uint count = 1, string text = "hello") =>
        RawEtwFrameCodec.Encode(new RawEtwFrame
        {
            Type = RawEtwFrameType.Event,
            Sequence = sequence,
            Record = TraceLoggingRecord(count, text)
        });

    public static RawEtwRecord TraceLoggingRecord(uint count, string text)
    {
        RawEtwRecord record = new()
        {
            Flags = 0x40,
            ThreadId = 5,
            ProcessId = 6,
            TimeStamp = 133_500_000_000_000_000,
            ProviderId = Provider,
            Level = 3,
            Payload = [.. BitConverter.GetBytes(count), .. Encoding.Unicode.GetBytes(text + "\0")]
        };
        record.ExtendedData.Add(new RawEtwExtendedData
        {
            ExtType = 11,
            Data = Schema("MyEvent", Field("Count", 8), Field("Name", 1))
        });
        return record;
    }

    private static byte[] Schema(string name, params byte[][] fields)
    {
        List<byte> body = [0];
        body.AddRange(Encoding.UTF8.GetBytes(name));
        body.Add(0);
        foreach (byte[] field in fields)
        {
            body.AddRange(field);
        }

        return [.. BitConverter.GetBytes((ushort)(body.Count + 2)), .. body];
    }

    private static byte[] Field(string name, byte inType) => [.. Encoding.UTF8.GetBytes(name), 0, inType];

    /// <summary>Raw records of an ETL file, as a capture client would relay them (no symbols involved).</summary>
    public static unsafe List<byte[]> FromEtl(string path, Guid session)
    {
        List<byte[]> frames = [Hello(session)];
        ulong sequence = 1;

        TraceOpenFailure? failure = TraceProcessor.ProcessFiles([path], EtwBackend.Managed, false, default, null,
            (EVENT_RECORD* record, in EtlRecordExtras extras) =>
                frames.Add(RawEtwFrameCodec.Encode(new RawEtwFrame
                {
                    Type = RawEtwFrameType.Event,
                    Sequence = sequence++,
                    Record = RelayRecordBuffer.ToRelayRecord(record, extras)
                })));
        Assert.That(failure, Is.Null);

        frames.Add(End(sequence));
        return frames;
    }

    public static ExportLogsServiceRequest Request(Guid session, params byte[][] frames) =>
        RelayLogRecords.CreateRequest(session, frames.Select(f => (ReadOnlyMemory<byte>)f));
}

/// <summary>
///     A sink that records what the gateway exports and can be told to fail or to block.
/// </summary>
internal sealed class CapturingSink : IDecodedLogSink
{
    private readonly object _gate = new();
    private readonly List<ExportLogsServiceRequest> _batches = [];
    private int _failuresRemaining;
    private bool _failPermanently;
    private TaskCompletionSource? _gateSignal;

    public int Attempts { get; private set; }

    public TaskCompletionSource Started { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IReadOnlyList<LogRecord> Records
    {
        get
        {
            lock (_gate)
            {
                return _batches.SelectMany(b => b.ResourceLogs).SelectMany(r => r.ScopeLogs)
                    .SelectMany(s => s.LogRecords).ToList();
            }
        }
    }

    public void FailRetryable(int times) => _failuresRemaining = times;

    public void FailPermanentlyOnce() => _failPermanently = true;

    public void Block() => _gateSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Unblock() => _gateSignal?.TrySetResult();

    public async Task SendAsync(DecodedBatch batch, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Attempts++;
        }

        Started.TrySetResult();

        if (_gateSignal is { } signal)
        {
            await signal.Task.WaitAsync(cancellationToken);
        }

        if (_failPermanently)
        {
            _failPermanently = false;
            throw new InvalidOperationException("rejected");
        }

        if (_failuresRemaining > 0)
        {
            _failuresRemaining--;
            throw new SinkRetryableException("try again");
        }

        lock (_gate)
        {
            _batches.Add(batch.Request);
        }
    }

    public async Task WaitForRecordsAsync(int count)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        while (Records.Count < count)
        {
            await Task.Delay(10, timeout.Token);
        }
    }
}

/// <summary>Authenticates requests that carry an <c>X-Test-User</c> header.</summary>
internal sealed class HeaderAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public new const string Scheme = "TestHeader";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-User", out Microsoft.Extensions.Primitives.StringValues user))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        ClaimsIdentity identity = new([new Claim(ClaimTypes.Name, user.ToString())], Scheme);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme)));
    }
}

/// <summary>
///     Hosts the gateway in-process, composed the way an adopter would: their authentication, then the gateway's
///     endpoints.
/// </summary>
internal sealed class TestGateway : IAsyncDisposable
{
    private WebApplication _app = null!;

    public HttpClient Client { get; private set; } = null!;

    public CapturingSink Sink { get; } = new();

    public TestServer Server => _app.GetTestServer();

    public static async Task<TestGateway> StartAsync(
        Dictionary<string, string?>? settings = null,
        bool authenticated = false)
    {
        TestGateway gateway = new();

        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(settings ?? []);

        builder.Services.AddEtwOtlpGateway(builder.Configuration);
        builder.Services.AddSingleton<IDecodedLogSink>(gateway.Sink);

        if (authenticated)
        {
            builder.Services.AddAuthentication(HeaderAuthHandler.Scheme)
                .AddScheme<AuthenticationSchemeOptions, HeaderAuthHandler>(HeaderAuthHandler.Scheme, null);
            builder.Services.AddAuthorization();
        }

        gateway._app = builder.Build();

        if (authenticated)
        {
            gateway._app.UseAuthentication();
            gateway._app.UseAuthorization();
            gateway._app.MapEtwOtlpGateway().RequireAuthorization();
        }
        else
        {
            gateway._app.MapEtwOtlpGateway();
        }

        gateway._app.MapHealthChecks("/healthz");

        await gateway._app.StartAsync();
        gateway.Client = gateway._app.GetTestClient();
        return gateway;
    }

    public Task StopAsync() => _app.StopAsync();

    public async Task<(HttpResponseMessage Response, ExportLogsServiceResponse? Body)> PostAsync(
        ExportLogsServiceRequest request, string? user = null, bool gzip = false)
    {
        byte[] bytes = request.ToByteArray();
        HttpContent content = gzip ? Gzip(bytes) : new ByteArrayContent(bytes);
        content.Headers.ContentType = new("application/x-protobuf");

        using HttpRequestMessage message = new(HttpMethod.Post, "/v1/logs") { Content = content };
        if (user is not null)
        {
            message.Headers.Add("X-Test-User", user);
        }

        HttpResponseMessage response = await Client.SendAsync(message);
        ExportLogsServiceResponse? body = null;
        if (response.IsSuccessStatusCode)
        {
            body = ExportLogsServiceResponse.Parser.ParseFrom(await response.Content.ReadAsByteArrayAsync());
        }

        return (response, body);
    }

    private static ByteArrayContent Gzip(byte[] bytes)
    {
        using MemoryStream output = new();
        using (System.IO.Compression.GZipStream gz = new(output, System.IO.Compression.CompressionMode.Compress, true))
        {
            gz.Write(bytes);
        }

        ByteArrayContent content = new(output.ToArray());
        content.Headers.ContentEncoding.Add("gzip");
        return content;
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.DisposeAsync();
    }
}
