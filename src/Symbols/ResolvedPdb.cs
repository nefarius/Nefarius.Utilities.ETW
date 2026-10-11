using Nefarius.Utilities.ETW.Deserializer.WPP;

namespace Nefarius.Utilities.ETW.Symbols;

/// <summary>
///     Where a resolved PDB file was found.
/// </summary>
public enum PdbResolutionOrigin
{
    /// <summary>No matching PDB was found.</summary>
    Unresolved = 0,

    /// <summary>The symbol-server cache already contained the indexed PDB.</summary>
    Cache,

    /// <summary>A search path contained a PDB with the requested file name, GUID, and age.</summary>
    LocalSearch,

    /// <summary>The PDB was downloaded from the configured symbol server.</summary>
    SymbolServer
}

/// <summary>
///     Why a PDB reference was not resolved.
/// </summary>
public enum PdbResolutionFailure
{
    /// <summary>The reference was resolved, or it was absent without a server response.</summary>
    None = 0,

    /// <summary>The symbol server returned HTTP 404.</summary>
    NotFoundOnServer,

    /// <summary>The symbol server request failed before a PDB could be stored.</summary>
    DownloadFailed
}

/// <summary>
///     The outcome of resolving one <see cref="PdbMetaData" /> reference.
/// </summary>
public sealed class ResolvedPdb
{
    /// <summary>Gets the requested PDB identity.</summary>
    public required PdbMetaData Reference { get; init; }

    /// <summary>Gets the local PDB path when resolution succeeded.</summary>
    public string? LocalPath { get; init; }

    /// <summary>Gets the source that supplied <see cref="LocalPath" />.</summary>
    public PdbResolutionOrigin Origin { get; init; }

    /// <summary>Gets a URL or exception detail associated with the attempt.</summary>
    public string? Detail { get; init; }

    /// <summary>Gets the server-side failure classification when <see cref="Origin" /> is unresolved.</summary>
    public PdbResolutionFailure Failure { get; init; }
}
