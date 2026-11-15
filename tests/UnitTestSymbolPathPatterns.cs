using Nefarius.Utilities.ETW.Symbols;

namespace Nefarius.Utilities.ETW.Tests;

[Category("Unit")]
public class SymbolPathPatternsTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"globs-{Guid.NewGuid():N}");
        foreach (string file in new[]
                 {
                     "top.pdb", "a/one.pdb", "a/sub/two.pdb", "a/b/sub/three.pdb", "sub/four.pdb", "c/other/five.pdb",
                     "a/sub/skip.txt"
                 })
        {
            string path = Path.Combine(_root, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, string.Empty);
        }
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_root, true);
    }

    private string[] Names(string glob)
    {
        return SymbolPathPatterns.Enumerate(Path.Combine(_root, glob).Replace('/', Path.DirectorySeparatorChar))
            .Select(f => Path.GetRelativePath(_root, f).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    [Test]
    public void FilenameWildcard_IsTopLevelOnly()
    {
        Assert.That(Names("*.pdb"), Is.EqualTo(new[] { "top.pdb" }));
    }

    [Test]
    public void DoubleStar_RecursesIncludingTheRoot()
    {
        Assert.That(Names("**/*.pdb"), Is.EqualTo(new[]
        {
            "a/b/sub/three.pdb", "a/one.pdb", "a/sub/two.pdb", "c/other/five.pdb", "sub/four.pdb", "top.pdb"
        }));
    }

    [Test]
    public void LiteralSegmentAfterDoubleStar_IsHonoured()
    {
        Assert.That(Names("**/sub/*.pdb"), Is.EqualTo(new[] { "a/b/sub/three.pdb", "a/sub/two.pdb", "sub/four.pdb" }));
    }

    [Test]
    public void LiteralSegmentAfterDoubleStar_UnderASubRoot()
    {
        Assert.That(Names("a/**/sub/*.pdb"), Is.EqualTo(new[] { "a/b/sub/three.pdb", "a/sub/two.pdb" }));
    }

    [Test]
    public void MultipleDoubleStars_DoNotProduceDuplicates()
    {
        Assert.That(Names("**/other/**/*.pdb"), Is.EqualTo(new[] { "c/other/five.pdb" }));
    }

    [Test]
    public void ConsecutiveDoubleStars_BehaveLikeOne()
    {
        Assert.That(Names("**/**/**/*.pdb"), Is.EqualTo(Names("**/*.pdb")));
        Assert.That(Names("**/**/sub/**/**/*.pdb"), Is.EqualTo(Names("**/sub/**/*.pdb")));
    }

    [Test]
    public void HiddenDirectories_AreSearched()
    {
        string hidden = Path.Combine(_root, ".hidden");
        Directory.CreateDirectory(hidden);
        File.WriteAllText(Path.Combine(hidden, "secret.pdb"), string.Empty);
        if (OperatingSystem.IsWindows())
        {
            File.SetAttributes(hidden, File.GetAttributes(hidden) | FileAttributes.Hidden);
        }

        Assert.That(Names("**/*.pdb"), Does.Contain(".hidden/secret.pdb"));
    }

    [Test]
    public void StarDotStar_MatchesExtensionlessFiles()
    {
        File.WriteAllText(Path.Combine(_root, "symbols"), string.Empty);

        Assert.That(Names("*.*"), Does.Contain("symbols"));
    }

    [Test]
    public void MissingRoot_YieldsNothing()
    {
        Assert.That(Names("nope/**/*.pdb"), Is.Empty);
    }
}