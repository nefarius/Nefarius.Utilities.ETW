using Nefarius.Utilities.ETW.Deserializer.WPP;

namespace Nefarius.Utilities.ETW.Symbols;

/// <summary>
///     Resolves PDB identities through a local cache, search paths, and an HTTP symbol server.
/// </summary>
public static class SymbolStore
{
    /// <summary>
    ///     Resolves each PDB reference in cache, local-search, symbol-server order.
    /// </summary>
    public static async Task<IReadOnlyList<ResolvedPdb>> ResolveAsync(
        IEnumerable<PdbMetaData> references,
        SymbolStoreConfiguration configuration,
        HttpClient? httpClient = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(configuration);

        List<PdbMetaData> requested = references.ToList();
        if (requested.Count == 0)
        {
            return [];
        }

        Directory.CreateDirectory(configuration.CacheDirectory);
        bool disposeClient = httpClient is null;
        HttpClient client = httpClient ?? new HttpClient();
        try
        {
            List<ResolvedPdb> resolved = new(requested.Count);
            foreach (PdbMetaData reference in requested)
            {
                cancellationToken.ThrowIfCancellationRequested();
                resolved.Add(await ResolveOneAsync(reference, configuration, client, cancellationToken)
                    .ConfigureAwait(false));
            }

            return resolved;
        }
        finally
        {
            if (disposeClient)
            {
                client.Dispose();
            }
        }
    }

    private static async Task<ResolvedPdb> ResolveOneAsync(
        PdbMetaData reference,
        SymbolStoreConfiguration configuration,
        HttpClient httpClient,
        CancellationToken cancellationToken)
    {
        string fileName = Path.GetFileName(reference.PdbName);
        string cacheFilePath = GetCachePath(configuration.CacheDirectory, reference);

        if (File.Exists(cacheFilePath))
        {
            return new ResolvedPdb
            {
                Reference = reference,
                LocalPath = cacheFilePath,
                Origin = PdbResolutionOrigin.Cache
            };
        }

        if (configuration.SearchPaths.Count > 0)
        {
            string? local = FindInSearchPaths(configuration.SearchPaths, fileName, reference.Guid, reference.Age);
            if (local is not null)
            {
                return new ResolvedPdb
                {
                    Reference = reference,
                    LocalPath = local,
                    Origin = PdbResolutionOrigin.LocalSearch
                };
            }
        }

        if (!string.IsNullOrWhiteSpace(configuration.ServerUrl))
        {
            string url = $"{configuration.ServerUrl!.TrimEnd('/')}/{reference.IndexPrefix}";
            string temporaryPath = cacheFilePath + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cacheFilePath)!);
                using HttpResponseMessage response = await httpClient
                    .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return new ResolvedPdb
                    {
                        Reference = reference,
                        Origin = PdbResolutionOrigin.Unresolved,
                        Failure = PdbResolutionFailure.NotFoundOnServer,
                        Detail = url
                    };
                }

                response.EnsureSuccessStatusCode();
                await using (FileStream temporary = new(
                                 temporaryPath,
                                 FileMode.Create,
                                 FileAccess.Write,
                                 FileShare.None,
                                 65536,
                                 useAsync: true))
                {
                    await response.Content.CopyToAsync(temporary, cancellationToken).ConfigureAwait(false);
                }

                File.Move(temporaryPath, cacheFilePath, overwrite: true);
                return new ResolvedPdb
                {
                    Reference = reference,
                    LocalPath = cacheFilePath,
                    Origin = PdbResolutionOrigin.SymbolServer,
                    Detail = url
                };
            }
            catch (OperationCanceledException)
            {
                DeleteIfExists(temporaryPath);
                throw;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                DeleteIfExists(temporaryPath);
                return new ResolvedPdb
                {
                    Reference = reference,
                    Origin = PdbResolutionOrigin.Unresolved,
                    Failure = PdbResolutionFailure.DownloadFailed,
                    Detail = $"{ex.GetType().Name}: {ex.Message}"
                };
            }
        }

        return new ResolvedPdb
        {
            Reference = reference,
            Origin = PdbResolutionOrigin.Unresolved
        };
    }

    private static string GetCachePath(string cacheDirectory, PdbMetaData reference)
    {
        string indexPath = reference.IndexPrefix.Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(cacheDirectory, indexPath));
    }

    private static string? FindInSearchPaths(
        IReadOnlyList<string> searchPaths,
        string pdbFileName,
        Guid expectedGuid,
        int expectedAge)
    {
        foreach (string searchPath in searchPaths)
        {
            IEnumerable<string> candidates = EnumerateCandidates(searchPath);
            foreach (string candidate in candidates)
            {
                if (!Path.GetFileName(candidate).Equals(pdbFileName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!PdbFileIdentityReader.TryRead(candidate, out PdbFileIdentity identity))
                {
                    continue;
                }

                if (identity.Guid == expectedGuid && identity.Age == expectedAge)
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateCandidates(string searchPath)
    {
        if (searchPath.Contains('*') || searchPath.Contains('?'))
        {
            if (SymbolPathPatterns.Validate(searchPath) is not null || !Directory.Exists(SymbolPathPatterns.NormalizeRoot(searchPath)))
            {
                return [];
            }

            return SymbolPathPatterns.Enumerate(searchPath);
        }

        if (!Directory.Exists(searchPath))
        {
            return [];
        }

        return Directory.EnumerateFiles(searchPath, "*.pdb", SearchOption.AllDirectories);
    }

    private static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
