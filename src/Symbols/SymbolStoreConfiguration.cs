namespace Nefarius.Utilities.ETW.Symbols;

/// <summary>
///     Local cache, search paths, and optional symbol server used to resolve PDB identities.
/// </summary>
public sealed class SymbolStoreConfiguration
{
    /// <summary>
    ///     Creates a symbol-store configuration.
    /// </summary>
    public SymbolStoreConfiguration(string cacheDirectory, string? serverUrl = null, IReadOnlyList<string>? searchPaths = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        CacheDirectory = cacheDirectory;
        ServerUrl = string.IsNullOrWhiteSpace(serverUrl) ? null : serverUrl;
        SearchPaths = searchPaths ?? [];
    }

    /// <summary>Gets the optional HTTP symbol server, without a trailing slash requirement.</summary>
    public string? ServerUrl { get; }

    /// <summary>Gets the directory that stores downloaded PDBs in symbol-server index layout.</summary>
    public string CacheDirectory { get; }

    /// <summary>Gets directories or filename globs searched before the symbol server.</summary>
    public IReadOnlyList<string> SearchPaths { get; }

    /// <summary>Gets human-readable notes produced while interpreting <c>_NT_SYMBOL_PATH</c>.</summary>
    public IReadOnlyList<string> Notices { get; private init; } = [];

    /// <summary>
    ///     Resolves the effective server and cache from explicit values, or from <paramref name="ntSymbolPath" />
    ///     when both explicit values are absent.
    /// </summary>
    /// <remarks>
    ///     Supports <c>srv*cache*url</c>, <c>srv*url</c>, and <c>cache*dir</c> segments. Explicit server or cache
    ///     values bypass environment parsing.
    /// </remarks>
    public static SymbolStoreConfiguration Resolve(
        string? configuredServer,
        string? configuredCache,
        string? ntSymbolPath,
        string defaultCacheDirectory,
        IReadOnlyList<string>? searchPaths = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultCacheDirectory);

        if (configuredServer is not null || configuredCache is not null)
        {
            return new SymbolStoreConfiguration(
                configuredCache ?? defaultCacheDirectory,
                configuredServer,
                searchPaths);
        }

        if (string.IsNullOrWhiteSpace(ntSymbolPath))
        {
            return new SymbolStoreConfiguration(defaultCacheDirectory, searchPaths: searchPaths);
        }

        string? server = null;
        string cache = defaultCacheDirectory;
        List<string> notices = [];

        foreach (string segment in ntSymbolPath.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = segment.Trim();
            string[] parts = trimmed.Split('*');

            if (parts.Length == 3 &&
                parts[0].Equals("srv", StringComparison.OrdinalIgnoreCase) &&
                Uri.TryCreate(parts[2], UriKind.Absolute, out Uri? serverWithCache) &&
                IsHttp(serverWithCache))
            {
                cache = string.IsNullOrWhiteSpace(parts[1]) ? defaultCacheDirectory : parts[1];
                server = parts[2];
                notices.Add($"[*] _NT_SYMBOL_PATH: cache='{cache}', server='{server}'");
                continue;
            }

            if (parts.Length == 2 &&
                parts[0].Equals("cache", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(parts[1]))
            {
                cache = parts[1];
                notices.Add($"[*] _NT_SYMBOL_PATH: cache='{cache}'");
                continue;
            }

            if (parts.Length == 2 &&
                parts[0].Equals("srv", StringComparison.OrdinalIgnoreCase) &&
                Uri.TryCreate(parts[1], UriKind.Absolute, out Uri? serverOnly) &&
                IsHttp(serverOnly))
            {
                server = parts[1];
                notices.Add($"[*] _NT_SYMBOL_PATH: server='{server}'");
                continue;
            }

            notices.Add(
                $"[*] Ignoring complex _NT_SYMBOL_PATH segment '{trimmed}'. " +
                "Use an explicit symbol server and cache for full control.");
        }

        return new SymbolStoreConfiguration(cache, server, searchPaths) { Notices = notices };
    }

    private static bool IsHttp(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
}
