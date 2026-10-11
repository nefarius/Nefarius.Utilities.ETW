using Nefarius.Utilities.ETW.Deserializer.WPP;
using Nefarius.Utilities.ETW.Symbols;

namespace Nefarius.Utilities.ETW.CLI;

/// <summary>
///     CLI-facing wrapper around the reusable symbol and decoding-source APIs.
/// </summary>
internal static class SymbolResolution
{
    /// <summary>
    ///     Returns whether WPP provider-name rewrite should be enabled for the loaded sources.
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
            .Any(source => source.WppTraceControls.Any(control => !string.IsNullOrWhiteSpace(control.Name)));

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
    ///     Loads explicit PDB and TMF inputs and writes diagnostics to stderr.
    /// </summary>
    internal static (DecodingContext? Context, List<DecodingContextType> ContextTypes) ResolveSymbols(string[] symbolPaths)
    {
        DecodingSourceSet loaded = DecodingSources.Load(symbolPaths);
        foreach (string diagnostic in loaded.Diagnostics)
        {
            Console.Error.WriteLine(diagnostic);
        }

        return (loaded.Context, loaded.Sources.ToList());
    }

    /// <summary>
    ///     Loads one PDB file for inspection. Returns <see langword="false" /> for non-PDB paths and load failures.
    /// </summary>
    internal static bool TryAddPdb(List<PdbFileDecodingContextType> pdbContexts, string path)
    {
        if (!Path.GetExtension(path).Equals(".pdb", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            PdbFileDecodingContextType context = new(path);
            pdbContexts.Add(context);
            Console.Error.WriteLine($"    + {path}");
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[!] Failed to load '{path}': {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    ///     Resolves the effective symbol-server URL and local cache directory from CLI flags or
    ///     <c>_NT_SYMBOL_PATH</c> when both CLI flags are absent.
    /// </summary>
    internal static (string? Server, string Cache) ResolveSymbolStoreConfig(string? cliServer, string? cliCache)
    {
        string defaultCache = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Nefarius", "etwutils", "symcache");

        SymbolStoreConfiguration configuration = SymbolStoreConfiguration.Resolve(
            cliServer,
            cliCache,
            Environment.GetEnvironmentVariable("_NT_SYMBOL_PATH"),
            defaultCache);

        foreach (string notice in configuration.Notices)
        {
            Console.Error.WriteLine(notice);
        }

        return (configuration.ServerUrl, configuration.CacheDirectory);
    }

    /// <summary>
    ///     Validates the explicit binary override path supplied via <c>--driver-binary</c>.
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

    /// <summary>
    ///     Opens the PE binary at <paramref name="binaryPath" /> and reads the first CodeView debug entry.
    /// </summary>
    internal static PdbMetaData? TryReadPdbReference(string binaryPath)
    {
        CodeViewReadResult result = PortableExecutableSymbols.TryReadCodeViewPdb(binaryPath);
        if (result.Metadata is null && result.Diagnostic is not null)
        {
            Console.Error.WriteLine($"[!] --driver: {result.Diagnostic}");
        }

        return result.Metadata;
    }

    /// <summary>
    ///     Resolves one PDB through the shared symbol store and writes the same status lines as before.
    /// </summary>
    internal static async Task<string?> ResolveSinglePdbAsync(
        PdbMetaData pdb,
        string[] searchPaths,
        string? symbolServerUrl,
        string symbolCacheDir,
        HttpClient http,
        CancellationToken cancellationToken)
    {
        SymbolStoreConfiguration configuration = new(symbolCacheDir, symbolServerUrl, searchPaths);
        ResolvedPdb resolved = (await SymbolStore.ResolveAsync([pdb], configuration, http, cancellationToken))[0];
        string fileName = Path.GetFileName(pdb.PdbName);
        switch (resolved.Origin)
        {
            case PdbResolutionOrigin.Cache:
                Console.Error.WriteLine($"    [cache ] {fileName}");
                return resolved.LocalPath;
            case PdbResolutionOrigin.LocalSearch:
                Console.Error.WriteLine($"    [local ] {fileName} <- {resolved.LocalPath}");
                return resolved.LocalPath;
            case PdbResolutionOrigin.SymbolServer:
                Console.Error.WriteLine($"    [server] {fileName} <- {resolved.Detail}");
                return resolved.LocalPath;
            default:
                if (resolved.Failure == PdbResolutionFailure.NotFoundOnServer)
                {
                    Console.Error.WriteLine($"    [404   ] {fileName} ({resolved.Detail})");
                }
                else if (resolved.Failure == PdbResolutionFailure.DownloadFailed)
                {
                    Console.Error.WriteLine($"[!] Failed to download '{fileName}': {resolved.Detail}");
                }

                return null;
        }
    }

    /// <summary>
    ///     Pre-scans ETL files and resolves every referenced PDB.
    /// </summary>
    internal static async Task<List<DecodingContextType>> ResolveAutoSymbolsAsync(
        List<string> etlFiles,
        string[] searchPaths,
        string? symbolServerUrl,
        string symbolCacheDir,
        HttpClient http,
        CancellationToken cancellationToken)
    {
        SymbolStoreConfiguration configuration = new(symbolCacheDir, symbolServerUrl, searchPaths);
        TraceSymbolResolution resolved = await DecodingSources.ResolveTraceSymbolsAsync(
            etlFiles,
            configuration,
            http,
            cancellationToken);

        foreach (string diagnostic in resolved.Diagnostics)
        {
            Console.Error.WriteLine(diagnostic);
        }

        return resolved.Sources.ToList();
    }

    private static string? ResolveDriverBinaryPathCore(string serviceName, ServiceKind? kind)
    {
        static string ExpandImagePath(string raw)
        {
            if (raw.StartsWith(@"\??\", StringComparison.OrdinalIgnoreCase))
            {
                raw = raw[4..];
            }

            if (raw.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase))
            {
                string sysRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
                raw = Path.Combine(sysRoot, raw[12..]);
            }

            raw = Environment.ExpandEnvironmentVariables(raw);
            if (!Path.IsPathRooted(raw))
            {
                string sysRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
                raw = Path.Combine(sysRoot, raw);
            }

            return raw;
        }

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
}
