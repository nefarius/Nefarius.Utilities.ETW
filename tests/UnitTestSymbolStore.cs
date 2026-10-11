using System.Net;

using Nefarius.Utilities.ETW.Deserializer.WPP;
using Nefarius.Utilities.ETW.Symbols;

namespace Nefarius.Utilities.ETW.Tests;

[Category("Unit")]
public class SymbolStoreTests
{
    private static readonly Guid SampleGuid = Guid.Parse("779e56ef-8d24-4145-a64a-3aee304b9de9");

    [Test]
    public void Validate_RejectsWildcardDirectorySegment()
    {
        string? error = SymbolPathPatterns.Validate(@"C:\Sym*\*.pdb");

        Assert.That(error, Does.Contain("Wildcard in directory segment"));
    }

    [Test]
    public void NormalizeRoot_StripsRecursiveMarker()
    {
        string root = SymbolPathPatterns.NormalizeRoot(@"C:\Symbols\**\*.pdb");

        Assert.That(root, Is.EqualTo(@"C:\Symbols"));
    }

    [Test]
    public void Resolve_PrefersExplicitServerOverNtSymbolPath()
    {
        SymbolStoreConfiguration configuration = SymbolStoreConfiguration.Resolve(
            "https://symbols.example/download/symbols",
            @"D:\cache",
            "srv*C:\\ignored*https://ignored.example",
            @"D:\default");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(configuration.ServerUrl, Is.EqualTo("https://symbols.example/download/symbols"));
            Assert.That(configuration.CacheDirectory, Is.EqualTo(@"D:\cache"));
            Assert.That(configuration.Notices, Is.Empty);
        }
    }

    [Test]
    public void Resolve_ParsesSrvCacheUrlSegment()
    {
        SymbolStoreConfiguration configuration = SymbolStoreConfiguration.Resolve(
            null,
            null,
            @"srv*D:\symcache*https://symbols.example/download/symbols",
            @"D:\default");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(configuration.ServerUrl, Is.EqualTo("https://symbols.example/download/symbols"));
            Assert.That(configuration.CacheDirectory, Is.EqualTo(@"D:\symcache"));
            Assert.That(configuration.Notices, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task ResolveAsync_ReturnsExistingCacheFile()
    {
        string root = CreateTempDirectory();
        PdbMetaData reference = SampleReference();
        string cacheFile = Path.Combine(root, reference.IndexPrefix.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
        await File.WriteAllTextAsync(cacheFile, "cached");

        IReadOnlyList<ResolvedPdb> resolved = await SymbolStore.ResolveAsync(
            [reference],
            new SymbolStoreConfiguration(root));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(resolved, Has.Count.EqualTo(1));
            Assert.That(resolved[0].Origin, Is.EqualTo(PdbResolutionOrigin.Cache));
            Assert.That(resolved[0].LocalPath, Is.EqualTo(Path.GetFullPath(cacheFile)));
        }
    }

    [Test]
    public async Task ResolveAsync_DownloadsPdbIntoIndexCache()
    {
        string root = CreateTempDirectory();
        PdbMetaData reference = SampleReference();
        byte[] payload = "pdb-bytes"u8.ToArray();
        using HttpClient client = new(new StubHandler(request =>
        {
            Assert.That(request.RequestUri!.AbsolutePath, Does.EndWith(reference.IndexPrefix));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload)
            };
        }));

        IReadOnlyList<ResolvedPdb> resolved = await SymbolStore.ResolveAsync(
            [reference],
            new SymbolStoreConfiguration(root, "https://symbols.example/download/symbols"),
            client);

        string cacheFile = Path.Combine(root, reference.IndexPrefix.Replace('/', Path.DirectorySeparatorChar));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(resolved[0].Origin, Is.EqualTo(PdbResolutionOrigin.SymbolServer));
            Assert.That(await File.ReadAllBytesAsync(cacheFile), Is.EqualTo(payload));
        }
    }

    [Test]
    public async Task ResolveAsync_ReportsServerNotFound()
    {
        using HttpClient client = new(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        IReadOnlyList<ResolvedPdb> resolved = await SymbolStore.ResolveAsync(
            [SampleReference()],
            new SymbolStoreConfiguration(CreateTempDirectory(), "https://symbols.example/download/symbols"),
            client);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(resolved[0].Origin, Is.EqualTo(PdbResolutionOrigin.Unresolved));
            Assert.That(resolved[0].Failure, Is.EqualTo(PdbResolutionFailure.NotFoundOnServer));
        }
    }

    [Test]
    public void Load_ReportsMissingSymbolPath()
    {
        DecodingSourceSet loaded = DecodingSources.Load([Path.Combine(CreateTempDirectory(), "missing.pdb")]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(loaded.Context, Is.Null);
            Assert.That(loaded.Diagnostics, Has.Some.Contains("Symbol path not found"));
        }
    }

    [Test]
    public void Load_ReadsTmfFile()
    {
        string? tmf = Directory.EnumerateFiles(Path.Combine(TestContext.CurrentContext.TestDirectory, "symbols"), "*.tmf")
            .FirstOrDefault();
        Assert.That(tmf, Is.Not.Null);

        DecodingSourceSet loaded = DecodingSources.Load([tmf!]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(loaded.Context, Is.Not.Null);
            Assert.That(loaded.Sources, Has.Count.EqualTo(1));
            Assert.That(loaded.Sources[0], Is.InstanceOf<TmfFileDecodingContextType>());
        }
    }

    private static PdbMetaData SampleReference() => new()
    {
        PdbName = "hidhide.pdb",
        Guid = SampleGuid,
        Age = 1
    };

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "etw-symbol-store", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
