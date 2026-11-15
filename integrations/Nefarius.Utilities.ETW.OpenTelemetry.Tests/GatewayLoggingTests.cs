using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

using Nefarius.Utilities.ETW.OtlpGateway;
using Nefarius.Utilities.ETW.Relay;

using NUnit.Framework;

using Serilog.Events;

namespace Nefarius.Utilities.ETW.OpenTelemetry.Tests;

/// <summary>Request and relay-session diagnostics emitted by the gateway.</summary>
public class GatewayLoggingTests
{
    [Test]
    public void RequestLogging_SuppressesSuccessfulHealthChecksAndKeepsFailures()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Level("/healthz", StatusCodes.Status200OK), Is.EqualTo(LogEventLevel.Verbose));
            Assert.That(Level("/healthz", StatusCodes.Status503ServiceUnavailable), Is.EqualTo(LogEventLevel.Error));
            Assert.That(Level("/healthz", StatusCodes.Status404NotFound), Is.EqualTo(LogEventLevel.Warning));
            Assert.That(Level("/healthz", StatusCodes.Status200OK, new InvalidOperationException("down")),
                Is.EqualTo(LogEventLevel.Error));
            Assert.That(Level("/v1/logs", StatusCodes.Status200OK), Is.EqualTo(LogEventLevel.Information));
            Assert.That(Level("/v1/logs", StatusCodes.Status400BadRequest), Is.EqualTo(LogEventLevel.Warning));
            Assert.That(Level("/v1/logs", StatusCodes.Status500InternalServerError), Is.EqualTo(LogEventLevel.Error));
        });
    }

    [Test]
    public async Task RequestLogging_EmitsOneCompletionAndSkipsSuccessfulHealthChecks()
    {
        await using TestGateway gateway = await TestGateway.StartAsync(captureSerilogRequests: true);

        HttpResponseMessage health = await gateway.Client.GetAsync("/healthz");
        Assert.That(health.IsSuccessStatusCode, Is.True);

        Guid session = Guid.NewGuid();
        (HttpResponseMessage logs, _) = await gateway.PostAsync(
            Frames.Request(session, Frames.Hello(session), Frames.End(1)));
        Assert.That(logs.IsSuccessStatusCode, Is.True);

        LogEvent[] completions = gateway.SerilogEvents!
            .Where(e => e.MessageTemplate.Text.StartsWith("HTTP ", StringComparison.Ordinal))
            .ToArray();
        LogEvent request = completions.Single(e => e.Level >= LogEventLevel.Information);
        Assert.That(request.RenderMessage(), Does.Contain("/v1/logs"));
        Assert.That(completions.Any(e =>
            e.Level >= LogEventLevel.Information && e.RenderMessage().Contains("/healthz", StringComparison.Ordinal)),
            Is.False);
    }

    [Test]
    public async Task HelloProducer_ControlCharactersAreEscaped()
    {
        await using TestGateway gateway = await TestGateway.StartAsync(captureLogs: true);
        Guid session = Guid.NewGuid();
        byte[] hello = RawEtwFrameCodec.Encode(new RawEtwFrame
        {
            Type = RawEtwFrameType.Hello,
            Hello = new RawEtwHello
            {
                SessionId = session,
                MaxFrameLength = 1 << 20,
                Producer = "bad\r\n\u001b[31mproducer\u2028"
            }
        });

        (HttpResponseMessage response, _) = await gateway.PostAsync(Frames.Request(session, hello, Frames.End(1)));
        Assert.That(response.IsSuccessStatusCode, Is.True);

        CapturedLog log = gateway.Logs!.Entries.Single(e => e.Message.Contains("Relay hello accepted"));
        Assert.Multiple(() =>
        {
            Assert.That(log.Message, Does.Contain(session.ToString()));
            Assert.That(log.Message, Does.Contain("anonymous"));
            Assert.That(log.Message, Does.Contain((1 << 20).ToString()));
            Assert.That(log.Message, Does.Contain("bad\\u000D\\u000A\\u001B[31mproducer\\u2028"));
            Assert.That(log.Message.Any(char.IsControl), Is.False);
            Assert.That(log.Message.Contains('\u2028'), Is.False);
            Assert.That(log.Message.Contains('\u2029'), Is.False);
        });
    }

    [Test]
    public async Task AcceptedHelloAndEnd_AreLoggedOnce()
    {
        await using TestGateway gateway = await TestGateway.StartAsync(authenticated: true, captureLogs: true);
        Guid session = Guid.NewGuid();
        byte[] hello = Frames.Hello(session);
        byte[] end = Frames.End(1);

        (HttpResponseMessage opened, _) = await gateway.PostAsync(Frames.Request(session, hello, end), "alice");
        (HttpResponseMessage replayed, _) = await gateway.PostAsync(Frames.Request(session, hello, end), "alice");
        Assert.That(opened.IsSuccessStatusCode, Is.True);
        Assert.That(replayed.IsSuccessStatusCode, Is.True);

        CapturedLog[] life = SessionLogs(gateway);
        CapturedLog helloLog = life.Single(e => e.Message.Contains("Relay hello accepted"));
        CapturedLog endLog = life.Single(e => e.Message.Contains("Relay end accepted"));
        Assert.Multiple(() =>
        {
            Assert.That(helloLog.Level, Is.EqualTo(LogLevel.Information));
            Assert.That(helloLog.Message, Does.Contain(session.ToString()));
            Assert.That(helloLog.Message, Does.Contain("alice"));
            Assert.That(helloLog.Message, Does.Contain("unit-test"));
            Assert.That(helloLog.Message, Does.Contain((1 << 20).ToString()));
            Assert.That(endLog.Level, Is.EqualTo(LogLevel.Information));
            Assert.That(endLog.Message, Does.Contain(session.ToString()));
            Assert.That(endLog.Message, Does.Contain("alice"));
        });

        Guid rejected = Guid.NewGuid();
        byte[] oversized = RawEtwFrameCodec.Encode(new RawEtwFrame
        {
            Type = RawEtwFrameType.Hello,
            Hello = new RawEtwHello { SessionId = rejected, MaxFrameLength = 50_000_000, Producer = "too-big" }
        });
        await gateway.PostAsync(Frames.Request(rejected, oversized), "alice");
        await gateway.PostAsync(Frames.Request(Guid.NewGuid(), Frames.End(0)), "alice");

        CapturedLog[] afterRejection = SessionLogs(gateway);
        Assert.Multiple(() =>
        {
            Assert.That(afterRejection.Count(e => e.Message.Contains("Relay hello accepted")), Is.EqualTo(1));
            Assert.That(afterRejection.Count(e => e.Message.Contains("Relay end accepted")), Is.EqualTo(1));
            Assert.That(afterRejection.Any(e => e.Message.Contains(rejected.ToString())), Is.False);
            Assert.That(afterRejection.Any(e => e.Message.Contains("too-big")), Is.False);
        });
    }

    private static LogEventLevel Level(string path, int status, Exception? error = null)
    {
        DefaultHttpContext context = new();
        context.Request.Path = path;
        context.Response.StatusCode = status;
        return GatewayRequestLogging.GetLevel(context, error);
    }

    private static CapturedLog[] SessionLogs(TestGateway gateway) =>
        gateway.Logs!.Entries.Where(e => e.Category == "RelaySessions").ToArray();
}
