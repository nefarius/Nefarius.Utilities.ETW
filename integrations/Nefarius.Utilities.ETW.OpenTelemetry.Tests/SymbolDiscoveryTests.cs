using System.Collections.Concurrent;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using Nefarius.Utilities.ETW.Deserializer.CustomParsers;
using Nefarius.Utilities.ETW.Deserializer.WPP;
using Nefarius.Utilities.ETW.Relay;
using Nefarius.Utilities.ETW.Symbols;

using NUnit.Framework;

using OpenTelemetry.Proto.Logs.V1;

namespace Nefarius.Utilities.ETW.OpenTelemetry.Tests;

/// <summary>
///     Gateway symbol-server discovery: RSDS identities in relayed events drive downloads from the operator's
///     configured server into the shared WPP context.
/// </summary>
public class SymbolDiscoveryTests
{
    private const string Trace = "./traces/BthPS3_0.etl";

    private static readonly string SymbolsDirectory = Path.GetFullPath("./symbols");

    private string _cache = null!;

    [SetUp]
    public void SetUp()
    {
        if (!File.Exists(Trace))
        {
            Assert.Ignore($"{Trace} is not available.");
        }

        _cache = Path.Combine(Path.GetTempPath(), "etw-symbol-tests-" + Guid.NewGuid().ToString("N"));
    }

    [TearDown]
    public void TearDown()
    {
        if (_cache is not null && Directory.Exists(_cache))
        {
            Directory.Delete(_cache, true);
        }
    }

    private static Guid NewSession() => Guid.NewGuid();

    private static IEnumerable<(string File, PdbMetaData Identity)> TestPdbs()
    {
        foreach (string file in Directory.GetFiles(SymbolsDirectory, "*.pdb"))
        {
            Assert.That(PdbFileIdentityReader.TryRead(file, out PdbFileIdentity id), Is.True);
            yield return (file, new PdbMetaData { PdbName = Path.GetFileName(file), Guid = id.Guid, Age = id.Age });
        }
    }

    private static byte[] RsdsFrame(ulong sequence, PdbMetaData pdb, string? pathPrefix = null)
    {
        byte[] name = [.. System.Text.Encoding.ASCII.GetBytes((pathPrefix ?? string.Empty) + pdb.PdbName), 0];
        return RawEtwFrameCodec.Encode(new RawEtwFrame
        {
            Type = RawEtwFrameType.Event,
            Sequence = sequence,
            Record = new RawEtwRecord
            {
                ProviderId = CustomParserGuids.KernelTraceControlImageIdGuid,
                Opcode = 36,
                TimeStamp = 133_500_000_000_000_000,
                Payload =
                [
                    .. BitConverter.GetBytes(0x7ff600000000UL), .. BitConverter.GetBytes(10u),
                    .. pdb.Guid.ToByteArray(), .. BitConverter.GetBytes((uint)pdb.Age), .. name
                ]
            }
        });
    }

    /// <summary>The ETL's raw events (no hello/end).</summary>
    private static List<RawEtwFrame> EtlEvents(bool includeRsds = true) =>
        Frames.FromEtl(Trace, Guid.NewGuid()).Select(f => RawEtwFrameCodec.Decode(f))
            .Where(f => f.Type == RawEtwFrameType.Event)
            .Where(f => includeRsds || !Nefarius.Utilities.ETW.Events.RawRsdsExtractor.TryExtract(f.Record, out _))
            .ToList();

    /// <summary>Hello, then the given frames re-sequenced, as one gapless stream.</summary>
    private static List<byte[]> Stream(Guid session, IEnumerable<RawEtwFrame> frames)
    {
        List<byte[]> result = [Frames.Hello(session)];
        ulong sequence = 1;
        foreach (RawEtwFrame frame in frames)
        {
            frame.Sequence = sequence++;
            result.Add(RawEtwFrameCodec.Encode(frame));
        }

        return result;
    }

    private static RawEtwFrame Rsds(PdbMetaData pdb) =>
        RawEtwFrameCodec.Decode(RsdsFrame(0, pdb));

    private static int ExpectedRecords(IEnumerable<byte[]> frames)
    {
        int count = 0;
        using RawEtwRelayDecoder decoder = new();
        foreach (byte[] frame in frames)
        {
            decoder.Process(frame, _ => count++);
        }

        return count;
    }

