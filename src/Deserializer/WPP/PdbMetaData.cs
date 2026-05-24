using System.Diagnostics.CodeAnalysis;

namespace Nefarius.Utilities.ETW.Deserializer.WPP;

/// <summary>
///     Describes a Program DataBase metaobject extracted from the provided trace.
/// </summary>
[SuppressMessage("ReSharper", "UnusedMember.Global")]
[SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
public readonly struct PdbMetaData : IEquatable<PdbMetaData>
{
    /// <summary>
    ///     The full path of the PDB extracted from the session information.
    /// </summary>
    public required string PdbName { get; init; }

    /// <summary>
    ///     The GUID uniquely identifying the symbol file.
    /// </summary>
    public required Guid Guid { get; init; }

    /// <summary>
    ///     The age a.k.a. the revision of the build of the symbol file.
    /// </summary>
    public required int Age { get; init; }

    /// <summary>
    ///     Index prefix (relative path name) of the symbol to lookup on a symbol server.
    /// </summary>
    /// <remarks>
    ///     For example
    ///     <a href="https://symbols.nefarius.at/download/symbols/hidhide.pdb/779e56ef8d244145a64a3aee304b9de91/hidhide.pdb">hidhide.pdb/779e56ef8d244145a64a3aee304b9de91/hidhide.pdb</a>
    /// </remarks>
    public string IndexPrefix
    {
        get
        {
            ArgumentException.ThrowIfNullOrEmpty(PdbName);

            string filename = Path.GetFileName(PdbName).ToLowerInvariant();

            return
                $"{filename}/{Guid.ToString("N").ToUpperInvariant()}{Age.ToString("X").ToUpperInvariant()}/{filename}";
        }
    }

    /// <summary>
    ///     Gets the typical symbol server download path.
    /// </summary>
    public string DownloadPath => $"/download/symbols/{IndexPrefix}";

    /// <inheritdoc />
    public bool Equals(PdbMetaData other) =>
        Guid == other.Guid && Age == other.Age &&
        string.Equals(
            Path.GetFileName(PdbName),
            Path.GetFileName(other.PdbName),
            StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is PdbMetaData other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(
            Guid,
            Age,
            StringComparer.OrdinalIgnoreCase.GetHashCode(Path.GetFileName(PdbName) ?? string.Empty));

    /// <inheritdoc />
    public static bool operator ==(PdbMetaData left, PdbMetaData right) => left.Equals(right);

    /// <inheritdoc />
    public static bool operator !=(PdbMetaData left, PdbMetaData right) => !left.Equals(right);
}