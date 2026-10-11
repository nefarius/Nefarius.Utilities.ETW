using Kaitai;

namespace Nefarius.Utilities.ETW.Symbols;

/// <summary>
///     PDB signature and age read from a Windows PDB information stream.
/// </summary>
/// <param name="Guid">The VC7 PDB signature.</param>
/// <param name="Age">The PDB age.</param>
public readonly record struct PdbFileIdentity(Guid Guid, int Age);

/// <summary>
///     Reads the GUID and age embedded in a Windows PDB without requiring a separate PDB parser package.
/// </summary>
public static class PdbFileIdentityReader
{
    /// <summary>
    ///     Attempts to read the VC7 signature and age from <paramref name="pdbPath" />.
    /// </summary>
    /// <returns><see langword="false" /> when the file is not a readable VC7 PDB.</returns>
    public static bool TryRead(string pdbPath, out PdbFileIdentity identity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pdbPath);
        identity = default;

        try
        {
            using FileStream stream = File.Open(pdbPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using KaitaiStream kaitai = new(stream);
            MsPdb pdb = new(kaitai);
            MsPdb.PdbStream? root = pdb.PdbRootStream;
            MsPdb.PdbStreamHdrVc70? header = root?.HeaderVc70;
            MsPdb.Guid? signature = header?.Sig70;
            if (root?.Header is null || signature?.Data4 is null || signature.Data4.Count != 8)
            {
                return false;
            }

            Guid guid = new(
                (int)signature.Data1,
                (short)signature.Data2,
                (short)signature.Data3,
                signature.Data4.ToArray());
            identity = new PdbFileIdentity(guid, checked((int)root.Header.Age));
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return false;
        }
    }
}
