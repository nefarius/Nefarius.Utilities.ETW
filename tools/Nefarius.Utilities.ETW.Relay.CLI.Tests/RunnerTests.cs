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

        try
        {
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
        finally
        {
            await StopAsync(pipe, running);
        }
    }

    [Test]
    public async Task NewBatch_ResetsTheHeartbeatDeadline()
    {
        ManualTime clock = new();
        PipeLikeStream pipe = new();
        byte[] secondEvent = Frames.Event(2);
        int schedulesAtSecondBatch = -1;
        RecordingHandler handler = new((_, captured) =>
        {
            if (Volatile.Read(ref schedulesAtSecondBatch) < 0 &&
                captured.FrameBytes().Select(Convert.ToBase64String)
                    .SequenceEqual([Convert.ToBase64String(secondEvent)]))
            {
                Volatile.Write(ref schedulesAtSecondBatch, clock.ScheduleCount);
            }

            return Task.FromResult(RecordingHandler.Ok());
        });
        Task<(int Code, RelayRunner Runner, List<TimeSpan> Delays)> running = Run.RunAsync(Run.Settings(b =>
        {
            b.BatchFrames = 2;
            b.FlushInterval = TimeSpan.FromMilliseconds(40);
            b.HeartbeatInterval = TimeSpan.FromMilliseconds(1000);
        }), pipe, handler, clock: clock);

        try
        {
            Guid session = Guid.NewGuid();
            pipe.Push(Frames.Concat([Frames.Hello(session), Frames.Event(1)]));
            await WaitUntil(() => handler.Requests.Count >= 1 && handler.Requests[0].FrameBytes().Count == 2
                                  && clock.ScheduleCount >= 1, TimeSpan.FromSeconds(5));

            clock.Advance(TimeSpan.FromMilliseconds(400));
            pipe.Push(secondEvent);
            await WaitUntil(() =>
            {
                int seen = Volatile.Read(ref schedulesAtSecondBatch);
                return seen >= 0 && clock.ScheduleCount > seen;
            }, TimeSpan.FromSeconds(5));

            clock.Advance(TimeSpan.FromMilliseconds(999));
            await Task.Delay(TimeSpan.FromMilliseconds(100));
            Assert.That(CountRequestsWithFrames(handler, secondEvent), Is.EqualTo(1),
                "The replay must wait for the full interval after the second batch.");

            clock.Advance(TimeSpan.FromMilliseconds(1));
            await WaitUntil(() => CountRequestsWithFrames(handler, secondEvent) >= 2, TimeSpan.FromSeconds(5));

            pipe.Complete();
            Assert.That((await running).Code, Is.EqualTo(0));
        }
        finally
        {
            await StopAsync(pipe, running);
        }
    }

    [Test]
    public async Task PendingBatch_FlushesWhenTheHeartbeatDeadlineArrives()
    {
        ManualTime clock = new();
        PipeLikeStream pipe = new();
        RecordingHandler handler = new();
        byte[] pending = Frames.Event(2);
        Task<(int Code, RelayRunner Runner, List<TimeSpan> Delays)> running = Run.RunAsync(Run.Settings(b =>
        {
            b.BatchFrames = 2;
            b.FlushInterval = TimeSpan.FromHours(1);
            b.HeartbeatInterval = TimeSpan.FromMilliseconds(1000);
        }), pipe, handler, clock: clock);

        try
        {
            Guid session = Guid.NewGuid();
            pipe.Push(Frames.Concat([Frames.Hello(session), Frames.Event(1)]));
            await WaitUntil(() => handler.Requests.Count >= 1 && clock.ScheduleCount >= 1, TimeSpan.FromSeconds(5));

            int armedSchedules = clock.ScheduleCount;
            pipe.Push(pending);
            await WaitUntil(() => clock.ScheduleCount > armedSchedules, TimeSpan.FromSeconds(5));

            clock.Advance(TimeSpan.FromMilliseconds(999));
            await Task.Delay(TimeSpan.FromMilliseconds(100));
            Assert.That(handler.Requests, Has.Count.EqualTo(1),
                "The open batch must wait for the heartbeat deadline rather than the flush interval.");

            clock.Advance(TimeSpan.FromMilliseconds(1));
            await WaitUntil(() => handler.Requests.Count >= 2, TimeSpan.FromSeconds(5));
            Assert.That(handler.Requests[1].FrameBytes().Select(Convert.ToBase64String),
                Is.EqualTo(new[] { Convert.ToBase64String(pending) }));

            pipe.Complete();
            Assert.That((await running).Code, Is.EqualTo(0));
        }
        finally
        {
            await StopAsync(pipe, running);
        }
    }

    [Test]
    public async Task DuplicateHelloOnlyBatch_KeepsThePreviousHeartbeat()
    {
        ManualTime clock = new();
        PipeLikeStream pipe = new();
        Guid session = Guid.NewGuid();
        byte[] hello = Frames.Hello(session);
        byte[] firstEvent = Frames.Event(1);
        int schedulesAtHello = -1;
        RecordingHandler handler = new((_, captured) =>
        {
            if (Volatile.Read(ref schedulesAtHello) < 0 &&
                captured.FrameBytes().Select(Convert.ToBase64String).SequenceEqual([Convert.ToBase64String(hello)]))
            {
                Volatile.Write(ref schedulesAtHello, clock.ScheduleCount);
            }

            return Task.FromResult(RecordingHandler.Ok());
        });
        Task<(int Code, RelayRunner Runner, List<TimeSpan> Delays)> running = Run.RunAsync(Run.Settings(b =>
        {
            b.BatchFrames = 2;
            b.FlushInterval = TimeSpan.FromHours(1);
            b.HeartbeatInterval = TimeSpan.FromMilliseconds(1000);
        }), pipe, handler, clock: clock);

        try
        {
            pipe.Push(Frames.Concat([hello, firstEvent]));
            await WaitUntil(() => handler.Requests.Count >= 1 && clock.ScheduleCount >= 1, TimeSpan.FromSeconds(5));

            int armedSchedules = clock.ScheduleCount;
            pipe.Push(hello);
            await WaitUntil(() => clock.ScheduleCount > armedSchedules, TimeSpan.FromSeconds(5));
            clock.Advance(TimeSpan.FromMilliseconds(1000));
            await WaitUntil(() =>
            {
                int seen = Volatile.Read(ref schedulesAtHello);
                return seen >= 0 && clock.ScheduleCount > seen;
            }, TimeSpan.FromSeconds(5));

            clock.Advance(TimeSpan.FromMilliseconds(999));
            await Task.Delay(TimeSpan.FromMilliseconds(100));
            Assert.That(CountRequestsWithFrames(handler, firstEvent), Is.EqualTo(0),
                "The retained heartbeat starts a full interval after the hello is acknowledged.");

            clock.Advance(TimeSpan.FromMilliseconds(1));
            await WaitUntil(() => CountRequestsWithFrames(handler, firstEvent) >= 1, TimeSpan.FromSeconds(5));

            pipe.Complete();
            Assert.That((await running).Code, Is.EqualTo(0));
        }
        finally
        {
            await StopAsync(pipe, running);
        }
    }

    private static int CountRequestsWithFrames(RecordingHandler handler, params byte[][] frames)
    {
        string[] expected = frames.Select(Convert.ToBase64String).ToArray();
        return handler.Requests.Count(request => request.FrameBytes().Select(Convert.ToBase64String).SequenceEqual(expected));
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

        try
        {
            pipe.Push(Frames.Concat(Frames.Session(Guid.NewGuid(), 1)));
            await WaitUntil(() => handler.Requests.Count >= 1, TimeSpan.FromSeconds(5));
            await Task.Delay(TimeSpan.FromMilliseconds(400));

            Assert.That(handler.Requests, Has.Count.EqualTo(1));
            pipe.Complete();
            Assert.That((await running).Code, Is.EqualTo(0));
        }
        finally
        {
            await StopAsync(pipe, running);
        }
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

        try
        {
            pipe.Push(Frames.Concat([Frames.Hello(Guid.NewGuid()), Frames.Event(1)]));
            await WaitUntil(() => handler.Requests.Count >= 1 && handler.Requests[0].FrameBytes().Count == 2,
                TimeSpan.FromSeconds(5));
            await Task.Delay(TimeSpan.FromMilliseconds(350));

            Assert.That(handler.Requests, Has.Count.EqualTo(1));
            pipe.Complete();
            Assert.That((await running).Code, Is.EqualTo(0));
        }
        finally
        {
            await StopAsync(pipe, running);
        }
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

        try
        {
            pipe.Push(Frames.Concat([Frames.Hello(Guid.NewGuid()), Frames.Event(1)]));
            Task finished = await Task.WhenAny(running, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.That(finished, Is.SameAs(running));
            DeliveryException? ex = await Assert.ThrowsAsync<DeliveryException>(async () => await running);

            Assert.Multiple(() =>
            {
                Assert.That(ex!.Message, Does.Contain("session is unknown"));
                Assert.That(handler.Requests, Has.Count.EqualTo(2));
                Assert.That(handler.Requests[1].Body, Is.Not.EqualTo(handler.Requests[0].Body));
                Assert.That(handler.Requests[1].FrameBytes().Select(Convert.ToBase64String),
                    Is.EqualTo(handler.Requests[0].FrameBytes().Skip(1).Select(Convert.ToBase64String)));
            });
        }
        finally
        {
            await StopAsync(pipe, running);
        }
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
            DeliveryException? ex = await Assert.ThrowsAsync<DeliveryException>(async () => await running);

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
            await StopAsync(pipe, running);
            gateway.Dispose();
        }
    }

    [Test]
    public async Task HeartbeatWithAnEmbeddedHello_DoesNotReopenAfterRestart()
    {
        ManualTime clock = new();
        PipeLikeStream pipe = new();
        RelayLogProcessor gateway = new();
        int seen = 0;
        int firstExports = 0;
        int replayExports = -1;
        int schedulesAtEmbeddedBatch = -1;
        RecordingHandler handler = new((_, request) =>
        {
            RelayLogResult result = gateway.Process("anonymous", request.Parse());
            seen++;
            if (seen < 3)
            {
                if (seen == 1)
                {
                    firstExports = ExportedEvents(result.Decoded);
                }
                else
                {
                    Volatile.Write(ref schedulesAtEmbeddedBatch, clock.ScheduleCount);
                    gateway.Dispose();
                    gateway = new RelayLogProcessor();
                }

                return Task.FromResult(RecordingHandler.Ok());
            }

            replayExports = ExportedEvents(result.Decoded);
            return Task.FromResult(RecordingHandler.Rejected(result.Error ?? "rejected"));
        });

        Task running = Run.RunAsync(Run.Settings(b =>
        {
            b.BatchFrames = 3;
            b.FlushInterval = TimeSpan.FromHours(1);
            b.HeartbeatInterval = TimeSpan.FromMilliseconds(1000);
        }), pipe, handler, clock: clock);

        try
        {
            Guid session = Guid.NewGuid();
            byte[] hello = Frames.Hello(session);
            byte[] firstEvent = Frames.Event(1);
            pipe.Push(Frames.Concat([hello, firstEvent, Frames.Event(2)]));
            await WaitUntil(() => handler.Requests.Count >= 1 && clock.ScheduleCount >= 1, TimeSpan.FromSeconds(5));

            pipe.Push(Frames.Concat([firstEvent, hello, firstEvent]));
            await WaitUntil(() =>
            {
                int seenSchedules = Volatile.Read(ref schedulesAtEmbeddedBatch);
                return seenSchedules >= 0 && clock.ScheduleCount > seenSchedules;
            }, TimeSpan.FromSeconds(5));

            clock.Advance(TimeSpan.FromMilliseconds(1000));
            Task finished = await Task.WhenAny(running, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.That(finished, Is.SameAs(running));
            DeliveryException? ex = await Assert.ThrowsAsync<DeliveryException>(async () => await running);

            Assert.Multiple(() =>
            {
                Assert.That(firstExports, Is.GreaterThan(0));
                Assert.That(replayExports, Is.EqualTo(0));
                Assert.That(ex!.Message, Does.Contain("session is unknown"));
                Assert.That(handler.Requests[2].FrameBytes().Select(Convert.ToBase64String),
                    Is.EqualTo(new[] { Convert.ToBase64String(firstEvent), Convert.ToBase64String(firstEvent) }));
            });
        }
        finally
        {
            await StopAsync(pipe, running);
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

    /// <summary>Stops the runner even when an assertion fails before the test reaches its own completion.</summary>
    private static async Task StopAsync(PipeLikeStream pipe, Task running)
    {
        pipe.Complete();
        try
        {
            await running.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception)
        {
            // The failing assertion stays the test result. This only lets the runner exit.
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
    public async Task GivingUp_AfterTheRetryBudget_IsADeliveryFailure()
    {
        RecordingHandler handler = new((_, _) => Respond(HttpStatusCode.BadGateway));

        DeliveryException? ex = await Assert.ThrowsAsync<DeliveryException>(async () =>
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
    public async Task OtherClientErrors_AreFatalAndNotRetried(HttpStatusCode code)
    {
        RecordingHandler handler = new((_, _) => Respond(code));

        await Assert.ThrowsAsync<DeliveryException>(async () =>
            await Run.RunAsync(Run.Settings(), Binary(Frames.Session(Guid.NewGuid(), 1)), handler));

        Assert.That(handler.Requests, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task RejectedRecords_AreFatalEvenOnSuccessStatus_AndNotRetried()
    {
        RecordingHandler handler = new((_, _) =>
            Task.FromResult(RecordingHandler.Rejected("relay session is unknown")));

        DeliveryException? ex = await Assert.ThrowsAsync<DeliveryException>(async () =>
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
    public async Task EventBeforeHello_IsAnInputError()
    {
        RecordingHandler handler = new();

        InputException? ex = await Assert.ThrowsAsync<InputException>(async () =>
            await Run.RunAsync(Run.Settings(), Binary([Frames.Event(0)]), handler));

        Assert.Multiple(() =>
        {
            Assert.That(ex!.Message, Does.Contain("hello"));
            Assert.That(handler.Requests, Is.Empty);
        });
    }

    [Test]
    public async Task FramesAfterEnd_NeedANewHello()
    {
        List<byte[]> input = [.. Frames.Session(Guid.NewGuid(), 1), Frames.Event(9)];

        await Assert.ThrowsAsync<InputException>(async () =>
            await Run.RunAsync(Run.Settings(), Binary(input), new RecordingHandler()));
    }

    [Test]
    public async Task Garbage_ExplainsTheTextPipelineProblemOnFirstFrame()
    {
        // What a pipeline that treated the bytes as text would produce: valid UTF-8 text, wrong magic.
        byte[] text = System.Text.Encoding.UTF8.GetBytes(new string('x', 64));

        InputException? ex = await Assert.ThrowsAsync<InputException>(async () =>
            await Run.RunAsync(Run.Settings(), new MemoryStream(text), new RecordingHandler()));

        Assert.That(ex!.Message, Does.Contain("PowerShell 7.4").And.Contain("relay-ndjson"));
    }

    [Test]
    public async Task FramesBeforeABrokenFrame_AreStillDelivered()
    {
        Guid session = Guid.NewGuid();
        byte[] input = [.. Frames.Concat([Frames.Hello(session), Frames.Event(1)]), .. new byte[32]];
        RecordingHandler handler = new();

        await Assert.ThrowsAsync<InputException>(async () =>
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
    public async Task Abort_WhileDeliveryIsStuck_IsADeliveryFailure()
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
        DeliveryException? ex = await Assert.ThrowsAsync<DeliveryException>(async () => await running);
        Assert.That(ex!.Message, Does.Contain("Interrupted"));
    }

    [Test]
    public async Task MalformedAcknowledgement_IsNotTreatedAsDelivered()
    {
        RecordingHandler handler = new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([0x80])
        }));

        DeliveryException? ex = await Assert.ThrowsAsync<DeliveryException>(async () =>
            await Run.RunAsync(Run.Settings(), Binary(Frames.Session(Guid.NewGuid(), 1)), handler));

        Assert.Multiple(() =>
        {
            Assert.That(handler.Requests, Has.Count.EqualTo(1));
            Assert.That(ex!.Message, Does.Contain("acknowledgement"));
        });
    }

    [Test]
    public async Task UnreadableRejectionBody_StaysAPermanentFailure()
    {
        RecordingHandler handler = new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new ThrowingContent()
        }));

        await Assert.ThrowsAsync<DeliveryException>(async () =>
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