    private static async Task PostAll(TestGateway gateway, Guid session, IEnumerable<byte[]> frames)
    {
        foreach (byte[][] batch in frames.Chunk(200))
        {
            (HttpResponseMessage response, _) = await gateway.PostAsync(Frames.Request(session, batch));
            Assert.That(response.IsSuccessStatusCode, Is.True);
        }
    }

    private static string?[] WppBodies(TestGateway gateway) =>
        gateway.Sink.Records.Where(r => r.EventName == "WPP").Select(r => r.Body.StringValue).ToArray();

    private static bool IsFormatted(string? body) => !string.IsNullOrEmpty(body) && !body.StartsWith("GUID=");

    private Dictionary<string, string?> Settings(StubSymbolServer server) => new()
    {
        ["Gateway:Wpp:SymbolServer"] = server.Url,
        ["Gateway:Wpp:SymbolCache"] = _cache
    };

    [Test]
    [Category("EndToEnd")]
    public async Task SameRequest_DiscoveryHappensBeforeDecoding()
    {
        await using StubSymbolServer server = await StubSymbolServer.StartAsync();
        await using TestGateway gateway = await TestGateway.StartAsync(Settings(server));

        Guid session = NewSession();
        List<byte[]> frames = Stream(session, TestPdbs().Select(p => Rsds(p.Identity)).Concat(EtlEvents()));
        await PostAll(gateway, session, frames);

        await gateway.Sink.WaitForRecordsAsync(ExpectedRecords(frames));
        Assert.That(WppBodies(gateway).Any(IsFormatted), Is.True, "Symbols must be loaded before the same request decodes.");
        Assert.That(server.Requests, Is.EquivalentTo(TestPdbs().Select(p => "/" + p.Identity.IndexPrefix)).IgnoreCase);
    }

    [Test]
    [Category("EndToEnd")]
    public async Task LaterRequests_BenefitButEarlierRecordsAreNotReplayed()
    {
        await using StubSymbolServer server = await StubSymbolServer.StartAsync();
        await using TestGateway gateway = await TestGateway.StartAsync(Settings(server));

        // The events arrive first (the ETL's own RSDS rundown records are left out) and only get placeholders.
        Guid early = NewSession();
        List<byte[]> beforeSymbols = Stream(early, EtlEvents(includeRsds: false));
        await PostAll(gateway, early, beforeSymbols);
        await gateway.Sink.WaitForRecordsAsync(ExpectedRecords(beforeSymbols));
        Assert.That(WppBodies(gateway), Has.All.StartWith("GUID="));
        int earlierCount = gateway.Sink.Records.Count;

        // The identity arrives later; nothing is replayed for the earlier session.
        Guid announce = NewSession();
        await PostAll(gateway, announce, Stream(announce, TestPdbs().Select(p => Rsds(p.Identity))));
        await WaitFor(() => server.Requests.Count >= TestPdbs().Count());
        Assert.That(WppBodies(gateway).Count(IsFormatted), Is.Zero);

        // A new session now decodes with the loaded symbols.
        Guid later = NewSession();
        List<byte[]> afterSymbols = Stream(later, EtlEvents(includeRsds: false));
        await PostAll(gateway, later, afterSymbols);
        await gateway.Sink.WaitForRecordsAsync(earlierCount + ExpectedRecords(afterSymbols));
        Assert.That(WppBodies(gateway).Any(IsFormatted), Is.True);
    }

    [Test]
    [Category("EndToEnd")]
    public async Task EventsOfOtherSessions_WaitForSymbolsStillResolving()
    {
        await using StubSymbolServer server = await StubSymbolServer.StartAsync(delay: TimeSpan.FromMilliseconds(500));
        await using TestGateway gateway = await TestGateway.StartAsync(Settings(server));

        // Session A only announces the images; the downloads are slow.
        Guid announce = NewSession();
        Task announcing = PostAll(gateway, announce, Stream(announce, TestPdbs().Select(p => Rsds(p.Identity))));
        await WaitFor(() => server.Requests.Count > 0);

        // Session B carries WPP events and no identity; it is queued in memory until the symbols are in.
        Guid session = NewSession();
        List<byte[]> frames = Stream(session, EtlEvents(includeRsds: false));
        await PostAll(gateway, session, frames);
        await announcing;

        await gateway.Sink.WaitForRecordsAsync(ExpectedRecords(frames));
        Assert.That(WppBodies(gateway).Any(IsFormatted), Is.True);
    }

