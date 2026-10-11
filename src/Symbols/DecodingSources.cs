using Nefarius.Utilities.ETW.Deserializer.WPP;

namespace Nefarius.Utilities.ETW.Symbols;

/// <summary>
///     PDB and TMF sources loaded into one WPP <see cref="DecodingContext" />.
/// </summary>
public sealed class DecodingSourceSet
{
    /// <summary>Creates a loaded source set.</summary>
    public DecodingSourceSet(
        DecodingContext? context,
        IReadOnlyList<DecodingContextType> sources,
        IReadOnlyList<string> diagnostics)
    {
        Context = context;
        Sources = sources;
        Diagnostics = diagnostics;
    }

    /// <summary>Gets the combined decoding context, or <see langword="null" /> when nothing loaded.</summary>
    public DecodingContext? Context { get; }

    /// <summary>Gets the individual sources that were loaded.</summary>
    public IReadOnlyList<DecodingContextType> Sources { get; }

    /// <summary>Gets human-readable load messages, including skipped inputs.</summary>
    public IReadOnlyList<string> Diagnostics { get; }

    /// <summary>
    ///     Returns whether any loaded PDB declares a single usable WPP control-GUID name.
    /// </summary>
    public bool CanRewriteProviderNames =>
        Sources.OfType<PdbFileDecodingContextType>()
            .Any(source => source.WppTraceControls.Any(control => !string.IsNullOrWhiteSpace(control.Name)));
}

/// <summary>
///     Counts and sources produced by resolving the PDB references embedded in ETL files.
/// </summary>
public sealed class TraceSymbolResolution
{
    /// <summary>Gets the loaded PDB decoding sources.</summary>
    public required IReadOnlyList<DecodingContextType> Sources { get; init; }

    /// <summary>Gets human-readable resolution messages.</summary>
    public required IReadOnlyList<string> Diagnostics { get; init; }

    /// <summary>Gets the number of distinct PDB references discovered in the traces.</summary>
    public int ReferencedCount { get; init; }

    /// <summary>Gets how many references were loaded from the cache.</summary>
    public int FromCache { get; init; }

    /// <summary>Gets how many references were loaded from a search path.</summary>
    public int FromLocal { get; init; }

    /// <summary>Gets how many references were downloaded.</summary>
    public int FromServer { get; init; }

    /// <summary>Gets how many references could not be loaded.</summary>
    public int Unresolved { get; init; }
}

/// <summary>
///     Loads PDB and TMF files, directories, and filename globs into WPP decoding sources.
/// </summary>
public static class DecodingSources
{
    /// <summary>
    ///     Loads explicit symbol inputs. Unreadable or unsupported inputs are reported in
    ///     <see cref="DecodingSourceSet.Diagnostics" /> and skipped.
    /// </summary>
    public static DecodingSourceSet Load(IEnumerable<string> symbolPaths)
    {
        ArgumentNullException.ThrowIfNull(symbolPaths);

        List<DecodingContextType> sources = [];
        List<string> diagnostics = [];
        foreach (string path in symbolPaths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                diagnostics.Add("[!] Symbol path is empty.");
                continue;
            }

            if (path.Contains('*') || path.Contains('?'))
            {
                LoadGlob(path, sources, diagnostics);
            }
            else if (Directory.Exists(path))
            {
                LoadDirectory(path, sources, diagnostics);
            }
            else if (File.Exists(path))
            {
                AddFile(sources, diagnostics, path);
            }
            else
            {
                diagnostics.Add($"[!] Symbol path not found: {path}");
            }
        }

        if (sources.Count == 0)
        {
            diagnostics.Add("[!] No symbol files could be loaded; WPP messages will not be decoded.");
            return new DecodingSourceSet(null, sources, diagnostics);
        }

