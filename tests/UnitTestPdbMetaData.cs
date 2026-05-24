using Nefarius.Utilities.ETW.Deserializer.WPP;

namespace Nefarius.Utilities.ETW.Tests;

[Category("Unit")]
public class PdbMetaDataTests
{
    // The IndexPrefix format is: {lowercase_filename}/{GUID_N_uppercase}{Age_hex_uppercase}/{lowercase_filename}
    // Example from PdbMetaData.cs docs: hidhide.pdb/779e56ef8d244145a64a3aee304b9de91/hidhide.pdb
    // where the "91" suffix is GUID_N + Age=1 (in hex).
    private static readonly Guid SampleGuid = Guid.Parse("779e56ef-8d24-4145-a64a-3aee304b9de9");
    private const int SampleAge = 1;
    private const string SampleFileName = "hidhide.pdb";

    [Test]
    public void IndexPrefix_FormatsAsLowercaseFilenameUppercaseGuidAndHexAge()
    {
        PdbMetaData meta = new() { PdbName = SampleFileName, Guid = SampleGuid, Age = SampleAge };

        string guidPart = SampleGuid.ToString("N").ToUpperInvariant();
        string agePart  = SampleAge.ToString("X").ToUpperInvariant();
        string expected = $"{SampleFileName}/{guidPart}{agePart}/{SampleFileName}";

        Assert.That(meta.IndexPrefix, Is.EqualTo(expected));
    }

    [Test]
    public void IndexPrefix_UsesFileNamePortionOnly_WhenFullPathSupplied()
    {
        PdbMetaData meta = new() { PdbName = @"C:\builds\symbols\hidhide.pdb", Guid = SampleGuid, Age = SampleAge };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(meta.IndexPrefix, Does.StartWith("hidhide.pdb/"));
            Assert.That(meta.IndexPrefix, Does.EndWith("/hidhide.pdb"));
            Assert.That(meta.IndexPrefix, Does.Not.Contain(@"C:\"));
        }
    }

    [Test]
    public void IndexPrefix_Throws_WhenPdbNameIsEmpty()
    {
        PdbMetaData meta = new() { PdbName = string.Empty, Guid = SampleGuid, Age = SampleAge };

        Assert.Throws<ArgumentException>(() => _ = meta.IndexPrefix);
    }

    [Test]
    public void DownloadPath_EqualsPrefixedWithDownloadSymbols()
    {
        PdbMetaData meta = new() { PdbName = SampleFileName, Guid = SampleGuid, Age = SampleAge };

        Assert.That(meta.DownloadPath, Is.EqualTo($"/download/symbols/{meta.IndexPrefix}"));
    }

    [Test]
    public void Equals_IsCaseInsensitiveOnFileName()
    {
        PdbMetaData lower = new() { PdbName = "bth.pdb",   Guid = SampleGuid, Age = SampleAge };
        PdbMetaData upper = new() { PdbName = "BTH.pdb",   Guid = SampleGuid, Age = SampleAge };
        PdbMetaData mixed = new() { PdbName = "BtH.PdB",   Guid = SampleGuid, Age = SampleAge };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(lower, Is.EqualTo(upper));
            Assert.That(lower, Is.EqualTo(mixed));
            Assert.That(lower == upper, Is.True);
            Assert.That(lower != upper, Is.False);
        }
    }

    [Test]
    public void Equals_IsCaseInsensitiveOnFileName_WhenFullPathSupplied()
    {
        PdbMetaData withPath = new() { PdbName = @"C:\foo\BTH.pdb", Guid = SampleGuid, Age = SampleAge };
        PdbMetaData justName = new() { PdbName = "bth.pdb",         Guid = SampleGuid, Age = SampleAge };

        Assert.That(withPath, Is.EqualTo(justName));
    }

    [Test]
    public void Equals_ReturnsFalse_WhenGuidDiffers()
    {
        PdbMetaData a = new() { PdbName = SampleFileName, Guid = SampleGuid,    Age = SampleAge };
        PdbMetaData b = new() { PdbName = SampleFileName, Guid = Guid.NewGuid(), Age = SampleAge };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(a, Is.Not.EqualTo(b));
            Assert.That(a == b, Is.False);
            Assert.That(a != b, Is.True);
        }
    }

    [Test]
    public void Equals_ReturnsFalse_WhenAgeDiffers()
    {
        PdbMetaData a = new() { PdbName = SampleFileName, Guid = SampleGuid, Age = 1 };
        PdbMetaData b = new() { PdbName = SampleFileName, Guid = SampleGuid, Age = 2 };

        Assert.That(a, Is.Not.EqualTo(b));
    }

    [Test]
    public void GetHashCode_IsConsistentWithEquals()
    {
        PdbMetaData a = new() { PdbName = "bth.pdb",  Guid = SampleGuid, Age = SampleAge };
        PdbMetaData b = new() { PdbName = "BTH.PDB",  Guid = SampleGuid, Age = SampleAge };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(a, Is.EqualTo(b));
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
        }
    }

    [Test]
    public void GetHashCode_DiffersWhenGuidDiffers()
    {
        PdbMetaData a = new() { PdbName = SampleFileName, Guid = SampleGuid,    Age = SampleAge };
        PdbMetaData b = new() { PdbName = SampleFileName, Guid = Guid.NewGuid(), Age = SampleAge };

        Assert.That(a.GetHashCode(), Is.Not.EqualTo(b.GetHashCode()));
    }
}
