using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;

using Nefarius.Utilities.ETW.OpenTelemetry;

using OpenTelemetry.Proto.Collector.Logs.V1;

namespace Nefarius.Utilities.ETW.Relay.CLI.Tests;

public class RunnerTests
{
    private static MemoryStream Binary(IEnumerable<byte[]> frames) => new(Frames.Concat(frames));

    private static Task<HttpResponseMessage> Respond(HttpStatusCode code, RetryConditionHeaderValue? retryAfter = null)
    {
        HttpResponseMessage response = new(code) { ReasonPhrase = code.ToString() };
        response.Headers.RetryAfter = retryAfter;
        return Task.FromResult(response);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Sessions and batching
    // ---------------------------------------------------------------------------------------------------------------

    [Test]
    public async Task EachSession_GetsItsOwnRequests()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        List<byte[]> input = [.. Frames.Session(a, 2), .. Frames.Session(b, 1)];
        RecordingHandler handler = new();

        (int code, RelayRunner runner, _) = await Run.RunAsync(Run.Settings(), Binary(input), handler);

        Assert.Multiple(() =>
        {
            Assert.That(code, Is.EqualTo(0));
            Assert.That(handler.Requests.Select(r => r.SessionId), Is.EqualTo(new[] { a, b }),
                "The end frame flushes, so two sessions are two requests, never mixed.");
            Assert.That(handler.Requests[0].FrameBytes(), Has.Count.EqualTo(4));
            Assert.That(handler.Requests[1].FrameBytes(), Has.Count.EqualTo(3));
            Assert.That(runner.FramesSent, Is.EqualTo(7));
        });
    }

    [Test]
    public async Task ANewHelloWithoutAnEnd_StartsANewRequest()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        List<byte[]> input = [.. Frames.Session(a, 2, end: false), .. Frames.Session(b, 1, end: false)];
        RecordingHandler handler = new();

        await Run.RunAsync(Run.Settings(), Binary(input), handler);