        diagnostics.Add($"[*] Loaded {sources.Count} symbol source(s) for WPP decoding.");
        return new DecodingSourceSet(new DecodingContext(sources), sources, diagnostics);
    }

    /// <summary>
    ///     Pre-scans ETL files and resolves every referenced PDB through <see cref="SymbolStore" />.
    /// </summary>
    public static async Task<TraceSymbolResolution> ResolveTraceSymbolsAsync(
        IEnumerable<string> etlFiles,
        SymbolStoreConfiguration configuration,
        HttpClient? httpClient = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(etlFiles);
        ArgumentNullException.ThrowIfNull(configuration);

        List<string> diagnostics = [];
        List<DecodingContextType> sources = [];
        IReadOnlyCollection<PdbMetaData> references;
        try
        {
            references = EtwUtil.EnumeratePdbReferences(etlFiles);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            diagnostics.Add($"[!] PDB pre-scan failed: {ex.GetType().Name}: {ex.Message}");
            return EmptyResolution(sources, diagnostics);
        }

        if (references.Count == 0)
        {
            diagnostics.Add("[*] Auto-discovery: no PDB references found in the trace(s).");
            return EmptyResolution(sources, diagnostics);
        }

        diagnostics.Add($"[*] Auto-discovery: resolving {references.Count} PDB reference(s)...");
        IReadOnlyList<ResolvedPdb> resolved = await SymbolStore
            .ResolveAsync(references, configuration, httpClient, cancellationToken)
            .ConfigureAwait(false);

        int fromCache = 0;
        int fromLocal = 0;
        int fromServer = 0;
        int unresolved = 0;
        foreach (ResolvedPdb pdb in resolved)
        {
            string fileName = Path.GetFileName(pdb.Reference.PdbName);
            switch (pdb.Origin)
            {
                case PdbResolutionOrigin.Cache:
                    diagnostics.Add($"    [cache ] {fileName}");
                    break;
                case PdbResolutionOrigin.LocalSearch:
                    diagnostics.Add($"    [local ] {fileName} <- {pdb.LocalPath}");
                    break;
                case PdbResolutionOrigin.SymbolServer:
                    diagnostics.Add($"    [server] {fileName} <- {pdb.Detail}");
                    break;
                default:
                    if (pdb.Failure == PdbResolutionFailure.NotFoundOnServer)
                    {
                        diagnostics.Add($"    [404   ] {fileName} ({pdb.Detail})");
                    }
                    else if (pdb.Failure == PdbResolutionFailure.DownloadFailed)
                    {
                        diagnostics.Add($"[!] Failed to download '{fileName}': {pdb.Detail}");
                    }

                    diagnostics.Add(
                        $"    [miss  ] {fileName} (guid={pdb.Reference.Guid:D}, age={pdb.Reference.Age})");
                    unresolved++;
                    continue;
            }

            try
            {
                sources.Add(new PdbFileDecodingContextType(pdb.LocalPath!));
                switch (pdb.Origin)
                {
                    case PdbResolutionOrigin.Cache:
                        fromCache++;
                        break;
                    case PdbResolutionOrigin.LocalSearch:
                        fromLocal++;
                        break;
                    case PdbResolutionOrigin.SymbolServer:
                        fromServer++;
                        break;
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                diagnostics.Add($"[!] PDB '{fileName}' at '{pdb.LocalPath}' could not be loaded: {ex.Message}");
                unresolved++;
            }
        }

        diagnostics.Add(
            $"[*] Auto-discovery complete: {sources.Count}/{references.Count} resolved " +
            $"({fromCache} cache, {fromLocal} local, {fromServer} downloaded, {unresolved} unresolved).");

        return new TraceSymbolResolution
        {
            Sources = sources,
            Diagnostics = diagnostics,
            ReferencedCount = references.Count,
            FromCache = fromCache,
            FromLocal = fromLocal,
            FromServer = fromServer,
            Unresolved = unresolved
        };
    }

    private static void LoadGlob(string glob, List<DecodingContextType> sources, List<string> diagnostics)
    {
        if (SymbolPathPatterns.Validate(glob) is string error)
        {
            diagnostics.Add(error);
            return;
        }

        string root = SymbolPathPatterns.NormalizeRoot(glob);
        if (!Directory.Exists(root))
        {
            diagnostics.Add($"[!] Glob root directory not found: {root}");
            return;
        }

        foreach (string file in SymbolPathPatterns.Enumerate(glob))
        {
            AddFile(sources, diagnostics, file);
        }
    }

    private static void LoadDirectory(string directory, List<DecodingContextType> sources, List<string> diagnostics)
    {
        bool any = false;
        foreach (string pdb in Directory.EnumerateFiles(directory, "*.pdb", SearchOption.AllDirectories))
        {
            if (AddFile(sources, diagnostics, pdb))
            {
                any = true;
            }
        }

        IEnumerable<string> tmfDirectories = Directory
            .EnumerateFiles(directory, "*.tmf", SearchOption.AllDirectories)
            .Select(file => Path.GetDirectoryName(file)!)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (string tmfDirectory in tmfDirectories)
        {
            try
            {
                sources.Add(new TmfFilesDirectoryDecodingContextType(tmfDirectory));
                diagnostics.Add($"    + {tmfDirectory} (TMF directory)");
                any = true;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                diagnostics.Add($"[!] Failed to load TMF directory '{tmfDirectory}': {ex.GetType().Name}: {ex.Message}");
            }
        }

        if (!any)
        {
            diagnostics.Add($"[!] No PDB or TMF files found in directory: {directory}");
        }
    }

    private static bool AddFile(List<DecodingContextType> sources, List<string> diagnostics, string path)
    {
        string extension = Path.GetExtension(path);
        try
        {
            if (extension.Equals(".pdb", StringComparison.OrdinalIgnoreCase))
            {
                sources.Add(new PdbFileDecodingContextType(path));
                diagnostics.Add($"    + {path}");
                return true;
            }

            if (extension.Equals(".tmf", StringComparison.OrdinalIgnoreCase))
            {
                sources.Add(new TmfFileDecodingContextType(path));
                diagnostics.Add($"    + {path}");
                return true;
            }

            diagnostics.Add($"[!] Unrecognized symbol file type (expected .pdb or .tmf): {path}");
            return false;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            diagnostics.Add($"[!] Failed to load '{path}': {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static TraceSymbolResolution EmptyResolution(
        IReadOnlyList<DecodingContextType> sources,
        IReadOnlyList<string> diagnostics) =>
        new()
        {
            Sources = sources,
            Diagnostics = diagnostics
        };
}
