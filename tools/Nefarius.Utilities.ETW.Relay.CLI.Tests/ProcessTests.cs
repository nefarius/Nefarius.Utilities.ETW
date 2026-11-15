using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

using Google.Protobuf;

using OpenTelemetry.Proto.Collector.Logs.V1;

namespace Nefarius.Utilities.ETW.Relay.CLI.Tests;

/// <summary>
///     Runs the real <c>etwrelay</c> process against a loopback HTTP server, with frames on its standard input.
/// </summary>
public class ProcessTests
{
    private HttpListener _listener = null!;
    private string _endpoint = null!;
    private readonly List<byte[]> _bodies = [];
    private Func<int, HttpStatusCode> _status = _ => HttpStatusCode.OK;
    private Task _serving = Task.CompletedTask;
    private volatile bool _sawGzip;

    [SetUp]
    public void StartServer()
    {
        TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        _endpoint = $"http://127.0.0.1:{port}";
        _listener = new HttpListener();
        _listener.Prefixes.Add(_endpoint + "/");
        _listener.Start();
        _bodies.Clear();
        _sawGzip = false;
        _status = _ => HttpStatusCode.OK;

        _serving = Task.Run(async () =>
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
                {
                    return;
                }

                using MemoryStream body = new();
                await context.Request.InputStream.CopyToAsync(body);
                byte[] received = body.ToArray();
                if (context.Request.Headers["Content-Encoding"] == "gzip")
                {
                    _sawGzip = true;
                    using System.IO.Compression.GZipStream gzip = new(new MemoryStream(received),
                        System.IO.Compression.CompressionMode.Decompress);
                    using MemoryStream plain = new();
                    await gzip.CopyToAsync(plain);
                    received = plain.ToArray();
                }

                int index;
                lock (_bodies)
                {
                    index = _bodies.Count;
                    _bodies.Add(received);
                }

                context.Response.StatusCode = (int)_status(index);
                byte[] reply = new ExportLogsServiceResponse().ToByteArray();
                context.Response.ContentLength64 = reply.Length;
                await context.Response.OutputStream.WriteAsync(reply);
                context.Response.Close();
            }
        });
    }

    [TearDown]
    public async Task StopServer()
    {
        _listener.Close();
        await _serving;
    }

    private async Task<(int Code, string Stderr)> RunTool(byte[] stdin, params string[] args)
    {
        string dll = Path.Combine(AppContext.BaseDirectory, "etwrelay.dll");
        Assert.That(File.Exists(dll), Is.True, $"{dll} must be copied next to the tests.");

        ProcessStartInfo info = new("dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        info.ArgumentList.Add(dll);
        info.ArgumentList.Add("--endpoint");
        info.ArgumentList.Add(_endpoint);
        foreach (string arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        using Process process = Process.Start(info)!;
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();

        await process.StandardInput.BaseStream.WriteAsync(stdin);
        process.StandardInput.Close();

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(60));
        await process.WaitForExitAsync(timeout.Token);
        Assert.That(await stdout, Is.Empty, "Diagnostics belong on stderr; stdout stays clean.");
        return (process.ExitCode, await stderr);
    }

    private List<byte[]> DeliveredFrames()
    {
        lock (_bodies)
        {
            return _bodies.SelectMany(b => new CapturedRequest(new Uri(_endpoint), b, null, null, []).FrameBytes())
                .ToList();
        }
    }

    [Test]
    public async Task BinaryStdin_IsDelivered_ExitZero()
    {
        List<byte[]> input = Frames.Session(Guid.NewGuid(), 5);

        (int code, string stderr) = await RunTool(Frames.Concat(input));

        Assert.Multiple(() =>
        {
            Assert.That(code, Is.EqualTo(0), stderr);
            Assert.That(DeliveredFrames().Select(Convert.ToBase64String),
                Is.EqualTo(input.Select(Convert.ToBase64String)));
        });
    }

    [Test]
    public async Task Base64NdjsonStdin_IsDelivered_ExitZero()
    {
        List<byte[]> input = Frames.Session(Guid.NewGuid(), 5);

        (int code, string stderr) = await RunTool(Frames.Ndjson(input, "\r\n"), "--input-format", "base64-ndjson",
            "--gzip", "--resource", "service.name=test");

        Assert.Multiple(() =>
        {
            Assert.That(code, Is.EqualTo(0), stderr);
            Assert.That(_sawGzip, Is.True);
            Assert.That(DeliveredFrames().Select(Convert.ToBase64String),
                Is.EqualTo(input.Select(Convert.ToBase64String)));
        });
    }

    [Test]
    public async Task InvalidInput_ExitsTwo()
    {
        (int code, string stderr) = await RunTool(new byte[64]);

        Assert.Multiple(() =>
        {
            Assert.That(code, Is.EqualTo(2));
            Assert.That(stderr, Does.Contain("[!]"));
        });
    }

    [Test]
    public async Task InvalidUsage_ExitsTwo()
    {
        (int code, string stderr) = await RunTool([], "--header", "Authorization=Bearer x");

        Assert.Multiple(() =>
        {
            Assert.That(code, Is.EqualTo(2));
            Assert.That(stderr, Does.Contain("https"));
        });
    }

    [Test]
    public async Task RejectedByTheGateway_ExitsOne()
    {
        _status = _ => HttpStatusCode.BadRequest;

        (int code, string stderr) = await RunTool(Frames.Concat(Frames.Session(Guid.NewGuid(), 1)));

        Assert.Multiple(() =>
        {
            Assert.That(code, Is.EqualTo(1));
            Assert.That(stderr, Does.Contain("400"));
            lock (_bodies)
            {
                Assert.That(_bodies, Has.Count.EqualTo(1), "A 400 is not retried.");
            }
        });
    }

    [Test]
    public async Task TransientFailures_AreRetriedByTheRealProcess()
    {
        _status = index => index == 0 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK;

        (int code, string stderr) = await RunTool(Frames.Concat(Frames.Session(Guid.NewGuid(), 1)));

        Assert.Multiple(() =>
        {
            Assert.That(code, Is.EqualTo(0), stderr);
            lock (_bodies)
            {
                Assert.That(_bodies, Has.Count.EqualTo(2));
                Assert.That(_bodies[1], Is.EqualTo(_bodies[0]), "The retry carries identical bytes.");
            }
        });
    }

    // ---------------------------------------------------------------------------------------------------------------
    // The whole pipeline: etwutils capture -> binary pipe -> etwrelay -> gateway (Windows, elevated)
    // ---------------------------------------------------------------------------------------------------------------

    [Test]
    [Category("Realtime")]
    [Category("RequiresAdmin")]
    [Category("EndToEnd")]
    public async Task RealtimeCapture_FlowsThroughTheBinaryPipe()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("Real-time ETW capture is Windows only.");
        }

        if (!OperatingSystem.IsWindows() || !IsElevated())
        {
            Assert.Ignore("Test requires administrator privileges.");
        }

        const string session = "Nefarius.ETW.Tests.RelayPipeline";
        string etwutils = Path.Combine(AppContext.BaseDirectory, "etwutils.dll");
        string etwrelay = Path.Combine(AppContext.BaseDirectory, "etwrelay.dll");
        Assert.That(File.Exists(etwutils) && File.Exists(etwrelay), Is.True);

        using Process relay = StartDotnet(etwrelay, "--endpoint", _endpoint, "--flush-ms", "50");
        using Process capture = StartDotnet(etwutils, "realtime", "--format", "relay", "--session-name", session,
            "22FB2CD6-0E7B-422B-A0C7-2FAD1FD0E716");
        Task relayErr = relay.StandardError.ReadToEndAsync();
        Task captureErr = capture.StandardError.ReadToEndAsync();

        // The real binary pipe: bytes from one process straight into the other.
        Task pump = Task.Run(async () =>
        {
            await capture.StandardOutput.BaseStream.CopyToAsync(relay.StandardInput.BaseStream);
            relay.StandardInput.Close();
        });

        try
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(60));
            while (!timeout.IsCancellationRequested && DeliveredFrames().Count < 3)
            {
                // Process creation is a reliable source of Kernel-Process events.
                using (Process.Start(new ProcessStartInfo("cmd", "/c exit") { CreateNoWindow = true })!)
                {
                }

                await Task.Delay(300);
            }

            List<byte[]> frames = DeliveredFrames();
            Assert.That(frames, Has.Count.GreaterThanOrEqualTo(3), "Capture did not produce frames in time.");
            Assert.Multiple(() =>
            {
                Assert.That(RawEtwFrameCodec.Decode(frames[0]).Type, Is.EqualTo(RawEtwFrameType.Hello));
                Assert.That(frames.Skip(1).Select(f => RawEtwFrameCodec.Decode(f).Type),
                    Has.Some.EqualTo(RawEtwFrameType.Event));
            });
        }
        finally
        {
            capture.Kill(true);
            await Task.WhenAny(pump, Task.Delay(TimeSpan.FromSeconds(10)));
            if (!relay.HasExited)
            {
                relay.StandardInput.Close();
                relay.WaitForExit(TimeSpan.FromSeconds(15));
                if (!relay.HasExited)
                {
                    relay.Kill(true);
                }
            }

            // The killed capture process could not stop its own session.
            using (Process.Start(new ProcessStartInfo("logman", $"stop {session} -ets")
                   { CreateNoWindow = true, RedirectStandardOutput = true })!)
            {
            }
        }
    }

    [TestCase("--header")]
    [TestCase("--columns", "Timestamp")]
    [TestCase("--filter", "true")]
    [TestCase("--color", "always")]
    [TestCase("--keep-original-provider")]
    public async Task EtwutilsRelayFormats_RejectDecodingAndPresentationOptions(params string[] option)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("etwutils realtime is Windows only.");
        }

        foreach (string format in new[] { "relay", "relay-ndjson" })
        {
            string[] args = ["realtime", "--format", format, "22FB2CD6-0E7B-422B-A0C7-2FAD1FD0E716", .. option];
            using Process process = StartDotnet(Path.Combine(AppContext.BaseDirectory, "etwutils.dll"), args);
            process.StandardInput.Close();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            Assert.That(process.ExitCode, Is.EqualTo(2), format);
            Assert.That(await stderr, Does.Contain("cannot be combined with --format " + format));
        }
    }
    private static Process StartDotnet(string dll, params string[] args)
    {
        ProcessStartInfo info = new("dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        info.ArgumentList.Add(dll);
        foreach (string arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        return Process.Start(info)!;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static bool IsElevated()
    {
        using System.Security.Principal.WindowsIdentity identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        return new System.Security.Principal.WindowsPrincipal(identity)
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }
}