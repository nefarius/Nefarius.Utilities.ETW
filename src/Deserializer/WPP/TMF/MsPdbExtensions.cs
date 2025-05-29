using Kaitai;

namespace Nefarius.Utilities.ETW.Deserializer.WPP.TMF;

internal static class MsPdbExtensions
{
    /// <summary>
    ///     Grabs the original file name from the PDB stream.
    /// </summary>
    /// <returns>The PDB name or null if not found.</returns>
    public static string? GetOriginalPdbName(this MsPdb pdb)
    {
        MsPdb.UModuleInfo? pdbModule = pdb.DbiStream.ModulesList.Items
            .FirstOrDefault(info => info.Module.EcInfo.PdbFilenameIndex != 0);

        if (pdbModule is null)
        {
            return null;
        }

        uint index = pdbModule.Module.EcInfo.PdbFilenameIndex;
        string? pdbPathName = pdb.DbiStream.EcInfo.Strings.Strings.FirstOrDefault(s => s.CharsIndex == index)?.String;

        // required to work on Linux
        string? normalizedPath = pdbPathName?.Replace('\\', Path.DirectorySeparatorChar);

        return string.IsNullOrEmpty(normalizedPath) ? null : Path.GetFileName(normalizedPath);
    }
}