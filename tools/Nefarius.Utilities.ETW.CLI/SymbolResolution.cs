using System.Net.Http;
using System.Reflection.PortableExecutable;

using Nefarius.Utilities.ETW.Deserializer.WPP;

using Smx.PDBSharp;

namespace Nefarius.Utilities.ETW.CLI;

/// <summary>
///     Helpers that turn <c>--symbols</c>, <c>--symbols-search</c>, <c>--driver</c>, and symbol-store
///     configuration into loaded <see cref="DecodingContextType" />s and resolved PDB file paths.
/// </summary>
internal static class SymbolResolution
{
    /// <summary>
    ///     Returns <see langword="true" /> when the WPP provider-name rewrite should be enabled.
    ///     Returns <see langword="false" /> immediately when <paramref name="keepOriginalProvider" /> is set.
    ///     Returns <see langword="false" /> (with a one-time informational stderr notice) when no
    ///     TMC control-GUID names are found in the loaded symbols — e.g. only TMF sources were loaded.
    /// </summary>
    internal static bool ShouldRewriteProviderName(
        IEnumerable<DecodingContextType> types,
        bool keepOriginalProvider)
    {
        if (keepOriginalProvider)
        {
            return false;
        }

        bool hasTmcNames = types
            .OfType<PdbFileDecodingContextType>()
            .Any(p => p.WppTraceControls.Any(c => !string.IsNullOrWhiteSpace(c.Name)));

        if (!hasTmcNames)
        {
            Console.Error.WriteLine(
                "[*] WPP provider name rewrite: no TMC control-GUID names found in the loaded symbols; " +
                "using original Provider values. Pass --keep-original-provider to suppress this notice.");
            return false;
        }

        return true;
    }

    /// <summary>
    ///     Converts the raw <c>--symbols</c> arguments into a <see cref="DecodingContext" /> and the
    ///     underlying list of <see cref="DecodingContextType" />s.
    ///     Returns a <see langword="null" /> context when no symbol paths were supplied or none could
    ///     be loaded, which disables WPP message formatting (raw events are still decoded via TDH/manifest).
    /// </summary>
    internal static (DecodingContext? Context, List<DecodingContextType> ContextTypes) ResolveSymbols(string[] symbolPaths)
    {
        List<DecodingContextType> contexts = [];

        if (symbolPaths.Length == 0)
        {
            return (null, contexts);
        }

        foreach (string arg in symbolPaths)
        {
            if (arg.Contains('*') || arg.Contains('?'))
            {
                // Glob pattern: split into directory root + file pattern.
                // Recurse only when the caller explicitly uses ** in the pattern.
                // NormalizeGlobRoot strips the standalone ** segment so Directory.Exists succeeds.
                // Reject before calling NormalizeGlobRoot so no filter is silently lost.
                if (PathGlobbing.ValidateGlobPattern(arg) is string validationError)
                {
                    Console.Error.WriteLine(validationError);
                    continue;
                }

                string root = PathGlobbing.NormalizeGlobRoot(arg);
                string pattern = Path.GetFileName(arg);
                bool recurse = arg.Contains("**");
                SearchOption search = recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

                if (!Directory.Exists(root))
                {
                    Console.Error.WriteLine($"[!] Glob root directory not found: {root}");
                    continue;
                }

                foreach (string file in Directory.EnumerateFiles(root, pattern, search))
                {
                    AddSymbolFile(contexts, file);
                }
            }
            else if (Directory.Exists(arg))
            {
                // Directory: collect all PDB files recursively (AllDirectories so subdirectories
                // are included — each PDB is loaded individually so recursion is safe).
                bool anyFound = false;

                foreach (string pdb in Directory.EnumerateFiles(arg, "*.pdb", SearchOption.AllDirectories))
                {
                    if (AddSymbolFile(contexts, pdb))
                    {
                        anyFound = true;
                    }
                }

                // For TMF: TmfFilesDirectoryDecodingContextType scans a flat directory, so add
                // each directory that *directly* contains .tmf files as a separate source.
                // Using AllDirectories in the file search lets us discover TMF dirs in subdirs too.
                IEnumerable<string> tmfDirs = Directory
                    .EnumerateFiles(arg, "*.tmf", SearchOption.AllDirectories)
                    .Select(f => Path.GetDirectoryName(f)!)
                    .Distinct();

                foreach (string tmfDir in tmfDirs)
                {
                    try
                    {
                        contexts.Add(new TmfFilesDirectoryDecodingContextType(tmfDir));
                        Console.Error.WriteLine($"    + {tmfDir} (TMF directory)");
                        anyFound = true;
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[!] Failed to load TMF directory '{tmfDir}': {ex.Message}");
                    }
                }

                if (!anyFound)
                {
                    Console.Error.WriteLine($"[!] No PDB or TMF files found in directory: {arg}");
                }
            }
            else if (File.Exists(arg))
            {
                AddSymbolFile(contexts, arg);
            }
            else
            {
                Console.Error.WriteLine($"[!] Symbol path not found: {arg}");
            }
        }

        if (contexts.Count == 0)
        {
            Console.Error.WriteLine("[!] No symbol files could be loaded; WPP messages will not be decoded.");
            return (null, contexts);
        }

        Console.Error.WriteLine($"[*] Loaded {contexts.Count} symbol source(s) for WPP decoding.");
        return (new DecodingContext(contexts), contexts);
    }