    [Test]
    [Category("EndToEnd")]
    public async Task TransientFailures_AreRetriedUntilTheyWork()
    {
        await using StubSymbolServer server = await StubSymbolServer.StartAsync(StubMode.FailFirstRequest);
        await using TestGateway gateway = await TestGateway.StartAsync(Settings(server));

        Guid session = NewSession();
        List<byte[]> frames = Stream(session, TestPdbs().Select(p => Rsds(p.Identity)).Concat(EtlEvents()));
        await PostAll(gateway, session, frames);

        await gateway.Sink.WaitForRecordsAsync(ExpectedRecords(frames));
        Assert.That(WppBodies(gateway).Any(IsFormatted), Is.True);
        Assert.That(server.Requests.Count, Is.GreaterThan(TestPdbs().Count()));
    }

    [Test]
    [Category("EndToEnd")]
    public async Task PermanentFailures_AreNotRetried()
    {
        await using StubSymbolServer server = await StubSymbolServer.StartAsync(StubMode.NotFound);
        await using TestGateway gateway = await TestGateway.StartAsync(Settings(server));

        for (int i = 0; i < 3; i++)
        {
            Guid session = NewSession();
            await PostAll(gateway, session, Stream(session, TestPdbs().Select(p => Rsds(p.Identity))));
        }

        Assert.That(server.Requests, Has.Count.EqualTo(TestPdbs().Count()));
    }

