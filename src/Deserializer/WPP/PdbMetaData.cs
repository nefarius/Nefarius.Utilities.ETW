namespace Nefarius.Utilities.ETW.Deserializer.WPP;

/// <summary>
///     Describes a Program DataBase metaobject extracted from the provided trace.
/// </summary>
public readonly struct PdbMetaData
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
    ///     Index prefix (path name) of the symbol to lookup on a symbol server. 
    /// </summary>
    public string IndexPrefix
    {
        get
        {
            ArgumentException.ThrowIfNullOrEmpty(PdbName);

            string filename = Path.GetFileName(PdbName).ToLowerInvariant();

            return $"{filename}/{Guid.ToString("N").ToUpperInvariant()}{Age:X}/{filename}";
        }
    }
}