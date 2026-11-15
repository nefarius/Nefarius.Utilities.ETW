using System.Text;

using Nefarius.Utilities.ETW.Deserializer;

namespace Nefarius.Utilities.ETW.Tests;

[Category("Unit")]
public class EventSourceManifestTests
{
    [Test]
    public void AnIncompleteManifestRejectsSchemaAccess()
    {
        EventSourceManifest manifest = new(Guid.NewGuid(), 1, 0, 0, 0, totalChunks: 2);

        Assert.Throws<InvalidOperationException>(() => _ = manifest.Schema);
    }

    [Test]
    public void AWrongRootManifestStreamIsRejected()
    {
        using MemoryStream source = new(Encoding.UTF8.GetBytes("<notManifest />"));

        Assert.Throws<InvalidOperationException>(() => new EventSourceManifest(Guid.NewGuid(), source));
    }

    [Test]
    public void AnEmptyManifestStreamIsRejected()
    {
        using MemoryStream source = new();

        Assert.Throws<InvalidOperationException>(() => new EventSourceManifest(Guid.NewGuid(), source));
    }
}
