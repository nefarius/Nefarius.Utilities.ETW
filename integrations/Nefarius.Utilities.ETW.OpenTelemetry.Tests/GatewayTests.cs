using System.Net;

using Google.Protobuf;

using Grpc.Core;
using Grpc.Net.Client;

using Nefarius.Utilities.ETW.OpenTelemetry;
using Nefarius.Utilities.ETW.OtlpGateway;
using Nefarius.Utilities.ETW.Relay;

using NUnit.Framework;

using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;

namespace Nefarius.Utilities.ETW.OpenTelemetry.Tests;

/// <summary>
///     End-to-end tests of the decoding gateway: OTLP in, decoded standard OTLP Logs out.
/// </summary>
public class GatewayTests
{
    private static readonly Guid Session = new("0f8fad5b-d9cb-469f-a165-70867728950e");

    private static string? Attribute(LogRecord record, string key) =>
        record.Attributes.FirstOrDefault(a => a.Key == key)?.Value?.StringValue;

    private static AnyValue? BodyMember(LogRecord record, string name) =>
        record.Body.KvlistValue.Values.FirstOrDefault(v => v.Key == name)?.Value;

    // ---------------------------------------------------------------------------------------------------------------
    // Adapter
    // ---------------------------------------------------------------------------------------------------------------

    [Test]
    public void Adapter_FrameSurvivesTheOtlpEnvelopeRoundTrip()
    {
        byte[] frame = Frames.Event(1);
        ExportLogsServiceRequest request =
            ExportLogsServiceRequest.Parser.ParseFrom(Frames.Request(Session, frame).ToByteArray());

        LogRecord record = request.ResourceLogs.Single().ScopeLogs.Single().LogRecords.Single();
        Assert.That(RelayLogRecords.TryGetFrame(record, out ReadOnlyMemory<byte> back), Is.True);
        Assert.That(back.ToArray(), Is.EqualTo(frame));
        Assert.That(RelayLogRecords.TryGetSessionId(request.ResourceLogs.Single().Resource, out Guid id), Is.True);
        Assert.That(id, Is.EqualTo(Session));
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Decoding
    // ---------------------------------------------------------------------------------------------------------------

    [Test]
    public async Task Http_DecodesTraceLoggingIntoStructuredLogRecords()
    {
        await using TestGateway gateway = await TestGateway.StartAsync();

        (HttpResponseMessage response, ExportLogsServiceResponse? body) = await gateway.PostAsync(
            Frames.Request(Session, Frames.Hello(Session), Frames.Event(1, 42, "hello")));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body!.PartialSuccess, Is.Null);

        await gateway.Sink.WaitForRecordsAsync(1);
        LogRecord record = gateway.Sink.Records.Single();

        Assert.That(record.EventName, Is.EqualTo("MyEvent"));
        Assert.That(record.Body.ValueCase, Is.EqualTo(AnyValue.ValueOneofCase.KvlistValue));
        Assert.That(BodyMember(record, "Count")!.IntValue, Is.EqualTo(42));
        Assert.That(BodyMember(record, "Name")!.StringValue, Is.EqualTo("hello"));
        Assert.That(record.Attributes.Any(a => a.Key == EtwOtlpAttributes.ProviderId), Is.True);
    }

    [Test]
    public async Task Http_AcceptsGzipBodies()
    {
        await using TestGateway gateway = await TestGateway.StartAsync();

        (HttpResponseMessage response, _) = await gateway.PostAsync(
            Frames.Request(Session, Frames.Hello(Session), Frames.Event(1)), gzip: true);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        await gateway.Sink.WaitForRecordsAsync(1);
    }

    [Test]
    public async Task Http_RejectsMalformedRequests()
    {
        await using TestGateway gateway = await TestGateway.StartAsync(new()
        {
            ["Gateway:Limits:MaxRequestBodyBytes"] = "2048"
        });

        using (HttpRequestMessage wrongType = new(HttpMethod.Post, "/v1/logs") { Content = new StringContent("{}") })
        {
            Assert.That((await gateway.Client.SendAsync(wrongType)).StatusCode,
                Is.EqualTo(HttpStatusCode.UnsupportedMediaType));
        }

        using (ByteArrayContent garbage = new([0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x01]))
        {
            garbage.Headers.ContentType = new("application/x-protobuf");
            Assert.That((await gateway.Client.PostAsync("/v1/logs", garbage)).StatusCode,
                Is.EqualTo(HttpStatusCode.BadRequest));
        }

        using (ByteArrayContent huge = new(new byte[4096]))
        {
            huge.Headers.ContentType = new("application/x-protobuf");
            Assert.That((await gateway.Client.PostAsync("/v1/logs", huge)).StatusCode,
                Is.EqualTo(HttpStatusCode.RequestEntityTooLarge));
        }
    }

