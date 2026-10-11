using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using Nefarius.Utilities.ETW.Deserializer.WPP;

namespace Nefarius.Utilities.ETW.Symbols;

/// <summary>
///     Result of reading a PE CodeView debug directory.
/// </summary>
/// <param name="Metadata">The embedded PDB identity when a CodeView entry was found.</param>
/// <param name="Diagnostic">A human-readable failure description; otherwise <see langword="null" />.</param>
public readonly record struct CodeViewReadResult(PdbMetaData? Metadata, string? Diagnostic);

/// <summary>
///     Reads the PDB identity embedded in a portable executable.
/// </summary>
public static class PortableExecutableSymbols
{
    /// <summary>
    ///     Reads the first RSDS CodeView debug entry from <paramref name="binaryPath" />.
    /// </summary>
    public static CodeViewReadResult TryReadCodeViewPdb(string binaryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(binaryPath);

        if (!File.Exists(binaryPath))
        {
            return new CodeViewReadResult(null, $"Binary path does not exist: '{binaryPath}'.");
        }

        try
        {
            using FileStream stream = new(binaryPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using PEReader reader = new(stream);
            foreach (DebugDirectoryEntry entry in reader.ReadDebugDirectory())
            {
                if (entry.Type != DebugDirectoryEntryType.CodeView)
                {
                    continue;
                }

                CodeViewDebugDirectoryData codeView = reader.ReadCodeViewDebugDirectoryData(entry);
                PdbMetaData metadata = new()
                {
                    PdbName = codeView.Path,
                    Guid = codeView.Guid,
                    Age = codeView.Age
                };
                return new CodeViewReadResult(metadata, null);
            }

            return new CodeViewReadResult(
                null,
                "No CodeView (RSDS) debug directory entry found in '" + binaryPath + "'. " +
                "The binary may be stripped or produced without PDB reference embedding.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new CodeViewReadResult(
                null,
                $"Failed to read the PE debug directory from '{binaryPath}': {ex.GetType().Name}: {ex.Message}");
        }
    }
}