    /// <summary>
    ///     Adds a single symbol file to <paramref name="contexts" /> based on its extension.
    ///     Returns <see langword="true" /> if the file was loaded successfully.
    /// </summary>
    private static bool AddSymbolFile(List<DecodingContextType> contexts, string path)
    {
        string ext = Path.GetExtension(path);

        try
        {
            if (ext.Equals(".pdb", StringComparison.OrdinalIgnoreCase))
            {
                contexts.Add(new PdbFileDecodingContextType(path));
                Console.Error.WriteLine($"    + {path}");
                return true;
            }

            if (ext.Equals(".tmf", StringComparison.OrdinalIgnoreCase))
            {
                contexts.Add(new TmfFileDecodingContextType(path));
                Console.Error.WriteLine($"    + {path}");
                return true;
            }

            Console.Error.WriteLine($"[!] Unrecognized symbol file type (expected .pdb or .tmf): {path}");
            return false;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[!] Failed to load '{path}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    ///     Attempts to parse a file as a PDB and add it to the <paramref name="pdbContexts" /> list.
    ///     Only accepts files with a <c>.pdb</c> extension; silently skips anything else.
    ///     Returns <see langword="true" /> if the file was loaded successfully.
    /// </summary>
    internal static bool TryAddPdb(List<PdbFileDecodingContextType> pdbContexts, string path)
    {
        if (!Path.GetExtension(path).Equals(".pdb", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            PdbFileDecodingContextType ctx = new(path);
            pdbContexts.Add(ctx);
            Console.Error.WriteLine($"    + {path}");
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[!] Failed to load '{path}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    ///     Resolves the effective symbol-store server URL and local cache directory from CLI flags
    ///     or <c>_NT_SYMBOL_PATH</c> when both CLI flags are absent.
    ///     Supports <c>srv*cache*url</c>, <c>srv*url</c>, and <c>cache*dir</c> segments.
    /// </summary>
    internal static (string? Server, string Cache) ResolveSymbolStoreConfig(string? cliServer, string? cliCache)
    {
        string defaultCache = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Nefarius", "etwutils", "symcache");

        if (cliServer is not null || cliCache is not null)
            return (cliServer, cliCache ?? defaultCache);

        string? ntSymPath = Environment.GetEnvironmentVariable("_NT_SYMBOL_PATH");
        if (!string.IsNullOrWhiteSpace(ntSymPath))
        {
            string? symbolServer = null;
            string  symbolCache  = defaultCache;

            foreach (string segment in ntSymPath.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                string   seg   = segment.Trim();
                string[] parts = seg.Split('*');

                // srv*<cache>*<url>
                if (parts.Length == 3 &&
                    parts[0].Equals("srv", StringComparison.OrdinalIgnoreCase) &&
                    Uri.TryCreate(parts[2], UriKind.Absolute, out Uri? u1) &&
                    (u1.Scheme == Uri.UriSchemeHttp || u1.Scheme == Uri.UriSchemeHttps))
                {
                    symbolCache  = string.IsNullOrWhiteSpace(parts[1]) ? defaultCache : parts[1];
                    symbolServer = parts[2];
                    Console.Error.WriteLine(
                        $"[*] _NT_SYMBOL_PATH: cache='{symbolCache}', server='{symbolServer}'");
                    continue;
                }

                // cache*<dir>
                if (parts.Length == 2 &&
                    parts[0].Equals("cache", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(parts[1]))
                {
                    symbolCache = parts[1];
                    Console.Error.WriteLine($"[*] _NT_SYMBOL_PATH: cache='{symbolCache}'");
                    continue;
                }

                // srv*<url>
                if (parts.Length == 2 &&
                    parts[0].Equals("srv", StringComparison.OrdinalIgnoreCase) &&
                    Uri.TryCreate(parts[1], UriKind.Absolute, out Uri? u2) &&
                    (u2.Scheme == Uri.UriSchemeHttp || u2.Scheme == Uri.UriSchemeHttps))
                {
                    symbolServer = parts[1];
                    Console.Error.WriteLine($"[*] _NT_SYMBOL_PATH: server='{symbolServer}'");
                    continue;
                }

                Console.Error.WriteLine(
                    $"[*] Ignoring complex _NT_SYMBOL_PATH segment '{seg}'. " +
                    "Use --symbol-server / --symbol-cache for explicit control.");
            }

            return (symbolServer, symbolCache);
        }

        return (null, defaultCache);
    }

    /// <summary>
    ///     Attempts to read the GUID and Age from a PDB file using <c>Smx.PDBSharp</c>.
    ///     Supports Big-format (modern) PDBs only. Returns <see langword="null" /> on any failure.
    /// </summary>
    private static (Guid Guid, int Age)? TryReadPdbIdentity(string path)
    {
        try
        {
            using PDBFile pdb = PDBFile.Open(path);
            if (pdb.Type != PDBType.Big) return null;

            DBIReader    dbi       = pdb.Services.GetService<DBIReader>();
            PdbStreamReader pdbStr = pdb.Services.GetService<PdbStreamReader>();

            if (dbi is null || pdbStr is null) return null;
            if (dbi.Header is not DBIHeaderNew hdr) return null;
            if (pdbStr.NewSignature is not Guid guid) return null;

            return (guid, (int)hdr.Age);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    ///     Searches <paramref name="searchPaths" /> (directories recursively, globs) for a PDB
    ///     whose filename matches <paramref name="pdbFileName" /> case-insensitively <em>and</em>
    ///     whose embedded GUID and Age match <paramref name="expectedGuid" /> /
    ///     <paramref name="expectedAge" />.  Returns the first verified match or
    ///     <see langword="null" />.
    /// </summary>
    private static string? FindPdbInSearchPaths(
        string[] searchPaths,
        string   pdbFileName,
        Guid     expectedGuid,
        int      expectedAge)
    {
        foreach (string arg in searchPaths)
        {
            IEnumerable<string> candidates;

            if (arg.Contains('*') || arg.Contains('?'))
            {
                if (PathGlobbing.ValidateGlobPattern(arg) is not null) continue;

                string root    = PathGlobbing.NormalizeGlobRoot(arg);
                string pattern = Path.GetFileName(arg);
                if (!Directory.Exists(root)) continue;

                bool         recurse = arg.Contains("**");
                SearchOption search  = recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

                candidates = Directory.EnumerateFiles(root, pattern, search);
            }
            else if (Directory.Exists(arg))
            {
                candidates = Directory.EnumerateFiles(arg, "*.pdb", SearchOption.AllDirectories);
            }
            else
            {
                continue;
            }

            foreach (string file in candidates)
            {
                if (!Path.GetFileName(file).Equals(pdbFileName, StringComparison.OrdinalIgnoreCase))
                    continue;

                (Guid Guid, int Age)? id = TryReadPdbIdentity(file);
                if (id is null) continue;
                if (id.Value.Guid != expectedGuid || id.Value.Age != expectedAge) continue;

                return file;
            }
        }

        return null;
    }

    /// <summary>
    ///     Validates the explicit binary override path supplied via <c>--driver-binary</c>.
    ///     Prints an error and returns <see langword="null" /> when the file does not exist.
    /// </summary>
    internal static string? ResolveDriverBinaryOverride(string path)
    {
        string expanded = Environment.ExpandEnvironmentVariables(path);
        if (!File.Exists(expanded))
        {
            Console.Error.WriteLine(
                $"[!] --driver-binary: path does not exist: '{expanded}'.");
            return null;
        }

        return Path.GetFullPath(expanded);
    }

    /// <summary>
    ///     Resolves the on-disk binary path for a driver service by reading the
    ///     <c>ImagePath</c> REG_EXPAND_SZ value from the appropriate services registry key.
    ///     Kernel-mode drivers are looked up under <c>SYSTEM\CurrentControlSet\Services\&lt;name&gt;</c>;
    ///     UMDF drivers are looked up under
    ///     <c>SOFTWARE\Microsoft\Windows NT\CurrentVersion\WUDF\Services\&lt;name&gt;</c>.
    ///     Use <c>--driver-binary</c> to bypass the registry lookup entirely.
    ///     Uses <see cref="VerboseRegistry.Detect" /> for <c>auto</c> kind resolution.
    /// </summary>
    internal static string? ResolveDriverBinaryPath(string serviceName, ServiceKind? kind)
    {
        try
        {
        return ResolveDriverBinaryPathCore(serviceName, kind);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[!] --driver: failed to resolve binary path for '{serviceName}': " +
                $"{ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static string? ResolveDriverBinaryPathCore(string serviceName, ServiceKind? kind)
    {
        // Normalise a raw ImagePath value to an absolute, rooted file-system path.
        static string ExpandImagePath(string raw)
        {
            // Strip \??\ device-namespace prefix used by many kernel drivers.
            if (raw.StartsWith(@"\??\", StringComparison.OrdinalIgnoreCase))
                raw = raw[4..];

            // Map \SystemRoot\ → %SystemRoot%
            if (raw.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase))
            {
                string sysRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
                raw = Path.Combine(sysRoot, raw[12..]);
            }

            // Expand any remaining %Foo% environment variables.
            raw = Environment.ExpandEnvironmentVariables(raw);

            // Relative paths (e.g. "system32\drivers\foo.sys") are relative to %SystemRoot%.
            if (!Path.IsPathRooted(raw))
            {
                string sysRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
                raw = Path.Combine(sysRoot, raw);
            }

            return raw;
        }

        // Determine effective kind when 'auto' is requested.
        ServiceKind effectiveKind;
        if (kind is null)
        {
            DetectionResult detection = VerboseRegistry.Detect(serviceName);
            if (detection.Kernel is not null)
            {
                effectiveKind = ServiceKind.Kernel;
            }
            else if (detection.Umdf is not null)
            {
                effectiveKind = ServiceKind.Umdf;
                Console.Error.WriteLine(
                    $"[*] --driver: '{serviceName}' detected as UMDF (no kernel-mode candidate found). " +
                    "If ImagePath is absent, supply the binary path via --driver-binary.");
            }
            else
            {
                Console.Error.WriteLine(
                    $"[!] --driver: service '{serviceName}' was not found in the kernel or UMDF service hives. " +
                    "Verify the service name and that the driver is installed.");
                return null;
            }
        }
        else
        {
            effectiveKind = kind.Value;
        }

        string serviceKeyPath = effectiveKind == ServiceKind.Umdf
            ? $@"{VerboseRegistry.WudfRoot}\{serviceName}"
            : $@"{VerboseRegistry.ServicesRoot}\{serviceName}";

        using Microsoft.Win32.RegistryKey hklm = Microsoft.Win32.RegistryKey.OpenBaseKey(
            Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64);

        using Microsoft.Win32.RegistryKey? serviceKey = hklm.OpenSubKey(serviceKeyPath, writable: false);
        if (serviceKey is null)
        {
            Console.Error.WriteLine(
                $"[!] --driver: service registry key HKLM\\{serviceKeyPath} not found.");
            return null;
        }

        // Read without automatic expansion so we can normalize the path ourselves.
        object? imagePathVal = serviceKey.GetValue(
            "ImagePath", null, Microsoft.Win32.RegistryValueOptions.DoNotExpandEnvironmentNames);

        if (imagePathVal is null)
        {
            if (effectiveKind == ServiceKind.Umdf)
            {
                Console.Error.WriteLine(
                    $"[!] --driver: no ImagePath value found for UMDF service '{serviceName}'. " +
                    "Use --driver-binary to specify the WUDFHost-hosted DLL path explicitly.");
            }
            else
            {
                Console.Error.WriteLine(
                    $"[!] --driver: no ImagePath value found for service '{serviceName}'.");
            }
            return null;
        }

        string rawPath = imagePathVal.ToString()!;
        string resolvedPath = ExpandImagePath(rawPath);

        if (!File.Exists(resolvedPath))
        {
            Console.Error.WriteLine(
                $"[!] --driver: resolved binary path does not exist on disk: '{resolvedPath}' " +
                $"(ImagePath='{rawPath}'). The driver may not be installed.");
            return null;
        }

        return resolvedPath;
    }

    /// <summary>
    ///     Opens the PE binary at <paramref name="binaryPath" /> and reads the first
    ///     <c>CodeView</c> debug directory entry (RSDS format).
    ///     Returns a <see cref="PdbMetaData" /> populated from the embedded PDB path, GUID, and age,
    ///     or <see langword="null" /> when no CodeView entry exists or an error occurs.
    /// </summary>
    internal static PdbMetaData? TryReadPdbReference(string binaryPath)
    {
        try
        {
            using FileStream fs = new(binaryPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using PEReader pe   = new(fs);

            foreach (DebugDirectoryEntry entry in pe.ReadDebugDirectory())
            {
                if (entry.Type != DebugDirectoryEntryType.CodeView) continue;

                CodeViewDebugDirectoryData cv = pe.ReadCodeViewDebugDirectoryData(entry);

                return new PdbMetaData
                {
                    PdbName = cv.Path,
                    Guid    = cv.Guid,
                    Age     = cv.Age
                };
            }

            Console.Error.WriteLine(
                $"[!] --driver: no CodeView (RSDS) debug directory entry found in '{binaryPath}'. " +
                "The binary may be stripped or produced without PDB reference embedding.");
            return null;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[!] --driver: failed to read PE debug directory from '{binaryPath}': " +
                $"{ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    ///     Resolves a single PDB via (1) local cache, (2) <paramref name="searchPaths" /> directories,
    ///     and (3) a remote symbol server, in that order.
    ///     Returns the local file path of the resolved PDB, or <see langword="null" /> when the PDB
    ///     could not be found through any source.
    ///     Logs <c>[cache ]</c>, <c>[local ]</c>, <c>[server]</c>, or <c>[404   ]</c> lines to stderr.
    /// </summary>
    internal static async Task<string?> ResolveSinglePdbAsync(
        PdbMetaData    pdb,
        string[]       searchPaths,
        string?        symbolServerUrl,
        string         symbolCacheDir,
        HttpClient     http,
        CancellationToken cancellationToken)
    {
        string pdbFileName   = Path.GetFileName(pdb.PdbName);
        string indexPath     = pdb.IndexPrefix.Replace('/', Path.DirectorySeparatorChar);
        string cacheFilePath = Path.GetFullPath(Path.Combine(symbolCacheDir, indexPath));

        // 1. Cache hit
        if (File.Exists(cacheFilePath))
        {
            Console.Error.WriteLine($"    [cache ] {pdbFileName}");
            return cacheFilePath;
        }

        // 2. Local search paths
        if (searchPaths.Length > 0)
        {
            string? found = FindPdbInSearchPaths(searchPaths, pdbFileName, pdb.Guid, pdb.Age);
            if (found is not null)
            {
                Console.Error.WriteLine($"    [local ] {pdbFileName} <- {found}");
                return found;
            }
        }

        // 3. Symbol-server download
        if (!string.IsNullOrWhiteSpace(symbolServerUrl))
        {
            string url     = $"{symbolServerUrl!.TrimEnd('/')}/{pdb.IndexPrefix}";
            string tmpPath = cacheFilePath + ".tmp";

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cacheFilePath)!);

                using HttpResponseMessage response =
                    await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                              .ConfigureAwait(false);

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    Console.Error.WriteLine($"    [404   ] {pdbFileName} ({url})");
                    return null;
                }

                response.EnsureSuccessStatusCode();

                await using (FileStream tmp = new(tmpPath, FileMode.Create, FileAccess.Write,
                                 FileShare.None, 65536, useAsync: true))
                {
                    await response.Content.CopyToAsync(tmp, cancellationToken).ConfigureAwait(false);
                }

                File.Move(tmpPath, cacheFilePath, overwrite: true);
                Console.Error.WriteLine($"    [server] {pdbFileName} <- {url}");
                return cacheFilePath;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"[!] Failed to download '{pdbFileName}': {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        return null;
    }

    /// <summary>
    ///     Pre-scans the given .etl files with <see cref="EtwUtil.EnumeratePdbReferences" />, then
    ///     resolves each <see cref="PdbMetaData" /> against (1) the local symbol cache,
    ///     (2) the <paramref name="searchPaths" /> directories, and (3) a remote symbol server.
    ///     Unresolved references are logged to stderr and are non-fatal.
    /// </summary>
    internal static async Task<List<DecodingContextType>> ResolveAutoSymbolsAsync(
        List<string> etlFiles,
        string[] searchPaths,
        string? symbolServerUrl,
        string symbolCacheDir,
        HttpClient http,
        CancellationToken cancellationToken)
    {
        List<DecodingContextType> result = [];
        int fromCache = 0, fromLocal = 0, fromServer = 0, unresolved = 0;

        IReadOnlyCollection<PdbMetaData> refs;
        try
        {
            refs = EtwUtil.EnumeratePdbReferences(etlFiles);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[!] PDB pre-scan failed: {ex.GetType().Name}: {ex.Message}");
            return result;
        }

        if (refs.Count == 0)
        {
            Console.Error.WriteLine("[*] Auto-discovery: no PDB references found in the trace(s).");
            return result;
        }

        Console.Error.WriteLine(
            $"[*] Auto-discovery: resolving {refs.Count} PDB reference(s)...");
        Directory.CreateDirectory(symbolCacheDir);

        foreach (PdbMetaData pdb in refs)
        {
            if (cancellationToken.IsCancellationRequested) break;

            string pdbFileName   = Path.GetFileName(pdb.PdbName);
            string indexPath     = pdb.IndexPrefix.Replace('/', Path.DirectorySeparatorChar);
            string cacheFilePath = Path.GetFullPath(Path.Combine(symbolCacheDir, indexPath));

            // Snapshot whether the file exists in cache *before* resolution so we can
            // distinguish a pre-existing cache hit from a freshly downloaded file.
            bool wasAlreadyCached = File.Exists(cacheFilePath);

            string? resolved = await ResolveSinglePdbAsync(
                pdb, searchPaths, symbolServerUrl, symbolCacheDir, http, cancellationToken);

            if (resolved is not null)
            {
                try
                {
                    result.Add(new PdbFileDecodingContextType(resolved));

                    bool wasLocal = !resolved.Equals(cacheFilePath, StringComparison.OrdinalIgnoreCase);
                    if (wasLocal)            fromLocal++;
                    else if (wasAlreadyCached) fromCache++;
                    else                     fromServer++;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(
                        $"[!] PDB '{pdbFileName}' at '{resolved}' could not be loaded: {ex.Message}");
                    unresolved++;
                }
            }
            else
            {
                Console.Error.WriteLine(
                    $"    [miss  ] {pdbFileName} (guid={pdb.Guid:D}, age={pdb.Age})");
                unresolved++;
            }
        }

        Console.Error.WriteLine(
            $"[*] Auto-discovery complete: {result.Count}/{refs.Count} resolved " +
            $"({fromCache} cache, {fromLocal} local, {fromServer} downloaded, {unresolved} unresolved).");

        return result;
    }
}