        Assert.That(handler.Requests.Select(r => r.SessionId), Is.EqualTo(new[] { a, b }));
    }

    [Test]
    public async Task Frames_ArriveInOrderAndUnmodified()
    {
        List<byte[]> input = Frames.Session(Guid.NewGuid(), 10);
        RecordingHandler handler = new();

        await Run.RunAsync(Run.Settings(), Binary(input), handler);

        List<byte[]> sent = handler.Requests.SelectMany(r => r.FrameBytes()).ToList();
        Assert.That(sent.Select(Convert.ToBase64String), Is.EqualTo(input.Select(Convert.ToBase64String)));
    }

    [Test]
    public async Task Batches_NeverExceedTheSixtyFourFrameRetryWindow()
    {
        List<byte[]> input = Frames.Session(Guid.NewGuid(), 129, end: false); // 130 frames
        RecordingHandler handler = new();

        await Run.RunAsync(Run.Settings(b => b.BatchFrames = 64), Binary(input), handler);

        Assert.That(handler.Requests.Select(r => r.FrameBytes().Count), Is.EqualTo(new[] { 64, 64, 2 }));
    }

    [Test]
    public async Task Batches_FlushOnBytes()
    {
        List<byte[]> input = Frames.Session(Guid.NewGuid(), 6, end: false);
        int eventLength = input[1].Length;
        RecordingHandler handler = new();

        await Run.RunAsync(Run.Settings(b => b.BatchBytes = eventLength * 2), Binary(input), handler);

        Assert.That(handler.Requests.Count, Is.GreaterThan(1));
        Assert.That(handler.Requests.SelectMany(r => r.FrameBytes()).Count(), Is.EqualTo(input.Count));
    }

    [Test]
    public async Task IncompleteBatches_FlushOnTheInterval()
    {
        PipeLikeStream pipe = new();
        TaskCompletionSource delivered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RecordingHandler handler = new((_, _) =>
        {
            delivered.TrySetResult();
            return Task.FromResult(RecordingHandler.Ok());
        });

        Task<(int Code, RelayRunner Runner, List<TimeSpan> Delays)> running = Run.RunAsync(
            Run.Settings(b => b.FlushInterval = TimeSpan.FromMilliseconds(50)), pipe, handler);

        pipe.Push(Frames.Hello(Guid.NewGuid()));

        // The stream is still open, so only the timer can have sent this.
        Assert.That(await Task.WhenAny(delivered.Task, Task.Delay(TimeSpan.FromSeconds(10))), Is.SameAs(delivered.Task));

        pipe.Complete();
        Assert.That((await running).Code, Is.EqualTo(0));
    }

    [Test]
    public async Task Resources_AreSentAsResourceAttributes()
    {
        RecordingHandler handler = new();

        await Run.RunAsync(
            Run.Settings(b => b.Resources = [new("service.name", "svc"), new("host.name", "box")]),
            Binary(Frames.Session(Guid.NewGuid(), 1)), handler);

        Dictionary<string, string> attributes = handler.Requests[0].Parse().ResourceLogs.Single().Resource.Attributes
            .ToDictionary(a => a.Key, a => a.Value.StringValue);
        Assert.Multiple(() =>
        {
            Assert.That(attributes["service.name"], Is.EqualTo("svc"));
            Assert.That(attributes["host.name"], Is.EqualTo("box"));
        });
    }

    [Test]
    public async Task Base64NdjsonInput_IsDeliveredLikeBinary()
    {
        List<byte[]> input = Frames.Session(Guid.NewGuid(), 3);
        RecordingHandler handler = new();

        await Run.RunAsync(Run.Settings(b => b.InputFormat = InputFormat.Base64Ndjson),
            new MemoryStream(Frames.Ndjson(input)), handler);

        Assert.That(handler.Requests.SelectMany(r => r.FrameBytes()).Select(Convert.ToBase64String),
            Is.EqualTo(input.Select(Convert.ToBase64String)));
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Idle heartbeat
    // ---------------------------------------------------------------------------------------------------------------

    [Test]
    public async Task IdleOpenSession_ReplaysEventsWithoutTheHello()
    {
        PipeLikeStream pipe = new();
        RecordingHandler handler = new();
        Task<(int Code, RelayRunner Runner, List<TimeSpan> Delays)> running = Run.RunAsync(
            Run.Settings(b =>
            {
                b.Gzip = true;
                b.BatchFrames = 2;
                b.FlushInterval = TimeSpan.FromHours(1);
                b.HeartbeatInterval = TimeSpan.FromMilliseconds(120);
            }), pipe, handler);

        pipe.Push(Frames.Concat([Frames.Hello(Guid.NewGuid()), Frames.Event(1)]));
        await WaitUntil(() => handler.Requests.Count >= 2, TimeSpan.FromSeconds(5));

        CapturedRequest[] sent = handler.Requests.Take(2).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(sent[0].FrameBytes(), Has.Count.EqualTo(2));
            Assert.That(sent[1].Body, Is.Not.EqualTo(sent[0].Body));
            Assert.That(sent[1].ContentEncoding, Is.EqualTo("gzip"));
            Assert.That(sent[1].FrameBytes().Select(Convert.ToBase64String),
                Is.EqualTo(sent[0].FrameBytes().Skip(1).Select(Convert.ToBase64String)));
        });

        pipe.Complete();
        (int code, RelayRunner runner, _) = await running;
        Assert.Multiple(() =>
        {
            Assert.That(code, Is.EqualTo(0));
            Assert.That(runner.FramesSent, Is.EqualTo(2));
            Assert.That(runner.Batches, Is.GreaterThanOrEqualTo(2));
        });
    }

    [Test]
    public async Task NewBatch_ResetsTheHeartbeatDeadline()
    {
        PipeLikeStream pipe = new();
        Stopwatch clock = Stopwatch.StartNew();
        List<TimeSpan> sentAt = [];
        RecordingHandler handler = new((_, _) =>
        {
            lock (sentAt)
            {
                sentAt.Add(clock.Elapsed);
            }

            return Task.FromResult(RecordingHandler.Ok());
        });

        Task running = Run.RunAsync(Run.Settings(b =>
        {
            b.BatchFrames = 2;
            b.FlushInterval = TimeSpan.FromMilliseconds(40);
            b.HeartbeatInterval = TimeSpan.FromMilliseconds(1000);
        }), pipe, handler);

        Guid session = Guid.NewGuid();
        pipe.Push(Frames.Concat([Frames.Hello(session), Frames.Event(1)]));
        await WaitUntil(() => handler.Requests.Count >= 1 && handler.Requests[0].FrameBytes().Count == 2,
            TimeSpan.FromSeconds(5));

        // Inside the armed interval. The gap distinguishes a replaced deadline from the remainder of the original one.
        await Task.Delay(TimeSpan.FromMilliseconds(400));
        pipe.Push(Frames.Event(2));

        int replayAt = -1;
        TimeSpan gap = TimeSpan.Zero;
        await WaitUntil(() =>
        {
            CapturedRequest[] requests = [.. handler.Requests];
            TimeSpan[] times;
            lock (sentAt)
            {
                times = [.. sentAt];
            }

            int count = Math.Min(requests.Length, times.Length);
            if (count < 2 || requests[0].FrameBytes().Count != 2)
            {
                return false;
            }

            int nextBatch = Array.FindIndex(requests, 1, count - 1,
                request => !request.Body.AsSpan().SequenceEqual(requests[0].Body));
            if (nextBatch < 0)
            {
                return false;
            }

            int duplicate = Array.FindIndex(requests, nextBatch + 1, count - (nextBatch + 1),
                request => request.Body.AsSpan().SequenceEqual(requests[nextBatch].Body));
            if (duplicate < 0)
            {
                return false;
            }

            replayAt = duplicate;
            gap = times[duplicate] - times[nextBatch];
            return true;
        }, TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(replayAt, Is.EqualTo(2), "The replay follows the batch that reset the deadline.");
            Assert.That(handler.Requests[1].FrameBytes(), Has.Count.EqualTo(1));
            Assert.That(gap, Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(700)));
        });

        pipe.Complete();
        await running;
    }

    [Test]
    public async Task EndedSession_IsNotKeptAlive()
    {
        PipeLikeStream pipe = new();
        RecordingHandler handler = new();
        Task<(int Code, RelayRunner Runner, List<TimeSpan> Delays)> running = Run.RunAsync(Run.Settings(b =>
        {
            b.FlushInterval = TimeSpan.FromMilliseconds(40);
            b.HeartbeatInterval = TimeSpan.FromMilliseconds(80);
        }), pipe, handler);

        pipe.Push(Frames.Concat(Frames.Session(Guid.NewGuid(), 1)));
        await WaitUntil(() => handler.Requests.Count >= 1, TimeSpan.FromSeconds(5));
        await Task.Delay(TimeSpan.FromMilliseconds(400));

        Assert.That(handler.Requests, Has.Count.EqualTo(1));

        pipe.Complete();
        Assert.That((await running).Code, Is.EqualTo(0));
    }

    [Test]
    public async Task DisabledHeartbeat_LeavesAnIdleSessionAlone()
    {
        PipeLikeStream pipe = new();
        RecordingHandler handler = new();
        Task<(int Code, RelayRunner Runner, List<TimeSpan> Delays)> running = Run.RunAsync(Run.Settings(b =>
        {
            b.BatchFrames = 2;
            b.FlushInterval = TimeSpan.FromHours(1);
            b.HeartbeatInterval = TimeSpan.Zero;
        }), pipe, handler);

        pipe.Push(Frames.Concat([Frames.Hello(Guid.NewGuid()), Frames.Event(1)]));
        await WaitUntil(() => handler.Requests.Count >= 1 && handler.Requests[0].FrameBytes().Count == 2,
            TimeSpan.FromSeconds(5));
        await Task.Delay(TimeSpan.FromMilliseconds(350));

        Assert.That(handler.Requests, Has.Count.EqualTo(1));

        pipe.Complete();
        Assert.That((await running).Code, Is.EqualTo(0));
    }

    [Test]
    public async Task HeartbeatRejection_IsFatalAndNotRetried()
    {
        PipeLikeStream pipe = new();
        RecordingHandler handler = new((index, _) =>
            Task.FromResult(index == 0 ? RecordingHandler.Ok() : RecordingHandler.Rejected("The session is unknown")));

        Task running = Run.RunAsync(Run.Settings(b =>
        {
            b.BatchFrames = 2;
            b.FlushInterval = TimeSpan.FromHours(1);
            b.HeartbeatInterval = TimeSpan.FromMilliseconds(80);
        }), pipe, handler);

        pipe.Push(Frames.Concat([Frames.Hello(Guid.NewGuid()), Frames.Event(1)]));
        Task finished = await Task.WhenAny(running, Task.Delay(TimeSpan.FromSeconds(5)));
        pipe.Complete();
        Assert.That(finished, Is.SameAs(running));
        DeliveryException? ex = Assert.ThrowsAsync<DeliveryException>(async () => await running);

        Assert.Multiple(() =>
        {
            Assert.That(ex!.Message, Does.Contain("session is unknown"));
            Assert.That(handler.Requests, Has.Count.EqualTo(2));
            Assert.That(handler.Requests[1].Body, Is.Not.EqualTo(handler.Requests[0].Body));
            Assert.That(handler.Requests[1].FrameBytes().Select(Convert.ToBase64String),
                Is.EqualTo(handler.Requests[0].FrameBytes().Skip(1).Select(Convert.ToBase64String)));
        });
    }

    [Test]
    public async Task HeartbeatAfterGatewayRestart_IsRejectedAndExportsNoEvents()
    {
        PipeLikeStream pipe = new();
        RelayLogProcessor gateway = new();
        bool restarted = false;
        int firstExports = 0;
        int replayExports = -1;
        RecordingHandler handler = new((_, request) =>
        {
            ExportLogsServiceRequest message = request.Parse();
            RelayLogResult result = gateway.Process("anonymous", message);
            if (!restarted)
            {
                firstExports = ExportedEvents(result.Decoded);
                gateway.Dispose();
                gateway = new RelayLogProcessor();
                restarted = true;
                return Task.FromResult(RecordingHandler.Ok());
            }

            replayExports = ExportedEvents(result.Decoded);
            return Task.FromResult(RecordingHandler.Rejected(result.Error ?? "rejected"));
        });

        Task running = Run.RunAsync(Run.Settings(b =>
        {
            b.BatchFrames = 2;
            b.FlushInterval = TimeSpan.FromHours(1);
            b.HeartbeatInterval = TimeSpan.FromMilliseconds(80);
        }), pipe, handler);

        try
        {
            pipe.Push(Frames.Concat([Frames.Hello(Guid.NewGuid()), Frames.Event(1)]));
            Task finished = await Task.WhenAny(running, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.That(finished, Is.SameAs(running));
            DeliveryException? ex = Assert.ThrowsAsync<DeliveryException>(async () => await running);

            Assert.Multiple(() =>
            {
                Assert.That(firstExports, Is.GreaterThan(0), "The normal flush must still export its event.");
                Assert.That(replayExports, Is.EqualTo(0));
                Assert.That(ex!.Message, Does.Contain("session is unknown"));
                Assert.That(handler.Requests, Has.Count.EqualTo(2));
                Assert.That(handler.Requests[0].FrameBytes(), Has.Count.EqualTo(2));
                Assert.That(handler.Requests[1].FrameBytes().Select(Convert.ToBase64String),
                    Is.EqualTo(handler.Requests[0].FrameBytes().Skip(1).Select(Convert.ToBase64String)));
            });
        }
        finally
        {
            pipe.Complete();
            gateway.Dispose();
        }
    }

    private static int ExportedEvents(ExportLogsServiceRequest request) =>
        request.ResourceLogs.Sum(resource => resource.ScopeLogs.Sum(scope => scope.LogRecords.Count));

    private static async Task WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        using CancellationTokenSource timeoutSource = new(timeout);
        while (!condition())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(15), timeoutSource.Token);
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Wire format
    // ---------------------------------------------------------------------------------------------------------------

    [Test]
    public async Task Requests_AreProtobufPostsToTheLogsEndpointWithHeaders()
    {
        RecordingHandler handler = new();

        await Run.RunAsync(Run.Settings(b => b.Headers = [new("X-Tenant", "blue"), new("X-Other", "1")]),
            Binary(Frames.Session(Guid.NewGuid(), 1)), handler);

        CapturedRequest request = handler.Requests.Single();
        Assert.Multiple(() =>
        {
            Assert.That(request.Uri.AbsoluteUri, Is.EqualTo("http://gateway.test/v1/logs"));
            Assert.That(request.ContentType, Is.EqualTo("application/x-protobuf"));
            Assert.That(request.ContentEncoding, Is.Null);
            Assert.That(request.Headers["X-Tenant"], Is.EqualTo("blue"));
            Assert.That(request.Headers["X-Other"], Is.EqualTo("1"));
        });
    }

    [Test]
    public async Task Gzip_CompressesTheBodyAndDeclaresIt()
    {
        List<byte[]> input = Frames.Session(Guid.NewGuid(), 4);
        RecordingHandler handler = new();

        await Run.RunAsync(Run.Settings(b => b.Gzip = true), Binary(input), handler);

        CapturedRequest request = handler.Requests.Single();
        Assert.Multiple(() =>
        {
            Assert.That(request.ContentEncoding, Is.EqualTo("gzip"));
            Assert.That(request.FrameBytes().Select(Convert.ToBase64String),
                Is.EqualTo(input.Select(Convert.ToBase64String)));
        });
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Retries
    // ---------------------------------------------------------------------------------------------------------------

    [Test]
    public async Task RetriedRequests_AreByteIdentical()
    {
        RecordingHandler handler = new((index, _) => index switch
        {
            0 => Respond(HttpStatusCode.ServiceUnavailable),
            1 => Respond((HttpStatusCode)429),
            2 => Respond(HttpStatusCode.RequestTimeout),
            _ => Task.FromResult(RecordingHandler.Ok())
        });

        (int code, _, _) = await Run.RunAsync(Run.Settings(b => b.Gzip = true),
            Binary(Frames.Session(Guid.NewGuid(), 3)), handler);

        Assert.Multiple(() =>
        {
            Assert.That(code, Is.EqualTo(0));
            Assert.That(handler.Requests, Has.Count.EqualTo(4));
            Assert.That(handler.Requests.Select(r => Convert.ToBase64String(r.Body)).Distinct().Count(), Is.EqualTo(1),
                "Every attempt must carry exactly the bytes of the first, including any gzip stream.");
        });
    }

    [Test]
    public async Task RetryAfter_IsHonouredAndCapped()
    {
        RecordingHandler handler = new((index, _) => index switch
        {
            0 => Respond((HttpStatusCode)429, new RetryConditionHeaderValue(TimeSpan.FromSeconds(7))),
            1 => Respond(HttpStatusCode.ServiceUnavailable, new RetryConditionHeaderValue(TimeSpan.FromHours(2))),
            _ => Task.FromResult(RecordingHandler.Ok())
        });

        (_, _, List<TimeSpan> delays) = await Run.RunAsync(
            Run.Settings(b => b.RetryAfterCap = TimeSpan.FromSeconds(30)),
            Binary(Frames.Session(Guid.NewGuid(), 1)), handler);

        Assert.That(delays, Is.EqualTo(new[] { TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(30) }));
    }

    [Test]
    public async Task ConnectionFailures_AreRetried()
    {
        RecordingHandler handler = new((index, _) => index < 2
            ? throw new HttpRequestException("connection refused")
            : Task.FromResult(RecordingHandler.Ok()));

        (int code, _, _) = await Run.RunAsync(Run.Settings(), Binary(Frames.Session(Guid.NewGuid(), 1)), handler);

        Assert.Multiple(() =>
        {
            Assert.That(code, Is.EqualTo(0));
            Assert.That(handler.Requests, Has.Count.EqualTo(3));
        });
    }

    [Test]
    public async Task TimedOutRequests_AreRetried()
    {
        HangFirstHandler handler = new();

        (int code, _, _) = await Run.RunAsync(Run.Settings(b => b.RequestTimeout = TimeSpan.FromMilliseconds(100)),
            Binary(Frames.Session(Guid.NewGuid(), 1)), handler);

        Assert.Multiple(() =>
        {
            Assert.That(code, Is.EqualTo(0));
            Assert.That(handler.Calls, Is.EqualTo(2), "The first request hangs until the timeout; the second succeeds.");
        });
    }
    [Test]
    public void GivingUp_AfterTheRetryBudget_IsADeliveryFailure()
    {
        RecordingHandler handler = new((_, _) => Respond(HttpStatusCode.BadGateway));

        DeliveryException? ex = Assert.ThrowsAsync<DeliveryException>(async () =>
            await Run.RunAsync(Run.Settings(b => b.MaxRetries = 2), Binary(Frames.Session(Guid.NewGuid(), 1)),
                handler));

        Assert.Multiple(() =>
        {
            Assert.That(handler.Requests, Has.Count.EqualTo(3));
            Assert.That(ex!.Message, Does.Contain("Giving up"));
        });
    }

    [TestCase(HttpStatusCode.BadRequest)]
    [TestCase(HttpStatusCode.Unauthorized)]
    [TestCase(HttpStatusCode.Forbidden)]
    [TestCase(HttpStatusCode.NotFound)]
    [TestCase(HttpStatusCode.UnprocessableEntity)]
    public void OtherClientErrors_AreFatalAndNotRetried(HttpStatusCode code)
    {
        RecordingHandler handler = new((_, _) => Respond(code));

        Assert.ThrowsAsync<DeliveryException>(async () =>
            await Run.RunAsync(Run.Settings(), Binary(Frames.Session(Guid.NewGuid(), 1)), handler));

        Assert.That(handler.Requests, Has.Count.EqualTo(1));
    }

    [Test]
    public void RejectedRecords_AreFatalEvenOnSuccessStatus_AndNotRetried()
    {
        RecordingHandler handler = new((_, _) =>
            Task.FromResult(RecordingHandler.Rejected("relay session is unknown")));

        DeliveryException? ex = Assert.ThrowsAsync<DeliveryException>(async () =>
            await Run.RunAsync(Run.Settings(), Binary(Frames.Session(Guid.NewGuid(), 1)), handler));

        Assert.Multiple(() =>
        {
            Assert.That(handler.Requests, Has.Count.EqualTo(1));
            Assert.That(ex!.Message, Does.Contain("session is unknown"));
        });
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Input protocol
    // ---------------------------------------------------------------------------------------------------------------

    [Test]
    public void EventBeforeHello_IsAnInputError()
    {
        RecordingHandler handler = new();

        InputException? ex = Assert.ThrowsAsync<InputException>(async () =>
            await Run.RunAsync(Run.Settings(), Binary([Frames.Event(0)]), handler));

        Assert.Multiple(() =>
        {
            Assert.That(ex!.Message, Does.Contain("hello"));
            Assert.That(handler.Requests, Is.Empty);
        });
    }

    [Test]
    public void FramesAfterEnd_NeedANewHello()
    {
        List<byte[]> input = [.. Frames.Session(Guid.NewGuid(), 1), Frames.Event(9)];

        Assert.ThrowsAsync<InputException>(async () =>
            await Run.RunAsync(Run.Settings(), Binary(input), new RecordingHandler()));
    }

    [Test]
    public void Garbage_ExplainsTheTextPipelineProblemOnFirstFrame()
    {
        // What a pipeline that treated the bytes as text would produce: valid UTF-8 text, wrong magic.
        byte[] text = System.Text.Encoding.UTF8.GetBytes(new string('x', 64));

        InputException? ex = Assert.ThrowsAsync<InputException>(async () =>
            await Run.RunAsync(Run.Settings(), new MemoryStream(text), new RecordingHandler()));

        Assert.That(ex!.Message, Does.Contain("PowerShell 7.4").And.Contain("relay-ndjson"));
    }

    [Test]
    public async Task FramesBeforeABrokenFrame_AreStillDelivered()
    {
        Guid session = Guid.NewGuid();
        byte[] input = [.. Frames.Concat([Frames.Hello(session), Frames.Event(1)]), .. new byte[32]];
        RecordingHandler handler = new();

        Assert.ThrowsAsync<InputException>(async () =>
            await Run.RunAsync(Run.Settings(), new MemoryStream(input), handler));

        Assert.That(handler.Requests.Single().FrameBytes(), Has.Count.EqualTo(2));
        await Task.CompletedTask;
    }

    [Test]
    public async Task InputEndingWithoutAnEndFrame_IsDeliveredAndSucceeds()
    {
        RecordingHandler handler = new();

        (int code, _, _) = await Run.RunAsync(Run.Settings(), Binary(Frames.Session(Guid.NewGuid(), 2, end: false)),
            handler);

        Assert.Multiple(() =>
        {
            Assert.That(code, Is.EqualTo(0));
            Assert.That(handler.Requests.Single().FrameBytes(), Has.Count.EqualTo(3));
        });
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Interrupts
    // ---------------------------------------------------------------------------------------------------------------

    [Test]
    public async Task Abort_StopsReadingButDeliversWhatIsQueued()
    {
        PipeLikeStream pipe = new();
        using CancellationTokenSource abort = new();
        RecordingHandler handler = new();

        Task<(int Code, RelayRunner Runner, List<TimeSpan> Delays)> running =
            Run.RunAsync(Run.Settings(), pipe, handler, abort.Token);

        pipe.Push(Frames.Concat(Frames.Session(Guid.NewGuid(), 2, end: false)));
        // Cancel only once those frames are queued and the reader is blocked on the next one.
        await pipe.WaitUntilNextReadBlocks().WaitAsync(TimeSpan.FromSeconds(5));
        await abort.CancelAsync();

        (int code, RelayRunner runner, _) = await running.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Multiple(() =>
        {
            Assert.That(code, Is.EqualTo(0));
            Assert.That(runner.FramesSent, Is.EqualTo(3));
        });
    }

    [Test]
    public void Abort_WhileDeliveryIsStuck_IsADeliveryFailure()
    {
        PipeLikeStream pipe = new();
        using CancellationTokenSource abort = new();
        HangFirstHandler handler = new();

        Task<(int Code, RelayRunner Runner, List<TimeSpan> Delays)> running =
            Run.RunAsync(Run.Settings(), pipe, handler, abort.Token);

        pipe.Push(Frames.Concat(Frames.Session(Guid.NewGuid(), 1, end: false)));
        Assert.That(pipe.WaitUntilNextReadBlocks().Wait(TimeSpan.FromSeconds(5)), Is.True);
        abort.Cancel();

        // The sender gets a few seconds of grace, then the unfinished request is a failure.
        DeliveryException? ex = Assert.ThrowsAsync<DeliveryException>(async () => await running);
        Assert.That(ex!.Message, Does.Contain("Interrupted"));
    }

    [Test]
    public void MalformedAcknowledgement_IsNotTreatedAsDelivered()
    {
        RecordingHandler handler = new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([0x80])
        }));

        DeliveryException? ex = Assert.ThrowsAsync<DeliveryException>(async () =>
            await Run.RunAsync(Run.Settings(), Binary(Frames.Session(Guid.NewGuid(), 1)), handler));

        Assert.Multiple(() =>
        {
            Assert.That(handler.Requests, Has.Count.EqualTo(1));
            Assert.That(ex!.Message, Does.Contain("acknowledgement"));
        });
    }

    [Test]
    public void UnreadableRejectionBody_StaysAPermanentFailure()
    {
        RecordingHandler handler = new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new ThrowingContent()
        }));

        Assert.ThrowsAsync<DeliveryException>(async () =>
            await Run.RunAsync(Run.Settings(), Binary(Frames.Session(Guid.NewGuid(), 1)), handler));

        Assert.That(handler.Requests, Has.Count.EqualTo(1));
    }

    private sealed class ThrowingContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            throw new IOException("body unavailable");

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
