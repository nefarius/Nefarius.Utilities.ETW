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
        string fileName = PathHelper.GetFileName(reference.PdbName);
        string cacheFilePath = GetCachePath(configuration.CacheDirectory, reference);

        if (MatchesReference(cacheFilePath, reference))
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
            string temporaryPath = $"{cacheFilePath}.{Guid.NewGuid():N}.tmp";
            string identityDirectory = Path.GetDirectoryName(cacheFilePath)!;
            try
            {
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

                if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                {
                    return new ResolvedPdb
                    {
                        Reference = reference,
                        Origin = PdbResolutionOrigin.Unresolved,
                        Failure = PdbResolutionFailure.Unauthorized,
                        Detail = $"{(int)response.StatusCode} {response.StatusCode}"
                    };
                }

                response.EnsureSuccessStatusCode();
                // Only a successful response is worth a cache directory. Failures must not leave it behind.
                await using (FileStream temporary = OpenDownload(temporaryPath, identityDirectory))
                {
                    await response.Content.CopyToAsync(temporary, cancellationToken).ConfigureAwait(false);
                }

                if (!MatchesReference(temporaryPath, reference))
                {
                    return new ResolvedPdb
                    {
                        Reference = reference,
                        Origin = PdbResolutionOrigin.Unresolved,
                        Failure = PdbResolutionFailure.DownloadFailed,
                        Detail = "Downloaded PDB identity does not match the requested GUID and age."
                    };
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
                throw;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return new ResolvedPdb
                {
                    Reference = reference,
                    Origin = PdbResolutionOrigin.Unresolved,
                    Failure = PdbResolutionFailure.DownloadFailed,
                    Detail = $"{ex.GetType().Name}: {ex.Message}"
                };
            }
            finally
            {
                // The cache slot may already hold a different PDB, so its presence must not keep this attempt's file.
                DeleteIfExists(temporaryPath);
                if (!File.Exists(cacheFilePath))
                {
                    RemoveEmptyIdentityDirectory(identityDirectory, configuration.CacheDirectory);
                }
            }
        }

        return new ResolvedPdb
        {
            Reference = reference,
            Origin = PdbResolutionOrigin.Unresolved
        };
    }

    private static bool MatchesReference(string pdbPath, PdbMetaData reference) =>
        PdbFileIdentityReader.TryRead(pdbPath, out PdbFileIdentity identity) &&
        identity.Guid == reference.Guid &&
        identity.Age == reference.Age;

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
            if (SymbolPathPatterns.Validate(searchPath) is not null ||
                !Directory.Exists(PathHelper.ToHostPath(SymbolPathPatterns.NormalizeRoot(searchPath))))
            {
                return [];
            }

            return SymbolPathPatterns.Enumerate(searchPath);
        }

        string directory = PathHelper.ToHostPath(searchPath);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory.EnumerateFiles(directory, "*.pdb", SearchOption.AllDirectories);
    }

    /// <summary>
    ///     Creates the identity directory and opens the temporary file. Retries when a concurrent failed transfer
    ///     removes the still-empty directory between the two calls.
    /// </summary>
    private static FileStream OpenDownload(string temporaryPath, string identityDirectory)
    {
        const int attempts = 3;
        for (int attempt = 1; ; attempt++)
        {
            Directory.CreateDirectory(identityDirectory);
            try
            {
                return new FileStream(
                    temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true);
            }
            catch (DirectoryNotFoundException) when (attempt < attempts)
            {
            }
        }
    }

    /// <summary>
    ///     Removes an identity directory left by a failed transfer, and its PDB-name parent when that is empty too.
    ///     Stops at the cache root and never deletes a directory that still holds a file.
    /// </summary>
    private static void RemoveEmptyIdentityDirectory(string identityDirectory, string cacheDirectory)
    {
        string stop = Path.GetFullPath(cacheDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string? current = identityDirectory;

        while (!string.IsNullOrEmpty(current))
        {
            string full = Path.GetFullPath(current)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (full.Equals(stop, StringComparison.OrdinalIgnoreCase) ||
                !full.StartsWith(stop + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                !Directory.Exists(full))
            {
                return;
            }

            try
            {
                if (Directory.EnumerateFileSystemEntries(full).Any())
                {
                    return;
                }

                Directory.Delete(full);
            }
            catch (IOException)
            {
                return;
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }

            current = Path.GetDirectoryName(full);
        }
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