    [Test]
    [Category("EndToEnd")]
    public async Task WaitingIsBounded_ThenDecodesWithPlaceholders()
    {
        await using StubSymbolServer server = await StubSymbolServer.StartAsync(delay: TimeSpan.FromSeconds(5));
        Dictionary<string, string?> settings = Settings(server);
        settings["Gateway:Wpp:SymbolWaitTimeout"] = "00:00:00.300";
        await using TestGateway gateway = await TestGateway.StartAsync(settings);

        Guid session = NewSession();
        List<byte[]> frames = Stream(session, TestPdbs().Select(p => Rsds(p.Identity)).Concat(EtlEvents()));
        System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
        await PostAll(gateway, session, frames);

        Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(4)));
        await gateway.Sink.WaitForRecordsAsync(ExpectedRecords(frames));
        Assert.That(WppBodies(gateway), Has.All.StartWith("GUID="));
    }

    [Test]
    [Category("EndToEnd")]
    public async Task Cache_IsReusedAcrossRestarts()
    {
        await using StubSymbolServer server = await StubSymbolServer.StartAsync();
        int expected = TestPdbs().Count();

        for (int run = 0; run < 2; run++)
        {
            await using TestGateway gateway = await TestGateway.StartAsync(Settings(server));
            Guid session = NewSession();
            List<byte[]> frames = Stream(session, TestPdbs().Select(p => Rsds(p.Identity)).Concat(EtlEvents()));
            await PostAll(gateway, session, frames);
            await gateway.Sink.WaitForRecordsAsync(ExpectedRecords(frames));
            Assert.That(WppBodies(gateway).Any(IsFormatted), Is.True);
        }

        Assert.That(server.Requests, Has.Count.EqualTo(expected), "The second run must be served from the cache.");
    }

    [Test]
    [Category("EndToEnd")]
    public async Task ConcurrentRequests_ShareOneDownloadPerIdentity()
    {
        await using StubSymbolServer server = await StubSymbolServer.StartAsync(delay: TimeSpan.FromMilliseconds(300));
        await using TestGateway gateway = await TestGateway.StartAsync(Settings(server));

        Task[] posts = Enumerable.Range(0, 6).Select(_ =>
        {
            Guid session = NewSession();
            return PostAll(gateway, session, Stream(session, TestPdbs().Select(p => Rsds(p.Identity))));
        }).ToArray();
        await Task.WhenAll(posts);

        Assert.That(server.Requests, Has.Count.EqualTo(TestPdbs().Count()));
        Assert.That(server.Requests.Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            Is.EqualTo(TestPdbs().Count()));
    }

    [TestCase(StubMode.NotFound)]
    [TestCase(StubMode.Garbage)]
    [TestCase(StubMode.WrongIdentity)]
    [TestCase(StubMode.ServerError)]
    [Category("EndToEnd")]
    public async Task FailedDownloads_AreNonFatal(StubMode mode)
    {
        await using StubSymbolServer server = await StubSymbolServer.StartAsync(mode);
        await using TestGateway gateway = await TestGateway.StartAsync(Settings(server));

        Guid session = NewSession();
        List<byte[]> frames = Stream(session, TestPdbs().Select(p => Rsds(p.Identity)).Concat(EtlEvents()));
        await PostAll(gateway, session, frames);

        await gateway.Sink.WaitForRecordsAsync(ExpectedRecords(frames));
        Assert.That(WppBodies(gateway), Is.Not.Empty);
        Assert.That(WppBodies(gateway), Has.All.StartWith("GUID="));
        Assert.That(server.Requests, Is.Not.Empty);
        if (Directory.Exists(_cache))
        {
            Assert.That(Directory.GetFiles(_cache, "*.pdb", SearchOption.AllDirectories), Is.Empty,
                "Rejected downloads must not enter the cache.");
        }
    }

    [Test]
    [Category("EndToEnd")]
    public async Task Redirects_AreNotFollowed()
    {
        await using StubSymbolServer server = await StubSymbolServer.StartAsync(StubMode.Redirect);
        await using TestGateway gateway = await TestGateway.StartAsync(Settings(server));

        Guid session = NewSession();
        List<byte[]> frames = Stream(session, TestPdbs().Select(p => Rsds(p.Identity)).Concat(EtlEvents()));
        await PostAll(gateway, session, frames);
        await gateway.Sink.WaitForRecordsAsync(ExpectedRecords(frames));

        Assert.That(server.Requests.Any(r => r.StartsWith("/elsewhere")), Is.False);
        Assert.That(WppBodies(gateway), Has.All.StartWith("GUID="));
    }

    [Test]
    [Category("EndToEnd")]
    public async Task RelayData_CannotChooseTheDownloadUrl()
    {
        await using StubSymbolServer server = await StubSymbolServer.StartAsync();
        await using TestGateway gateway = await TestGateway.StartAsync(Settings(server));

        PdbMetaData pdb = TestPdbs().First().Identity;
        Guid session = NewSession();
        // Only the leaf name is used, and always against the configured server; syntax-carrying names are dropped.
        List<RawEtwFrame> hostile =
        [
            RawEtwFrameCodec.Decode(RsdsFrame(0, pdb with { PdbName = "http://evil.example/x.pdb" })),
            RawEtwFrameCodec.Decode(RsdsFrame(0, pdb with { PdbName = "a?b.pdb" })),
            RawEtwFrameCodec.Decode(RsdsFrame(0, pdb with { PdbName = ".." }))
        ];
        await PostAll(gateway, session, Stream(session, hostile));

        Assert.That(server.Requests,
            Is.EqualTo(new[] { "/" + (pdb with { PdbName = "x.pdb" }).IndexPrefix }).IgnoreCase);
    }

    [Test]
    public async Task InvalidConfiguration_FailsAtStartup()
    {
        foreach (string url in new[] { "ftp://symbols.example/", "not a url" })
        {
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                TestGateway gateway = await TestGateway.StartAsync(new()
                {
                    ["Gateway:Wpp:SymbolServer"] = url,
                    ["Gateway:Wpp:SymbolCache"] = _cache
                });
                await gateway.DisposeAsync();
            });
        }

        await Task.CompletedTask;
    }

    [Test]
    public async Task WithoutSymbolServer_NothingIsContacted()
    {
        await using StubSymbolServer server = await StubSymbolServer.StartAsync();
        await using TestGateway gateway = await TestGateway.StartAsync();

        Guid session = NewSession();
        await PostAll(gateway, session, Stream(session, TestPdbs().Select(p => Rsds(p.Identity))));

        Assert.That(server.Requests, Is.Empty);
    }

    [Test]
    [Category("EndToEnd")]
    public async Task SymbolLookup_LogsEachAttemptAndSuccessfulLoad()
    {
        await using StubSymbolServer server = await StubSymbolServer.StartAsync();
        await using TestGateway gateway = await TestGateway.StartAsync(Settings(server), captureLogs: true);

        Guid session = NewSession();
        await PostAll(gateway, session, Stream(session, TestPdbs().Select(p => Rsds(p.Identity))));

        CapturedLog[] logs = SymbolLogs(gateway);
        Assert.That(logs.Count(e => e.Level == LogLevel.Debug && e.Message.Contains("attempt 1 of")),
            Is.EqualTo(TestPdbs().Count()));
        Assert.That(logs.Count(e => e.Message.Contains("Loaded symbols for")), Is.EqualTo(TestPdbs().Count()));
        Assert.That(logs.Where(e => e.Message.Contains("Loaded symbols for")).Select(e => e.Level),
            Has.All.EqualTo(LogLevel.Information));
    }

    [Test]
    [Category("EndToEnd")]
    public async Task SymbolLookup_LogsRetriesBeforeTheSuccessfulAttempt()
    {
        await using StubSymbolServer server = await StubSymbolServer.StartAsync(StubMode.FailFirstRequest);
        await using TestGateway gateway = await TestGateway.StartAsync(Settings(server), captureLogs: true);

        Guid session = NewSession();
        await PostAll(gateway, session, Stream(session, TestPdbs().Select(p => Rsds(p.Identity))));

        CapturedLog[] logs = SymbolLogs(gateway);
        Assert.That(
            logs.Count(e => e.Level == LogLevel.Debug && e.Message.Contains("retrying") && e.Message.Contains("503")),
            Is.EqualTo(TestPdbs().Count()));
        Assert.That(logs.Count(e => e.Level == LogLevel.Debug && e.Message.Contains("attempt 2 of")),
            Is.EqualTo(TestPdbs().Count()));
        Assert.That(logs.Count(e => e.Message.Contains("Loaded symbols for")), Is.EqualTo(TestPdbs().Count()));
    }

    [Test]
    [Category("EndToEnd")]
    public async Task SymbolLookup_LogsTheFailureDetail()
    {
        await using StubSymbolServer server = await StubSymbolServer.StartAsync(StubMode.NotFound);
        await using TestGateway gateway = await TestGateway.StartAsync(Settings(server), captureLogs: true);

        Guid session = NewSession();
        await PostAll(gateway, session, Stream(session, TestPdbs().Select(p => Rsds(p.Identity))));

        CapturedLog[] warnings = SymbolLogs(gateway).Where(e => e.Level == LogLevel.Warning).ToArray();
        Assert.That(warnings, Has.Length.EqualTo(TestPdbs().Count()));
        foreach (CapturedLog warning in warnings)
        {
            Assert.That(warning.Message, Does.Contain("NotFoundOnServer"));
            Assert.That(warning.Message, Does.Contain("after 1 attempt"));
            Assert.That(warning.Message, Does.Contain(server.Url));
        }

        Assert.That(SymbolLogs(gateway).Any(e => e.Message.Contains("retrying")), Is.False);
        Assert.That(SymbolLogs(gateway).Any(e => e.Message.Contains("Loaded symbols for")), Is.False);
    }

    [Test]
    [Category("EndToEnd")]
    public async Task SymbolLookup_LogsTheExceptionWhenACachedPdbCannotBeLoaded()
    {
        (string file, PdbMetaData identity) = TestPdbs().OrderBy(p => new FileInfo(p.File).Length).First();
        byte[] broken = PdbThatKeepsItsIdentityButFailsToLoad(file);
        string cacheFile = Path.GetFullPath(Path.Combine(_cache,
            identity.IndexPrefix.Replace('/', Path.DirectorySeparatorChar)));
        Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
        await File.WriteAllBytesAsync(cacheFile, broken);

        await using StubSymbolServer server = await StubSymbolServer.StartAsync();
        await using TestGateway gateway = await TestGateway.StartAsync(Settings(server), captureLogs: true);

        Guid session = NewSession();
        await PostAll(gateway, session, Stream(session, [Rsds(identity)]));

        CapturedLog failure = SymbolLogs(gateway).Single(e => e.Level == LogLevel.Warning);
        Assert.That(failure.Exception, Is.Not.Null);
        Assert.That(failure.Message, Does.Contain(identity.PdbName));
        Assert.That(failure.Message, Does.Contain(identity.Guid.ToString()));
        Assert.That(failure.Message, Does.Contain("could not be loaded after 1 attempt"));
        Assert.That(server.Requests, Is.Empty);
    }

    private static CapturedLog[] SymbolLogs(TestGateway gateway) =>
        gateway.Logs!.Entries.Where(e => e.Category == "SymbolDiscovery").ToArray();

    /// <summary>
    ///     A copy of <paramref name="pdbPath" /> whose GUID and age still match, while loading its WPP formats throws.
    /// </summary>
    private static byte[] PdbThatKeepsItsIdentityButFailsToLoad(string pdbPath)
    {
        byte[] bytes = File.ReadAllBytes(pdbPath);
        Assert.That(PdbFileIdentityReader.TryRead(pdbPath, out PdbFileIdentity identity), Is.True);
        string probe = Path.Combine(Path.GetTempPath(), "etw-pdb-corrupt-" + Guid.NewGuid().ToString("N"));
        try
        {
            for (int offset = 0; offset < bytes.Length; offset += 4096)
            {
                byte[] mutated = (byte[])bytes.Clone();
                Array.Clear(mutated, offset, Math.Min(4096, mutated.Length - offset));
                File.WriteAllBytes(probe, mutated);
                if (!PdbFileIdentityReader.TryRead(probe, out PdbFileIdentity read) || read != identity)
                {
                    continue;
                }

                try
                {
                    _ = new PdbFileDecodingContextType(probe);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    return mutated;
                }
            }
        }
        finally
        {
            if (File.Exists(probe))
            {
                File.Delete(probe);
            }
        }

        Assert.Fail($"No corruption of {pdbPath} kept its identity while failing to load.");
        return [];
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    public enum StubMode
    {
        Normal,
        NotFound,
        Garbage,
        WrongIdentity,
        ServerError,
        Redirect,
        FailFirstRequest
    }

    /// <summary>A loopback SymSrv that serves the test PDBs by index path.</summary>
    private sealed class StubSymbolServer : IAsyncDisposable
    {
        private readonly ConcurrentQueue<string> _requests = new();
        private WebApplication _app = null!;

        public IReadOnlyCollection<string> Requests => _requests.ToArray();

        public string Url { get; private set; } = string.Empty;

        public static async Task<StubSymbolServer> StartAsync(StubMode mode = StubMode.Normal, TimeSpan delay = default)
        {
            StubSymbolServer stub = new();
            Dictionary<string, string> files = TestPdbs().ToDictionary(
                p => "/" + p.Identity.IndexPrefix, p => p.File, StringComparer.OrdinalIgnoreCase);

            WebApplicationBuilder builder = WebApplication.CreateBuilder();
            // The test output carries the gateway's appsettings.json; do not inherit its Kestrel endpoints.
            builder.Configuration.Sources.Clear();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["urls"] = "http://127.0.0.1:0"
            });
            builder.Logging.ClearProviders();
            stub._app = builder.Build();
            stub._app.Map("/{**path}", async (HttpContext context) =>
            {
                string path = context.Request.Path.Value ?? "/";
                stub._requests.Enqueue(path);
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay);
                }

                if (!files.TryGetValue(path, out string? file))
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                switch (mode)
                {
                    case StubMode.FailFirstRequest when stub._requests.Count(r => r == path) == 1:
                        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                        return;
                    case StubMode.NotFound:
                        context.Response.StatusCode = StatusCodes.Status404NotFound;
                        return;
                    case StubMode.ServerError:
                        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                        return;
                    case StubMode.Redirect:
                        context.Response.Redirect("/elsewhere" + path);
                        return;
                    case StubMode.Garbage:
                        await context.Response.Body.WriteAsync(new byte[] { 1, 2, 3, 4 });
                        return;
                    case StubMode.WrongIdentity:
                        string other = files.Values.First(f => !f.Equals(file, StringComparison.OrdinalIgnoreCase));
                        await context.Response.SendFileAsync(other);
                        return;
                    default:
                        await context.Response.SendFileAsync(file);
                        return;
                }
            });

            await stub._app.StartAsync();
            stub.Url = stub._app.Urls.First();
            return stub;
        }

        public async ValueTask DisposeAsync() => await _app.DisposeAsync();
    }
}