    [Test]
    public async Task Health_Responds()
    {
        await using TestGateway gateway = await TestGateway.StartAsync();
        Assert.That((await gateway.Client.GetAsync("/healthz")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task Grpc_DecodesLikeHttp()
    {
        await using TestGateway gateway = await TestGateway.StartAsync();

        using GrpcChannel channel = GrpcChannel.ForAddress(gateway.Server.BaseAddress,
            new GrpcChannelOptions { HttpHandler = gateway.Server.CreateHandler() });
        LogsService.LogsServiceClient client = new(channel);

        ExportLogsServiceResponse response =
            await client.ExportAsync(Frames.Request(Session, Frames.Hello(Session), Frames.Event(1, 7)));

        Assert.That(response.PartialSuccess, Is.Null);
        await gateway.Sink.WaitForRecordsAsync(1);
        Assert.That(BodyMember(gateway.Sink.Records.Single(), "Count")!.IntValue, Is.EqualTo(7));
    }

    // ---------------------------------------------------------------------------------------------------------------
    // WPP: assets exist only on the gateway
    // ---------------------------------------------------------------------------------------------------------------

    [Test]
    [Category("EndToEnd")]
    public async Task Wpp_IsFormattedFromServerSideSymbols()
    {
        const string trace = "./traces/BthPS3_0.etl";
        if (!File.Exists(trace))
        {
            Assert.Ignore($"{trace} is not available.");
        }

        // The frames come straight from the ETL file: raw records only, no PDB/TMF information.
        List<byte[]> frames = Frames.FromEtl(trace, Session);

        await using TestGateway gateway = await TestGateway.StartAsync(new()
        {
            ["Gateway:Wpp:PdbDirectories:0"] = Path.GetFullPath("./symbols")
        });

        // Larger streams are posted in batches, as a client would.
        foreach (byte[][] batch in frames.Chunk(200))
        {
            (_, ExportLogsServiceResponse? body) = await gateway.PostAsync(Frames.Request(Session, batch));
            Assert.That(body!.PartialSuccess, Is.Null);
        }

        await gateway.Sink.WaitForRecordsAsync(DecodedEventCount(frames));
        string?[] bodies = gateway.Sink.Records.Where(r => r.EventName == "WPP").Select(r => r.Body.StringValue)
            .ToArray();

        Assert.That(bodies, Is.Not.Empty);
        Assert.That(bodies.Any(b => !string.IsNullOrEmpty(b) && !b.StartsWith("GUID=")), Is.True,
            "At least one message must be formatted from the gateway's symbols.");
    }

    [Test]
    [Category("EndToEnd")]
    public async Task Wpp_WithoutAssetsOnlyYieldsPlaceholders()
    {
        const string trace = "./traces/BthPS3_0.etl";
        if (!File.Exists(trace))
        {
            Assert.Ignore($"{trace} is not available.");
        }

        List<byte[]> frames = Frames.FromEtl(trace, Session);
        await using TestGateway gateway = await TestGateway.StartAsync();

        foreach (byte[][] batch in frames.Chunk(200))
        {
            await gateway.PostAsync(Frames.Request(Session, batch));
        }

        await gateway.Sink.WaitForRecordsAsync(DecodedEventCount(frames));
        LogRecord[] wpp = gateway.Sink.Records.Where(r => r.EventName == "WPP").ToArray();
        Assert.That(wpp, Is.Not.Empty);
        Assert.That(wpp.Select(r => r.Body.StringValue ?? string.Empty), Has.All.StartsWith("GUID="));
    }

    [Test]
    public async Task Spans_GroupASessionIntoOneTraceWithStableIds()
    {
        await using TestGateway gateway = await TestGateway.StartAsync();
        await gateway.PostAsync(Frames.Request(Session, Frames.Hello(Session), Frames.Event(1, 5), Frames.Event(2, 6)));
        await gateway.Sink.WaitForRecordsAsync(2);

        ExportLogsServiceRequest decoded = new();
        ResourceLogs resource = new()
        {
            Resource = RelayLogRecords.CreateRequest(Session, []).ResourceLogs.Single().Resource
        };
        ScopeLogs scope = new();
        scope.LogRecords.AddRange(gateway.Sink.Records);
        resource.ScopeLogs.Add(scope);
        decoded.ResourceLogs.Add(resource);

        const string tenant = "anonymous";
        var first = EtwSpanMapper.ToTraceRequest(decoded, tenant).ResourceSpans.Single().ScopeSpans.Single().Spans;
        var again = EtwSpanMapper.ToTraceRequest(decoded, tenant).ResourceSpans.Single().ScopeSpans.Single().Spans;

        // The trace id is a hash of the tenant and the session, not the session GUID itself. Span ids are
        // EtwSpanMapper.SpanId(traceId, sequence): stable inside that trace, different for each sequence.
        byte[] traceId = EtwSpanMapper.TraceId(tenant, Session);
        Assert.That(first, Has.Count.EqualTo(2));
        Assert.That(first[0].TraceId.ToByteArray(), Is.EqualTo(traceId));
        Assert.That(first[1].TraceId.ToByteArray(), Is.EqualTo(traceId));
        Assert.That(first[0].SpanId.ToByteArray(), Is.EqualTo(EtwSpanMapper.SpanId(traceId, 1)));
        Assert.That(first[1].SpanId.ToByteArray(), Is.EqualTo(EtwSpanMapper.SpanId(traceId, 2)));
        Assert.That(first[0].SpanId, Is.Not.EqualTo(first[1].SpanId));
        Assert.That(first.Select(s => s.SpanId), Is.EqualTo(again.Select(s => s.SpanId)));
        Assert.That(first[0].Name, Is.EqualTo("MyEvent"));
        Assert.That(first[0].Attributes.Single(a => a.Key == "etw.property.Count").Value.IntValue, Is.EqualTo(5));

        // A second tenant using the same session GUID does not share the trace, so its span ids differ too.
        byte[] otherTrace = EtwSpanMapper.TraceId("bob", Session);
        var other = EtwSpanMapper.ToTraceRequest(decoded, "bob").ResourceSpans.Single().ScopeSpans.Single().Spans;
        Assert.That(other[0].TraceId.ToByteArray(), Is.EqualTo(otherTrace));
        Assert.That(other[0].TraceId, Is.Not.EqualTo(first[0].TraceId));
        Assert.That(other[0].SpanId.ToByteArray(), Is.EqualTo(EtwSpanMapper.SpanId(otherTrace, 1)));
        Assert.That(other[0].SpanId, Is.Not.EqualTo(first[0].SpanId));
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Sequencing
    // ---------------------------------------------------------------------------------------------------------------

    [Test]
    public async Task Duplicates_AreAcknowledgedButNotDecodedTwice()
    {
        await using TestGateway gateway = await TestGateway.StartAsync();

        await gateway.PostAsync(Frames.Request(Session, Frames.Hello(Session), Frames.Event(1)));
        (_, ExportLogsServiceResponse? body) = await gateway.PostAsync(Frames.Request(Session, Frames.Event(1)));

        Assert.That(body!.PartialSuccess, Is.Null, "A retry of an applied frame is not an error.");

        await gateway.Sink.WaitForRecordsAsync(1);
        await Task.Delay(100);
        Assert.That(gateway.Sink.Records, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task OutOfOrderFrames_AreRejected()
    {
        await using TestGateway gateway = await TestGateway.StartAsync();

        await gateway.PostAsync(Frames.Request(Session, Frames.Hello(Session), Frames.Event(1)));
        (_, ExportLogsServiceResponse? body) = await gateway.PostAsync(Frames.Request(Session, Frames.Event(5)));

        Assert.That(body!.PartialSuccess.RejectedLogRecords, Is.EqualTo(1));

        // the session is intact: the expected frame still goes through
        (_, ExportLogsServiceResponse? next) = await gateway.PostAsync(Frames.Request(Session, Frames.Event(2)));
        Assert.That(next!.PartialSuccess, Is.Null);
        await gateway.Sink.WaitForRecordsAsync(2);
    }

    [Test]
    public async Task UnknownSessions_AreRejectedSoTheProducerRestarts()
    {
        await using TestGateway gateway = await TestGateway.StartAsync();

        (_, ExportLogsServiceResponse? body) = await gateway.PostAsync(Frames.Request(Session, Frames.Event(1)));

        Assert.That(body!.PartialSuccess.RejectedLogRecords, Is.EqualTo(1));
        Assert.That(gateway.Sink.Records, Is.Empty);
    }

    [Test]
    public async Task Sessions_ContinueAcrossRequests()
    {
        await using TestGateway gateway = await TestGateway.StartAsync();

        await gateway.PostAsync(Frames.Request(Session, Frames.Hello(Session), Frames.Event(1, 1)));
        await gateway.PostAsync(Frames.Request(Session, Frames.Event(2, 2), Frames.Event(3, 3)));

        await gateway.Sink.WaitForRecordsAsync(3);
        Assert.That(gateway.Sink.Records.Select(r => BodyMember(r, "Count")!.IntValue), Is.EqualTo(new long[] { 1, 2, 3 }));
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Export
    // ---------------------------------------------------------------------------------------------------------------

    [Test]
    public async Task Export_RetriesTransientFailures()
    {
        await using TestGateway gateway = await TestGateway.StartAsync(new()
        {
            ["Gateway:Export:InitialRetryDelay"] = "00:00:00.010",
            ["Gateway:Export:MaxAttempts"] = "5"
        });
        gateway.Sink.FailRetryable(2);

        await gateway.PostAsync(Frames.Request(Session, Frames.Hello(Session), Frames.Event(1)));

        await gateway.Sink.WaitForRecordsAsync(1);
        Assert.That(gateway.Sink.Attempts, Is.EqualTo(3));
    }

    [Test]
    public async Task Export_ShutdownDuringRetryDeliversTheBatch()
    {
        await using TestGateway gateway = await TestGateway.StartAsync(new()
        {
            ["Gateway:Export:InitialRetryDelay"] = "00:00:05",
            ["Gateway:Export:MaxAttempts"] = "5"
        });
        gateway.Sink.FailRetryable(1);

        await gateway.PostAsync(Frames.Request(Session, Frames.Hello(Session), Frames.Event(1)));
        await gateway.Sink.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(50);

        await gateway.StopAsync();

        Assert.That(gateway.Sink.Records, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Export_DropsBatchesThatAreRejectedDownstreamAndKeepsGoing()
    {
        await using TestGateway gateway = await TestGateway.StartAsync(new()
        {
            ["Gateway:Export:InitialRetryDelay"] = "00:00:00.010"
        });
        gateway.Sink.FailPermanentlyOnce();

        await gateway.PostAsync(Frames.Request(Session, Frames.Hello(Session), Frames.Event(1)));
        await gateway.Sink.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await gateway.PostAsync(Frames.Request(Session, Frames.Event(2)));

        await gateway.Sink.WaitForRecordsAsync(1);
        Assert.That(gateway.Sink.Records, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task FullExportQueue_RefusesWithoutConsumingFrames()
    {
        await using TestGateway gateway = await TestGateway.StartAsync(new()
        {
            ["Gateway:Export:QueueCapacity"] = "1"
        });
        gateway.Sink.Block();

        // A is taken by the exporter and held in the blocked sink, B fills the queue.
        await gateway.PostAsync(Frames.Request(Session, Frames.Hello(Session), Frames.Event(1, 1)));
        await gateway.Sink.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        (HttpResponseMessage b, _) = await gateway.PostAsync(Frames.Request(Session, Frames.Event(2, 2)));
        Assert.That(b.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // C is refused, with a hint to retry.
        ExportLogsServiceRequest c = Frames.Request(Session, Frames.Event(3, 3));
        (HttpResponseMessage refused, _) = await gateway.PostAsync(c);
        Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        Assert.That(refused.Headers.RetryAfter, Is.Not.Null);

        // Nothing was consumed: once the queue drains, the very same request is accepted and decoded.
        gateway.Sink.Unblock();
        await gateway.Sink.WaitForRecordsAsync(2);

        (HttpResponseMessage retried, ExportLogsServiceResponse? body) = await gateway.PostAsync(c);
        Assert.That(retried.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body!.PartialSuccess, Is.Null);

        await gateway.Sink.WaitForRecordsAsync(3);
        Assert.That(gateway.Sink.Records.Select(r => BodyMember(r, "Count")!.IntValue), Is.EqualTo(new long[] { 1, 2, 3 }));
    }

    [Test]
    public async Task FullExportQueue_RefusesGrpcCallsAsUnavailable()
    {
        await using TestGateway gateway = await TestGateway.StartAsync(new()
        {
            ["Gateway:Export:QueueCapacity"] = "1"
        });
        gateway.Sink.Block();

        using GrpcChannel channel = GrpcChannel.ForAddress(gateway.Server.BaseAddress,
            new GrpcChannelOptions { HttpHandler = gateway.Server.CreateHandler() });
        LogsService.LogsServiceClient client = new(channel);

        await client.ExportAsync(Frames.Request(Session, Frames.Hello(Session), Frames.Event(1)));
        await gateway.Sink.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await client.ExportAsync(Frames.Request(Session, Frames.Event(2)));

        RpcException? ex = Assert.ThrowsAsync<RpcException>(async () =>
            await client.ExportAsync(Frames.Request(Session, Frames.Event(3))));
        Assert.That(ex!.StatusCode, Is.EqualTo(StatusCode.Unavailable));

        gateway.Sink.Unblock();
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Authentication belongs to the host
    // ---------------------------------------------------------------------------------------------------------------

    [Test]
    public async Task HostAuthentication_ProtectsTheEndpoints()
    {
        await using TestGateway gateway = await TestGateway.StartAsync(authenticated: true);
        ExportLogsServiceRequest request = Frames.Request(Session, Frames.Hello(Session), Frames.Event(1));

        (HttpResponseMessage anonymous, _) = await gateway.PostAsync(request);
        Assert.That(anonymous.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(gateway.Sink.Records, Is.Empty);

        (HttpResponseMessage alice, _) = await gateway.PostAsync(request, "alice");
        Assert.That(alice.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        await gateway.Sink.WaitForRecordsAsync(1);
    }

    [Test]
    public async Task Tenants_AreIsolatedEvenWhenTheySelectTheSameSessionId()
    {
        await using TestGateway gateway = await TestGateway.StartAsync(authenticated: true);

        await gateway.PostAsync(Frames.Request(Session, Frames.Hello(Session), Frames.Event(1, 1)), "alice");

        // Bob cannot continue Alice's session by announcing its identifier.
        (_, ExportLogsServiceResponse? intruder) =
            await gateway.PostAsync(Frames.Request(Session, Frames.Event(2, 666)), "bob");
        Assert.That(intruder!.PartialSuccess.RejectedLogRecords, Is.EqualTo(1));

        // Bob can open his own session with the same identifier without disturbing Alice's.
        (_, ExportLogsServiceResponse? own) =
            await gateway.PostAsync(Frames.Request(Session, Frames.Hello(Session), Frames.Event(1, 2)), "bob");
        Assert.That(own!.PartialSuccess, Is.Null);

        (_, ExportLogsServiceResponse? alice) =
            await gateway.PostAsync(Frames.Request(Session, Frames.Event(2, 3)), "alice");
        Assert.That(alice!.PartialSuccess, Is.Null);

        await gateway.Sink.WaitForRecordsAsync(3);
        Assert.That(gateway.Sink.Records.Select(r => BodyMember(r, "Count")!.IntValue),
            Is.EquivalentTo(new long[] { 1, 2, 3 }));
    }

    [Test]
    public void ResourceAttributes_RejectTheReservedSessionKeyAndDuplicates()
    {
        Assert.Throws<ArgumentException>(() =>
            RelayLogRecords.CreateRequest(Session, [], [new(EtwOtlpAttributes.RelaySessionId, "other")]));
        Assert.Throws<ArgumentException>(() =>
            RelayLogRecords.CreateRequest(Session, [], [new("service.name", "a"), new("service.name", "b")]));

        ExportLogsServiceRequest request = RelayLogRecords.CreateRequest(Session, [], [new("service.name", "relay")]);
        Assert.That(RelayLogRecords.TryGetSessionId(request.ResourceLogs.Single().Resource, out Guid id), Is.True);
        Assert.That(id, Is.EqualTo(Session));
        Assert.That(request.ResourceLogs.Single().Resource.Attributes.Count(a => a.Key == "service.name"), Is.EqualTo(1));
    }

    [Test]
    public void EndedSession_StillRecognizesRetriesInsideTheWindow()
    {
        using RelayLogProcessor processor = new();
        byte[] hello = Frames.Hello(Session);
        byte[] first = Frames.Event(1, 1);
        byte[] end = Frames.End(2);
        processor.Process("tenant", Frames.Request(Session, hello, first, end));

        RelayLogResult retry = processor.Process("tenant", Frames.Request(Session, first));
        Assert.That(retry.Rejected, Is.Zero);
        Assert.That(retry.Accepted, Is.EqualTo(1));
        Assert.That(retry.Decoded.ResourceLogs, Is.Empty);

        RelayLogResult after = processor.Process("tenant", Frames.Request(Session, Frames.Event(3, 9)));
        Assert.That(after.Rejected, Is.EqualTo(1));
        Assert.That(processor.ActiveSessions, Is.EqualTo(1));
    }

    [Test]
    public void SessionAdmission_NeverExceedsEitherLimit()
    {
        using RelayLogProcessor processor = new(new RelayLogProcessorOptions
        {
            MaxSessions = 4,
            MaxSessionsPerTenant = 2
        });

        List<Task<RelayLogResult>> tasks = [];
        foreach (string tenant in new[] { "a", "b" })
        {
            for (int i = 0; i < 8; i++)
            {
                Guid id = Guid.NewGuid();
                string captured = tenant;
                tasks.Add(Task.Run(() => processor.Process(captured, Frames.Request(id, Frames.Hello(id)))));
            }
        }

        RelayLogResult[] results = Task.WhenAll(tasks).GetAwaiter().GetResult();
        Assert.That(processor.ActiveSessions, Is.LessThanOrEqualTo(4));
        Assert.That(results.Sum(r => r.Accepted), Is.EqualTo(processor.ActiveSessions));
        Assert.That(results.Count(r => r.Accepted > 0 && r.Error is null), Is.LessThanOrEqualTo(4));
    }

    [Test]
    public void ExpiredSession_ReleasesItsSlot()
    {
        ManualTime clock = new();
        using RelayLogProcessor processor = new(new RelayLogProcessorOptions
        {
            MaxSessions = 1,
            SessionIdleTimeout = TimeSpan.FromMinutes(1),
            TimeProvider = clock
        });

        processor.Process("tenant", Frames.Request(Session, Frames.Hello(Session)));
        clock.UtcNow += TimeSpan.FromMinutes(2);

        Guid next = Guid.NewGuid();
        RelayLogResult opened = processor.Process("tenant", Frames.Request(next, Frames.Hello(next)));
        Assert.That(opened.Rejected, Is.Zero);
        Assert.That(processor.ActiveSessions, Is.EqualTo(1));
    }

    [Test]
    public void ExportEndpoint_RejectsCredentialsOnPlainHttp()
    {
        Assert.Throws<InvalidOperationException>(() => new OtlpHttpSink(new HttpClient(), "http://collector:4318",
            "v1/logs", new Dictionary<string, string> { ["Authorization"] = "secret" }, static batch => batch.Request));

        OtlpHttpSink plaintext = new(new HttpClient(), "http://jaeger:4318", "v1/traces",
            new Dictionary<string, string>(), static batch => batch.Request);
        Assert.That(plaintext, Is.Not.Null);
    }

    private static int DecodedEventCount(IEnumerable<byte[]> frames)
    {
        int count = 0;
        using RawEtwRelayDecoder decoder = new();
        foreach (byte[] frame in frames)
        {
            decoder.Process(frame, _ => count++);
        }

        return count;
    }

    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => UtcNow;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => UtcNow.UtcTicks;
    }
}
