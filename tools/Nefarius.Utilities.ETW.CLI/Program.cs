using System.CommandLine;
using System.Globalization;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

using Nefarius.Utilities.ETW;
using Nefarius.Utilities.ETW.Deserializer.WPP;
using Nefarius.Utilities.ETW.Deserializer.WPP.TMF;

using Smx.PDBSharp;

// ---------------------------------------------------------------------------
// Arguments and options
// ---------------------------------------------------------------------------
Argument<Guid[]> providerArg = new("provider-guid")
{
    Description =
        "One or more ETW provider GUIDs to enable (e.g. {12345678-...}). " +
        "When omitted, provider GUIDs are auto-derived from any PDB files passed to --symbols. " +
        "All providers share the same keyword/level filters.",
    Arity = ArgumentArity.ZeroOrMore
};

Option<string> keywordsOpt = new("--keywords")
{
    Description = "Match-any keyword mask. Accepts hex (0xABCD) or decimal. Default: all keywords.",
    DefaultValueFactory = _ => "0xFFFFFFFFFFFFFFFF"
};

Option<string> matchAllOpt = new("--match-all-keywords")
{
    Description = "Match-all keyword mask. Accepts hex (0xABCD) or decimal. Default: disabled (0).",
    DefaultValueFactory = _ => "0"
};

Option<TraceEventLevel> levelOpt = new("--level")
{
    Description = "Maximum event severity level to capture.",
    DefaultValueFactory = _ => TraceEventLevel.Verbose
};

Option<string> sessionNameOpt = new("--session-name")
{
    Description = "ETW session name. Defaults to NefariusEtwCli-<pid>.",
    DefaultValueFactory = _ => $"NefariusEtwCli-{Environment.ProcessId}"
};

Option<string[]> symbolsOpt = new("--symbols")
{
    Description =
        "Path to a PDB file, TMF file, directory (searched recursively), or a glob pattern " +
        "(e.g. C:\\Symbols\\*.pdb). Repeat the flag for multiple paths.",
    Arity = ArgumentArity.ZeroOrMore,
    AllowMultipleArgumentsPerToken = false
};

Option<uint> bufferSizeOpt = new("--buffer-size-kb")
{
    Description = "ETW buffer size in kilobytes per buffer.",
    DefaultValueFactory = _ => 64u
};

Option<uint> flushSecondsOpt = new("--flush-seconds")
{
    Description = "How often (in seconds) the session flushes in-flight buffers.",
    DefaultValueFactory = _ => 1u
};

Option<string> realtimeFormatOpt = new("--format")
{
    Description = "Output format: 'ndjson' (default) streams one JSON object per line; " +
                  "'plain' emits tab-separated Timestamp/Provider/Level/Message columns.",
    DefaultValueFactory = _ => "ndjson"
};

Option<string> colorOpt = new("--color")
{
    Description = "Colorize the Level column in plain mode. " +
                  "'auto' (default) enables color when stdout is a TTY and NO_COLOR is unset; " +
                  "'always' forces color; 'never' disables it.",
    DefaultValueFactory = _ => "auto"
};

// ---------------------------------------------------------------------------
// 'realtime' subcommand
// ---------------------------------------------------------------------------
Command realtime = new(
    "realtime",
    "Attach to a realtime ETW session, enable a provider, and stream decoded events on stdout.")
{
    providerArg,
    keywordsOpt,
    matchAllOpt,
    levelOpt,
    sessionNameOpt,
    symbolsOpt,
    bufferSizeOpt,
    flushSecondsOpt,
    realtimeFormatOpt,
    colorOpt
};

realtime.SetAction(async (ParseResult result, CancellationToken cancellationToken) =>
{
    Guid[] explicitProviders = result.GetValue(providerArg) ?? [];
    string keywordsRaw = result.GetValue(keywordsOpt)!;
    string matchAllRaw = result.GetValue(matchAllOpt)!;
    TraceEventLevel level = result.GetValue(levelOpt);
    string sessionName = result.GetValue(sessionNameOpt)!;
    string[] symbolPaths = result.GetValue(symbolsOpt) ?? [];
    uint bufferSizeKb = result.GetValue(bufferSizeOpt);
    uint flushSeconds = result.GetValue(flushSecondsOpt);
    string formatRaw = result.GetValue(realtimeFormatOpt)!;
    string colorRaw = result.GetValue(colorOpt)!;

    if (!formatRaw.Equals("ndjson", StringComparison.OrdinalIgnoreCase) &&
        !formatRaw.Equals("plain", StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine(
            $"[!] Unknown --format value '{formatRaw}'. Expected 'ndjson' or 'plain'.");
        return 2;
    }

    if (!colorRaw.Equals("auto", StringComparison.OrdinalIgnoreCase) &&
        !colorRaw.Equals("always", StringComparison.OrdinalIgnoreCase) &&
        !colorRaw.Equals("never", StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine(
            $"[!] Unknown --color value '{colorRaw}'. Expected 'auto', 'always', or 'never'.");
        return 2;
    }

    bool usePlain = formatRaw.Equals("plain", StringComparison.OrdinalIgnoreCase);
    bool useColor = usePlain && ResolveColor(colorRaw);

    ulong matchAny;
    ulong matchAll;
    try
    {
        matchAny = ParseKeywordMask(keywordsRaw);
        matchAll = ParseKeywordMask(matchAllRaw);
    }
    catch (Exception ex) when (ex is FormatException or OverflowException)
    {
        Console.Error.WriteLine(
            $"[!] Invalid keyword value. Use decimal (255) or hex (0xFF). " +
            $"Got: keywords='{keywordsRaw}' match-all='{matchAllRaw}'");
        return 2;
    }

    return await RunAsync(
        explicitProviders,
        matchAny,
        matchAll,
        level,
        sessionName,
        symbolPaths,
        bufferSizeKb,
        flushSeconds,
        usePlain,
        useColor,
        cancellationToken);
});

// ---------------------------------------------------------------------------
// 'inspect-pdb' subcommand
// ---------------------------------------------------------------------------
Argument<string[]> inspectPathsArg = new("path")
{
    Description =
        "One or more paths to .pdb files, directories (searched for *.pdb), " +
        "or glob patterns (e.g. C:\\Symbols\\*.pdb). TMF files are ignored as they do not " +
        "contain WPP control GUID information.",
    Arity = ArgumentArity.OneOrMore
};

Option<string> inspectFormatOpt = new("--format")
{
    Description = "Output format: 'plain' (default) or 'ndjson'.",
    DefaultValueFactory = _ => "plain"
};

Command inspectPdb = new(
    "inspect-pdb",
    "Parse one or more PDB files and list the WPP provider GUIDs (control GUIDs) embedded in " +
    "them via TMC: annotations. No ETW session is created. Output goes to stdout; status messages " +
    "go to stderr.")
{
    inspectPathsArg,
    inspectFormatOpt
};

inspectPdb.SetAction((ParseResult result) =>
{
    string[] paths = result.GetValue(inspectPathsArg)!;
    string formatValue = result.GetValue(inspectFormatOpt)!;

    if (!formatValue.Equals("plain", StringComparison.OrdinalIgnoreCase) &&
        !formatValue.Equals("ndjson", StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine(
            $"[!] Unknown --format value '{formatValue}'. Expected 'plain' or 'ndjson'.");
        return 1;
    }

    bool useNdjson = formatValue.Equals("ndjson", StringComparison.OrdinalIgnoreCase);

    // Collect PDB contexts only — TMF sources never carry a control GUID.
    List<PdbFileDecodingContextType> pdbContexts = [];

    foreach (string arg in paths)
    {
        if (arg.Contains('*') || arg.Contains('?'))
        {
            if (ValidateGlobPattern(arg) is string validationError)
            {
                Console.Error.WriteLine(validationError);
                continue;
            }

            string root = NormalizeGlobRoot(arg);
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
                TryAddPdb(pdbContexts, file);
            }
        }
        else if (Directory.Exists(arg))
        {
            bool any = false;
            foreach (string pdb in Directory.EnumerateFiles(arg, "*.pdb", SearchOption.TopDirectoryOnly))
            {
                if (TryAddPdb(pdbContexts, pdb)) { any = true; }
            }

            if (!any)
            {
                Console.Error.WriteLine($"[!] No PDB files found in directory: {arg}");
            }
        }
        else if (File.Exists(arg))
        {
            TryAddPdb(pdbContexts, arg);
        }
        else
        {
            Console.Error.WriteLine($"[!] Path not found: {arg}");
        }
    }

    if (pdbContexts.Count == 0)
    {
        Console.Error.WriteLine("[!] No PDB files could be loaded.");
        return 1;
    }

    // Collect all TMC records across every loaded PDB.
    List<WppTraceControl> allControls = pdbContexts
        .SelectMany(ctx => ctx.WppTraceControls)
        .DistinctBy(c => c.ControlGuid)
        .OrderBy(c => c.Name)
        .ThenBy(c => c.ControlGuid)
        .ToList();

    if (allControls.Count == 0)
    {
        Console.Error.WriteLine(
            "[!] No WPP control GUIDs (TMC: annotations) found in the supplied PDB files. " +
            "The PDB must have been compiled with WPP tracing enabled.");
        return 1;
    }

    if (useNdjson)
    {
        JsonSerializerOptions jsonOpts = new() { WriteIndented = false };
        foreach (WppTraceControl ctrl in allControls)
        {
            string json = JsonSerializer.Serialize(new
            {
                guid = ctrl.ControlGuid.ToString("B").ToUpperInvariant(),
                name = ctrl.Name,
                bitFlags = ctrl.BitFlags,
                source = ctrl.OriginalSymbolFileName ?? string.Empty
            }, jsonOpts);
            Console.WriteLine(json);
        }
    }
    else
    {
        foreach (WppTraceControl ctrl in allControls)
        {
            string source = ctrl.OriginalSymbolFileName ?? "unknown";
            Console.WriteLine(
                $"{ctrl.ControlGuid.ToString("B").ToUpperInvariant(),-42}  " +
                $"{ctrl.Name,-30}  " +
                $"({source}, {ctrl.BitFlags.Count} bit flags)");

            foreach (string flag in ctrl.BitFlags)
            {
                Console.WriteLine($"    {flag}");
            }
        }
    }

    return 0;
});

// ---------------------------------------------------------------------------
// 'parse' subcommand – arguments and options
// ---------------------------------------------------------------------------
Argument<string[]> parseEtlPathsArg = new("etl-path")
{
    Description =
        "One or more .etl files, directories (top-level *.etl), or glob patterns " +
        "(e.g. C:\\Traces\\*.etl). Repeat for multiple paths.",
    Arity = ArgumentArity.OneOrMore
};

Option<string?> parseOutDirOpt = new("--out-dir")
{
    Description =
        "Write each input .etl to an equally-named file " +
        "(e.g. trace.etl → trace.ndjson or trace.tsv) in this directory " +
        "instead of streaming all events merged to stdout."
};

Option<string[]> parseSymbolsOpt = new("--symbols")
{
    Description =
        "Path to a PDB file, TMF file, directory (searched recursively), or a glob pattern. " +
        "Repeat the flag for multiple paths. These are loaded unconditionally.",
    Arity = ArgumentArity.ZeroOrMore,
    AllowMultipleArgumentsPerToken = false
};

Option<string[]> parseSymbolsSearchOpt = new("--symbols-search")
{
    Description =
        "Directories or glob patterns searched for PDBs that the trace actually references. " +
        "Resolved via a pre-scan of the .etl files. Repeat for multiple paths.",
    Arity = ArgumentArity.ZeroOrMore,
    AllowMultipleArgumentsPerToken = false
};

Option<string?> parseSymbolServerOpt = new("--symbol-server")
{
    Description =
        "Microsoft-style symbol store root URL " +
        "(e.g. https://msdl.microsoft.com/download/symbols or " +
        "https://symbols.nefarius.at/download/symbols). " +
        "Unresolved PDBs are downloaded and cached atomically."
};

Option<string?> parseSymbolCacheOpt = new("--symbol-cache")
{
    Description =
        "Local symstore-layout cache directory. " +
        "Defaults to %LOCALAPPDATA%\\Nefarius\\etwutils\\symcache. " +
        "Falls back to _NT_SYMBOL_PATH when this flag and --symbol-server are both absent."
};

Option<string> parseFormatOpt = new("--format")
{
    Description = "Output format: 'ndjson' (default) streams one JSON object per line; " +
                  "'plain' emits tab-separated Timestamp/Provider/Level/Message columns.",
    DefaultValueFactory = _ => "ndjson"
};

Option<string> parseColorOpt = new("--color")
{
    Description =
        "Colorize the Level column in plain stdout mode. " +
        "'auto' (default) enables color when stdout is a TTY and NO_COLOR is unset; " +
        "'always' forces color; 'never' disables it. Ignored in per-file (--out-dir) mode.",
    DefaultValueFactory = _ => "auto"
};

Option<bool> parsePreserveRawTimestampsOpt = new("--preserve-raw-timestamps")
{
    Description = "Apply PROCESS_TRACE_MODE_RAW_TIMESTAMP when processing the trace."
};

// ---------------------------------------------------------------------------
// 'parse' subcommand
// ---------------------------------------------------------------------------
Command parse = new(
    "parse",
    "Decode one or more offline .etl files and emit events as NDJSON (or plain TSV) " +
    "to stdout (events time-merged across all inputs) or to per-file output in a " +
    "target directory (--out-dir). WPP symbols are resolved automatically when " +
    "--symbols-search or --symbol-server is supplied.")
{
    parseEtlPathsArg,
    parseOutDirOpt,
    parseSymbolsOpt,
    parseSymbolsSearchOpt,
    parseSymbolServerOpt,
    parseSymbolCacheOpt,
    parseFormatOpt,
    parseColorOpt,
    parsePreserveRawTimestampsOpt
};

parse.SetAction(async (ParseResult result, CancellationToken cancellationToken) =>
{
    string[] etlPathArgs       = result.GetValue(parseEtlPathsArg) ?? [];
    string?  outDir            = result.GetValue(parseOutDirOpt);
    string[] symbolPaths       = result.GetValue(parseSymbolsOpt) ?? [];
    string[] symbolSearchPaths = result.GetValue(parseSymbolsSearchOpt) ?? [];
    string?  symbolServer      = result.GetValue(parseSymbolServerOpt);
    string?  symbolCache       = result.GetValue(parseSymbolCacheOpt);
    string   formatRaw         = result.GetValue(parseFormatOpt)!;
    string   colorRaw          = result.GetValue(parseColorOpt)!;
    bool     preserveRaw       = result.GetValue(parsePreserveRawTimestampsOpt);

    // --- Validate format / color ---
    if (!formatRaw.Equals("ndjson", StringComparison.OrdinalIgnoreCase) &&
        !formatRaw.Equals("plain", StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine(
            $"[!] Unknown --format value '{formatRaw}'. Expected 'ndjson' or 'plain'.");
        return 2;
    }

    if (!colorRaw.Equals("auto", StringComparison.OrdinalIgnoreCase) &&
        !colorRaw.Equals("always", StringComparison.OrdinalIgnoreCase) &&
        !colorRaw.Equals("never", StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine(
            $"[!] Unknown --color value '{colorRaw}'. Expected 'auto', 'always', or 'never'.");
        return 2;
    }

    // --- Validate --symbol-server ---
    if (symbolServer is not null)
    {
        if (!Uri.TryCreate(symbolServer, UriKind.Absolute, out Uri? parsedUri) ||
            (parsedUri.Scheme != Uri.UriSchemeHttp && parsedUri.Scheme != Uri.UriSchemeHttps))
        {
            Console.Error.WriteLine(
                $"[!] --symbol-server must be an absolute http(s) URL. Got: '{symbolServer}'");
            return 2;
        }
    }

    // --- Validate --out-dir ---
    if (outDir is not null && File.Exists(outDir))
    {
        Console.Error.WriteLine(
            $"[!] --out-dir '{outDir}' already exists as a file, not a directory.");
        return 2;
    }

    bool usePlain = formatRaw.Equals("plain", StringComparison.OrdinalIgnoreCase);

    // Color only applies to plain stdout mode; per-file and NDJSON ignore it.
    bool useColor = usePlain && outDir is null && ResolveColor(colorRaw);

    if (useColor)
    {
        TryEnableWindowsVt();
    }

    // --- Resolve ETL inputs ---
    List<string> etlFiles = ResolveEtlInputs(etlPathArgs);
    if (etlFiles.Count == 0)
    {
        Console.Error.WriteLine("[!] No .etl files found for the given inputs.");
        return 1;
    }

    Console.Error.WriteLine($"[*] Resolved {etlFiles.Count} .etl file(s).");

    // --- Resolve explicit --symbols (unconditional, same as 'realtime') ---
    (_, List<DecodingContextType> explicitTypes) = ResolveSymbols(symbolPaths);

    // --- Determine effective symbol-store config (CLI flags → _NT_SYMBOL_PATH → defaults) ---
    (string? effectiveServer, string effectiveCache) = ResolveSymbolStoreConfig(symbolServer, symbolCache);

    // Auto-discovery is triggered when any symbol source is active.
    bool runAutoDiscovery = symbolSearchPaths.Length > 0
                            || effectiveServer is not null
                            || symbolCache is not null;

    List<DecodingContextType> autoTypes = [];
    if (runAutoDiscovery)
    {
        string assemblyVersion =
            Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ?? "0.0.0";

        using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"etwutils/{assemblyVersion}");

        autoTypes = await ResolveAutoSymbolsAsync(
            etlFiles, symbolSearchPaths, effectiveServer, effectiveCache, http, cancellationToken);
    }

    // Merge explicit + auto into one DecodingContext.
    List<DecodingContextType> allTypes = [..explicitTypes, ..autoTypes];
    DecodingContext? decodingContext = allTypes.Count > 0 ? new DecodingContext(allTypes) : null;

    if (outDir is not null)
    {
        return await RunParsePerFileAsync(
            etlFiles, outDir, decodingContext, usePlain, preserveRaw, cancellationToken);
    }

    return await RunParseStdoutAsync(
        etlFiles, decodingContext, usePlain, useColor, preserveRaw, cancellationToken);
});

// ---------------------------------------------------------------------------
// Root command
// ---------------------------------------------------------------------------
RootCommand root = new("etwutils — ETW event decoder and offline trace parser. Stream realtime events or decode .etl files as NDJSON on stdout.")
{
    realtime,
    inspectPdb,
    parse
};

return await root.Parse(args).InvokeAsync();

// ---------------------------------------------------------------------------
// Implementation
// ---------------------------------------------------------------------------

static ulong ParseKeywordMask(string raw)
{
    ReadOnlySpan<char> s = raw.AsSpan().Trim();
    if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
    {
        return ulong.Parse(s[2..], NumberStyles.HexNumber);
    }

    return ulong.Parse(s, NumberStyles.None);
}

static async Task<int> RunAsync(
    Guid[] explicitProviderGuids,
    ulong matchAnyKeyword,
    ulong matchAllKeyword,
    TraceEventLevel level,
    string sessionName,
    string[] symbolPaths,
    uint bufferSizeKb,
    uint flushSeconds,
    bool usePlain,
    bool useColor,
    CancellationToken cancellationToken)
{
    // Resolve --symbols paths into a DecodingContext and the raw context list.
    (DecodingContext? decodingContext, List<DecodingContextType> contextTypes) = ResolveSymbols(symbolPaths);

    // Determine the final provider list: explicit args take precedence; fall back to WPP control
    // GUIDs extracted from TMC: annotations in the PDB files.
    Guid[] providerGuids;
    bool autoderived = false;
    if (explicitProviderGuids.Length > 0)
    {
        providerGuids = explicitProviderGuids;
    }
    else
    {
        providerGuids = contextTypes
            .SelectMany(t => t.ProviderGuids)
            .Distinct()
            .ToArray();
        autoderived = providerGuids.Length > 0;
    }

    if (providerGuids.Length == 0)
    {
        if (symbolPaths.Length > 0)
        {
            Console.Error.WriteLine(
                "[!] No provider GUIDs could be derived from the supplied --symbols paths. " +
                "Only PDB files carry WPP control GUID information (TMC: annotations). " +
                "TMF files alone are not sufficient — either pass a PDB file or supply the " +
                "provider-guid argument explicitly.");
        }
        else
        {
            Console.Error.WriteLine(
                "[!] No provider GUIDs available. Supply at least one provider-guid argument, " +
                "or pass --symbols with one or more PDB files containing WPP annotations.");
        }

        return 2;
    }

    // Build a linked CTS so both Ctrl+C and the caller's token can cancel.
    using CancellationTokenSource cts =
        CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

    // Print the "stopping" notice the moment the token is cancelled, regardless
    // of what triggered it (Ctrl+C or ProcessExit), so it always appears before
    // the finally-block "[*] Done." message.
    cts.Token.Register(() => Console.Error.WriteLine("\r[*] Interrupt received — stopping session..."));

    // Ctrl+C: cancel gracefully without immediately terminating the process.
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        try { cts.Cancel(); } catch (ObjectDisposedException) { }
    };

    // Process-exit: ensure the ETW session is torn down even when the process
    // exits via Environment.Exit() or an unhandled exception finaliser.
    AppDomain.CurrentDomain.ProcessExit += (_, _) =>
    {
        try { cts.Cancel(); } catch (ObjectDisposedException) { }
    };

    // Remove any orphan session from a previous crash before we start.
    EtwUtil.StopOrphanSession(sessionName);

    // Choose output writer based on format.
    // NDJSON: raw byte BufferedStream for maximum throughput.
    // Plain: StreamWriter (UTF-8, 64 KB) for text formatting.
    Stream? ndjsonOut = usePlain ? null : new BufferedStream(Console.OpenStandardOutput(), 65536);
    StreamWriter? plainOut = usePlain
        ? new StreamWriter(Console.OpenStandardOutput(), new System.Text.UTF8Encoding(false), 65536)
        : null;

    if (usePlain && useColor)
    {
        TryEnableWindowsVt();
    }

    try
    {
        using EtwRealtimeSession session = EtwRealtimeSession.Create(sessionName, opts =>
        {
            opts.BufferSizeKb = bufferSizeKb;
            opts.FlushTimerSeconds = flushSeconds;
        });

        foreach (Guid guid in providerGuids)
        {
            session.EnableProvider(guid, level, matchAnyKeyword, matchAllKeyword);
        }

        string providerSource = autoderived ? "auto-derived from symbols" : "explicit";
        Console.Error.WriteLine(
            $"[*] Session '{sessionName}' started. " +
            $"Providers ({providerSource}): {string.Join(", ", providerGuids.Select(g => $"{{{g}}}"))} | " +
            $"level={level} | keywords=0x{matchAnyKeyword:X} | matchAll=0x{matchAllKeyword:X}");
        Console.Error.WriteLine("[*] Streaming events... (Ctrl+C to stop)");

        await foreach (ReadOnlyMemory<byte> json in EtwUtil.EnumerateRealtimeEventsAsync(
                           sessionName,
                           o => o.WppDecodingContext = decodingContext,
                           cts.Token))
        {
            // Stop emitting immediately once cancellation is requested so no
            // buffered events leak out after Ctrl+C.
            if (cts.Token.IsCancellationRequested) break;

            if (usePlain)
            {
                await WritePlainLineAsync(json, plainOut!, useColor, cts.Token);
            }
            else
            {
                // Each buffer is a self-contained JSON object; append a newline for NDJSON,
                // then flush immediately so events appear in realtime rather than waiting
                // for the buffer to fill.
                await ndjsonOut!.WriteAsync(json, cts.Token);
                ndjsonOut.WriteByte((byte)'\n');
                await ndjsonOut.FlushAsync(cts.Token);
            }
        }
    }
    catch (OperationCanceledException)
    {
        // Expected on Ctrl+C — not an error.
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[!] Fatal error: {ex.GetType().Name}: {ex.Message}");
        return 1;
    }
    finally
    {
        // Flush remaining bytes regardless of how we exited.
        if (usePlain)
        {
            await plainOut!.FlushAsync(CancellationToken.None);
            await plainOut.DisposeAsync();
        }
        else
        {
            await ndjsonOut!.FlushAsync(CancellationToken.None);
            await ndjsonOut.DisposeAsync();
        }

        Console.Error.WriteLine("[*] Done.");
    }

    return 0;
}

/// <summary>
///     Converts the raw <c>--symbols</c> arguments into a <see cref="DecodingContext" /> and the
///     underlying list of <see cref="DecodingContextType" />s.
///     Returns a <see langword="null" /> context when no symbol paths were supplied or none could
///     be loaded, which disables WPP message formatting (raw events are still decoded via TDH/manifest).
/// </summary>
static (DecodingContext? Context, List<DecodingContextType> ContextTypes) ResolveSymbols(string[] symbolPaths)
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
            if (ValidateGlobPattern(arg) is string validationError)
            {
                Console.Error.WriteLine(validationError);
                continue;
            }

            string root = NormalizeGlobRoot(arg);
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
static bool AddSymbolFile(List<DecodingContextType> contexts, string path)
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
///     Validates that a glob pattern does not contain wildcard characters inside any directory
///     segment except the standalone <c>**</c> recursion marker.
///     Returns a non-null, ready-to-print error message when the pattern is invalid;
///     returns <see langword="null" /> when the pattern is acceptable.
/// </summary>
/// <remarks>
///     Allowed: <c>C:\Symbols\*.pdb</c> (filename wildcard) and
///     <c>C:\Symbols\**\*.pdb</c> (standalone <c>**</c> recursion marker).
///     Rejected: <c>C:\Sym*\*.pdb</c> or <c>C:\Symbols\sub?\*.pdb</c>
///     (wildcards in a non-terminal, non-<c>**</c> directory segment).
///     Also rejected: <c>C:\Symbols\**</c> or a trailing separator — patterns
///     with no filename component produce an invalid <c>searchPattern</c> for
///     <see cref="Directory.EnumerateFiles" />.
/// </remarks>
static string? ValidateGlobPattern(string globArg)
{
    string[] segments = globArg.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);

    // Every segment except the last is a directory segment.
    for (int i = 0; i < segments.Length - 1; i++)
    {
        string seg = segments[i];
        if (seg == "**") { continue; } // recursion marker — explicitly allowed

        if (seg.Contains('*') || seg.Contains('?'))
        {
            return $"[!] Wildcard in directory segment '{seg}' is not supported in '{globArg}'. " +
                   $"Use a filename-only wildcard (e.g. *.pdb) or '**' for recursion " +
                   $"(e.g. Symbols\\**\\*.pdb).";
        }
    }

    // The last segment is the filename pattern passed to Directory.EnumerateFiles as
    // searchPattern. "**" and empty are not valid searchPattern values and would throw
    // ArgumentException at runtime; reject them here with a clear message.
    // Use Path.GetFileName rather than segments[^1] so that a trailing separator
    // (e.g. "C:\Symbols\*.pdb\") is detected: Split(..., RemoveEmptyEntries) silently
    // drops the trailing empty segment, but Path.GetFileName returns "" in that case.
    string lastSeg = Path.GetFileName(globArg);
    if (lastSeg == "**" || lastSeg.Length == 0)
    {
        return $"[!] Glob pattern '{globArg}' has no filename component. " +
               $"Use a filename wildcard (e.g. *.pdb) or '**' with a filename " +
               $"(e.g. Symbols\\**\\*.pdb).";
    }

    return null;
}

/// <summary>
///     Returns the last path-directory segment of <paramref name="globArg" /> that does not
///     contain any wildcard characters (<c>*</c> or <c>?</c>).
///     For example, <c>C:\Symbols\**\*.pdb</c> yields <c>C:\Symbols</c> rather than
///     <c>C:\Symbols\**</c>, which allows <see cref="Directory.Exists" /> to succeed.
/// </summary>
static string NormalizeGlobRoot(string globArg)
{
    string? dir = Path.GetDirectoryName(globArg);
    if (string.IsNullOrEmpty(dir)) return ".";

    while (dir.Contains('*') || dir.Contains('?'))
    {
        string? parent = Path.GetDirectoryName(dir);
        if (parent is null || parent.Length == 0 || parent == dir)
        {
            return ".";
        }

        dir = parent;
    }

    return dir;
}

/// <summary>
///     Attempts to parse a file as a PDB and add it to the <paramref name="pdbContexts" /> list.
///     Only accepts files with a <c>.pdb</c> extension; silently skips anything else.
///     Returns <see langword="true" /> if the file was loaded successfully.
/// </summary>
static bool TryAddPdb(List<PdbFileDecodingContextType> pdbContexts, string path)
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
static (string? Server, string Cache) ResolveSymbolStoreConfig(string? cliServer, string? cliCache)
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
///     Expands the raw <c>etl-path</c> argument values into a deduplicated, ordered list of
///     absolute .etl file paths. Accepts files, directories (top-level <c>*.etl</c>), and
///     glob patterns.
/// </summary>
static List<string> ResolveEtlInputs(string[] rawArgs)
{
    Dictionary<string, string> seen = new(StringComparer.OrdinalIgnoreCase);

    foreach (string arg in rawArgs)
    {
        if (arg.Contains('*') || arg.Contains('?'))
        {
            if (ValidateGlobPattern(arg) is string validationError)
            {
                Console.Error.WriteLine(validationError);
                continue;
            }

            string root    = NormalizeGlobRoot(arg);
            string pattern = Path.GetFileName(arg);
            bool   recurse = arg.Contains("**");
            SearchOption search = recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            if (!Directory.Exists(root))
            {
                Console.Error.WriteLine($"[!] Glob root directory not found: {root}");
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(root, pattern, search))
            {
                if (!file.EndsWith(".etl", StringComparison.OrdinalIgnoreCase)) continue;
                string full = Path.GetFullPath(file);
                seen.TryAdd(full, full);
            }
        }
        else if (Directory.Exists(arg))
        {
            bool any = false;
            foreach (string file in Directory.EnumerateFiles(arg, "*.etl", SearchOption.TopDirectoryOnly))
            {
                string full = Path.GetFullPath(file);
                if (seen.TryAdd(full, full)) any = true;
            }

            if (!any)
                Console.Error.WriteLine($"[!] No .etl files found in directory: {arg}");
        }
        else if (File.Exists(arg))
        {
            if (!arg.EndsWith(".etl", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"[!] '{arg}' does not have a .etl extension; skipping.");
                continue;
            }

            string full = Path.GetFullPath(arg);
            seen.TryAdd(full, full);
        }
        else
        {
            Console.Error.WriteLine($"[!] Input path not found: {arg}");
        }
    }

    return [..seen.Values];
}

/// <summary>
///     Attempts to read the GUID and Age from a PDB file using <c>Smx.PDBSharp</c>.
///     Supports Big-format (modern) PDBs only. Returns <see langword="null" /> on any failure.
/// </summary>
static (Guid Guid, int Age)? TryReadPdbIdentity(string path)
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
static string? FindPdbInSearchPaths(
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
            if (ValidateGlobPattern(arg) is not null) continue;

            string root    = NormalizeGlobRoot(arg);
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
///     Pre-scans the given .etl files with <see cref="EtwUtil.EnumeratePdbReferences" />, then
///     resolves each <see cref="PdbMetaData" /> against (1) the local symbol cache,
///     (2) the <paramref name="searchPaths" /> directories, and (3) a remote symbol server.
///     Unresolved references are logged to stderr and are non-fatal.
/// </summary>
static async Task<List<DecodingContextType>> ResolveAutoSymbolsAsync(
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

    bool hasServer      = !string.IsNullOrWhiteSpace(symbolServerUrl);
    bool hasSearchPaths = searchPaths.Length > 0;

    foreach (PdbMetaData pdb in refs)
    {
        if (cancellationToken.IsCancellationRequested) break;

        string pdbFileName = Path.GetFileName(pdb.PdbName);

        // PdbMetaData.IndexPrefix uses '/' separators; convert for the local file system.
        string indexPath     = pdb.IndexPrefix.Replace('/', Path.DirectorySeparatorChar);
        string cacheFilePath = Path.GetFullPath(Path.Combine(symbolCacheDir, indexPath));

        // 1. Cache lookup
        if (File.Exists(cacheFilePath))
        {
            try
            {
                result.Add(new PdbFileDecodingContextType(cacheFilePath));
                Console.Error.WriteLine($"    [cache ] {pdbFileName}");
                fromCache++;
                continue;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"[!] Cached PDB '{cacheFilePath}' could not be loaded: {ex.Message}");
            }
        }

        // 2. Local search
        if (hasSearchPaths)
        {
            string? found = FindPdbInSearchPaths(searchPaths, pdbFileName, pdb.Guid, pdb.Age);
            if (found is not null)
            {
                try
                {
                    result.Add(new PdbFileDecodingContextType(found));
                    Console.Error.WriteLine($"    [local ] {pdbFileName} <- {found}");
                    fromLocal++;
                    continue;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(
                        $"[!] Local PDB '{found}' could not be loaded: {ex.Message}");
                }
            }
        }

        // 3. Symbol-server download
        if (hasServer)
        {
            // IndexPrefix already uses '/' so it is safe to embed in a URL directly.
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
                    Console.Error.WriteLine($"    [404   ] {pdbFileName}");
                }
                else
                {
                    response.EnsureSuccessStatusCode();

                    await using (FileStream tmp = new(tmpPath, FileMode.Create, FileAccess.Write,
                                     FileShare.None, 65536, useAsync: true))
                    {
                        await response.Content.CopyToAsync(tmp, cancellationToken)
                                      .ConfigureAwait(false);
                    }

                    File.Move(tmpPath, cacheFilePath, overwrite: true);

                    result.Add(new PdbFileDecodingContextType(cacheFilePath));
                    Console.Error.WriteLine($"    [server] {pdbFileName} <- {url}");
                    fromServer++;
                    continue;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"[!] Failed to download '{pdbFileName}': {ex.GetType().Name}: {ex.Message}");
            }
        }

        // 4. Unresolved (non-fatal – WPP events fall back to GUID=... placeholder)
        Console.Error.WriteLine(
            $"    [miss  ] {pdbFileName} (guid={pdb.Guid:D}, age={pdb.Age})");
        unresolved++;
    }

    Console.Error.WriteLine(
        $"[*] Auto-discovery complete: {result.Count}/{refs.Count} resolved " +
        $"({fromCache} cache, {fromLocal} local, {fromServer} downloaded, {unresolved} unresolved).");

    return result;
}

/// <summary>
///     Parses all <paramref name="etlFiles" /> as a single time-merged stream and writes
///     events to stdout as NDJSON or plain TSV.
/// </summary>
static async Task<int> RunParseStdoutAsync(
    List<string> etlFiles,
    DecodingContext? decodingContext,
    bool usePlain,
    bool useColor,
    bool preserveRawTimestamps,
    CancellationToken cancellationToken)
{
    using CancellationTokenSource cts =
        CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

    cts.Token.Register(() =>
        Console.Error.WriteLine("\r[*] Interrupt received — stopping..."));

    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        try { cts.Cancel(); } catch (ObjectDisposedException) { }
    };

    AppDomain.CurrentDomain.ProcessExit += (_, _) =>
    {
        try { cts.Cancel(); } catch (ObjectDisposedException) { }
    };

    Stream?      ndjsonOut = usePlain ? null : new BufferedStream(Console.OpenStandardOutput(), 65536);
    StreamWriter? plainOut = usePlain
        ? new StreamWriter(Console.OpenStandardOutput(), new System.Text.UTF8Encoding(false), 65536)
        : null;

    Console.Error.WriteLine($"[*] Streaming {etlFiles.Count} .etl file(s) to stdout...");

    try
    {
        await foreach (ReadOnlyMemory<byte> json in EtwUtil.EnumerateEventsAsync(
                           etlFiles,
                           o =>
                           {
                               o.WppDecodingContext    = decodingContext;
                               o.PreserveRawTimestamps = preserveRawTimestamps;
                           },
                           cts.Token))
        {
            if (cts.Token.IsCancellationRequested) break;

            if (usePlain)
            {
                await WritePlainLineAsync(json, plainOut!, useColor, cts.Token);
            }
            else
            {
                await ndjsonOut!.WriteAsync(json, cts.Token);
                ndjsonOut.WriteByte((byte)'\n');
                await ndjsonOut.FlushAsync(cts.Token);
            }
        }
    }
    catch (OperationCanceledException)
    {
        // Expected on Ctrl+C.
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[!] Fatal error: {ex.GetType().Name}: {ex.Message}");
        return 1;
    }
    finally
    {
        if (usePlain)
        {
            await plainOut!.FlushAsync(CancellationToken.None);
            await plainOut.DisposeAsync();
        }
        else
        {
            await ndjsonOut!.FlushAsync(CancellationToken.None);
            await ndjsonOut.DisposeAsync();
        }

        Console.Error.WriteLine("[*] Done.");
    }

    return 0;
}

/// <summary>
///     Decodes each .etl file in <paramref name="etlFiles" /> independently and writes the
///     output to an equally-named file in <paramref name="outDir" />, replacing the
///     <c>.etl</c> extension with <c>.ndjson</c> or <c>.tsv</c>.
/// </summary>
static async Task<int> RunParsePerFileAsync(
    List<string> etlFiles,
    string outDir,
    DecodingContext? decodingContext,
    bool usePlain,
    bool preserveRawTimestamps,
    CancellationToken cancellationToken)
{
    string ext = usePlain ? ".tsv" : ".ndjson";

    try
    {
        Directory.CreateDirectory(outDir);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(
            $"[!] Cannot create output directory '{outDir}': {ex.GetType().Name}: {ex.Message}");
        return 2;
    }

    // Detect output file name collisions before processing anything.
    Dictionary<string, string> outputMap = new(StringComparer.OrdinalIgnoreCase);
    bool hasCollision = false;

    foreach (string etl in etlFiles)
    {
        string outName = Path.GetFileNameWithoutExtension(etl) + ext;
        string outPath = Path.Combine(outDir, outName);

        if (outputMap.TryGetValue(outName, out string? previous))
        {
            Console.Error.WriteLine(
                $"[!] Output file name collision: '{previous}' and '{etl}' " +
                $"both map to '{outPath}'.");
            hasCollision = true;
        }
        else
        {
            outputMap[outName] = etl;
        }
    }

    if (hasCollision) return 2;

    bool anyFailed = false;

    foreach (string etl in etlFiles)
    {
        if (cancellationToken.IsCancellationRequested) break;

        string outName = Path.GetFileNameWithoutExtension(etl) + ext;
        string outPath = Path.Combine(outDir, outName);

        Console.Error.WriteLine($"[*] {Path.GetFileName(etl)} -> {outPath}");

        long eventCount = 0;
        try
        {
            await using FileStream fileStream = new(outPath, FileMode.Create, FileAccess.Write,
                FileShare.None, 65536, useAsync: true);

            if (usePlain)
            {
                await using StreamWriter writer =
                    new(fileStream, new System.Text.UTF8Encoding(false), 65536);

                await foreach (ReadOnlyMemory<byte> json in EtwUtil.EnumerateEventsAsync(
                                   [etl],
                                   o =>
                                   {
                                       o.WppDecodingContext    = decodingContext;
                                       o.PreserveRawTimestamps = preserveRawTimestamps;
                                   },
                                   cancellationToken))
                {
                    await WritePlainLineAsync(json, writer, false, cancellationToken, flush: false);
                    eventCount++;
                }

                await writer.FlushAsync(cancellationToken);
            }
            else
            {
                await using BufferedStream buffered = new(fileStream, 65536);

                await foreach (ReadOnlyMemory<byte> json in EtwUtil.EnumerateEventsAsync(
                                   [etl],
                                   o =>
                                   {
                                       o.WppDecodingContext    = decodingContext;
                                       o.PreserveRawTimestamps = preserveRawTimestamps;
                                   },
                                   cancellationToken))
                {
                    await buffered.WriteAsync(json, cancellationToken);
                    buffered.WriteByte((byte)'\n');
                    eventCount++;
                }

                await buffered.FlushAsync(cancellationToken);
            }

            Console.Error.WriteLine($"    {eventCount:N0} event(s) written.");
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("    [cancelled]");
            break;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[!] Failed to process '{Path.GetFileName(etl)}': " +
                $"{ex.GetType().Name}: {ex.Message}");
            anyFailed = true;
        }
    }

    Console.Error.WriteLine("[*] Done.");
    return anyFailed ? 1 : 0;
}

/// <summary>
///     Resolves whether ANSI colour should be active based on the <c>--color</c> option,
///     the <c>NO_COLOR</c> environment variable, and whether stdout is redirected.
/// </summary>
static bool ResolveColor(string colorOpt)
{
    if (colorOpt.Equals("always", StringComparison.OrdinalIgnoreCase)) return true;
    if (colorOpt.Equals("never", StringComparison.OrdinalIgnoreCase)) return false;

    // auto: honour NO_COLOR (https://no-color.org/) and piped stdout.
    string? noColor = Environment.GetEnvironmentVariable("NO_COLOR");
    if (!string.IsNullOrEmpty(noColor)) return false;
    if (Console.IsOutputRedirected) return false;
    return true;
}

/// <summary>
///     Enables ANSI/VT escape processing on Windows consoles (conhost) so that legacy
///     hosts render colour sequences correctly. No-op on non-Windows and on failure.
/// </summary>
static void TryEnableWindowsVt()
{
    if (!OperatingSystem.IsWindows()) return;

    try
    {
        IntPtr handle = NativeConsole.GetStdHandle(-11); // STD_OUTPUT_HANDLE
        if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return;
        if (!NativeConsole.GetConsoleMode(handle, out uint mode)) return;
        NativeConsole.SetConsoleMode(handle, mode | 0x0004u); // ENABLE_VIRTUAL_TERMINAL_PROCESSING
    }
    catch
    {
        // Non-fatal: modern terminals already have VT enabled.
    }
}

/// <summary>
///     Maps a WPP level name string to its ANSI SGR open/close pair.
///     Returns <c>(null, null)</c> when no colour should be applied.
/// </summary>
static (string? Open, string? Reset) LevelColor(string levelName)
{
    const string reset = "\x1b[0m";
    ReadOnlySpan<char> s = levelName.AsSpan();

    if (s.IndexOf("CRITICAL", StringComparison.OrdinalIgnoreCase) >= 0 ||
        s.IndexOf("FATAL", StringComparison.OrdinalIgnoreCase) >= 0)
        return ("\x1b[1;91m", reset);

    if (s.IndexOf("ERROR", StringComparison.OrdinalIgnoreCase) >= 0)
        return ("\x1b[31m", reset);

    if (s.IndexOf("WARN", StringComparison.OrdinalIgnoreCase) >= 0)
        return ("\x1b[33m", reset);

    if (s.IndexOf("INFO", StringComparison.OrdinalIgnoreCase) >= 0)
        return ("\x1b[36m", reset);

    if (s.IndexOf("VERBOSE", StringComparison.OrdinalIgnoreCase) >= 0)
        return ("\x1b[90m", reset);

    return (null, null);
}

/// <summary>
///     Parses one NDJSON event buffer and returns a tab-separated plain-text line.
///     Returns <see langword="null" /> on parse failure (caller should log to stderr).
/// </summary>
static string? FormatPlainLine(ReadOnlyMemory<byte> jsonBytes, bool useColor)
{
    JsonDocument doc;
    try
    {
        doc = JsonDocument.Parse(jsonBytes);
    }
    catch (JsonException)
    {
        return null;
    }

    using (doc)
    {
        JsonElement root = doc.RootElement;
        if (!root.TryGetProperty("Event", out JsonElement evt))
            return null;

        // --- Timestamp ---
        string timestamp = "-";
        if (evt.TryGetProperty("Timestamp", out JsonElement tsEl) &&
            tsEl.TryGetInt64(out long ticks))
        {
            try
            {
                timestamp = DateTime.FromFileTimeUtc(ticks)
                    .ToLocalTime()
                    .ToString("o", CultureInfo.InvariantCulture);
            }
            catch
            {
                timestamp = ticks.ToString(CultureInfo.InvariantCulture);
            }
        }

        // --- Detect WPP (Name == "WPP") ---
        bool isWpp = evt.TryGetProperty("Name", out JsonElement nameEl) &&
                     nameEl.GetString() == "WPP";

        string provider = "-";
        string levelName = "-";
        string message = "-";

        if (isWpp &&
            evt.TryGetProperty("Properties", out JsonElement propsArr) &&
            propsArr.ValueKind == JsonValueKind.Array &&
            propsArr.GetArrayLength() > 0)
        {
            JsonElement p = propsArr[0];

            if (p.TryGetProperty("GuidName", out JsonElement gn))
                provider = gn.GetString() ?? "-";

            if (p.TryGetProperty("LevelName", out JsonElement ln))
                levelName = ln.GetString() ?? "-";

            if (p.TryGetProperty("FormattedString", out JsonElement fs))
                message = fs.GetString() ?? "-";
        }
        else
        {
            // Non-WPP: derive provider from the Name field ("Provider/Task/Opcode") or ProviderGuid.
            if (evt.TryGetProperty("Name", out JsonElement evtName))
            {
                string? full = evtName.GetString();
                if (!string.IsNullOrEmpty(full))
                {
                    int slash = full.IndexOf('/');
                    provider = slash > 0 ? full[..slash] : full;
                }
            }

            if (provider == "-" && evt.TryGetProperty("ProviderGuid", out JsonElement pg))
                provider = pg.GetString() ?? "-";

            // Best-effort: serialize the Properties array as compact JSON.
            if (evt.TryGetProperty("Properties", out JsonElement rawProps))
                message = rawProps.GetRawText();
        }

        // Escape tabs and embedded newlines so each event stays on one line.
        message = message.Replace("\t", "\\t").Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\r", "\\n");

        // Optionally colourise the level column.
        string levelColumn;
        if (useColor && levelName != "-")
        {
            (string? open, string? reset) = LevelColor(levelName);
            levelColumn = open is not null
                ? $"{open}{levelName}{reset}"
                : levelName;
        }
        else
        {
            levelColumn = levelName;
        }

        return $"{timestamp}\t{provider}\t{levelColumn}\t{message}";
    }
}

/// <summary>
///     Formats one event as a plain TSV line and writes it to <paramref name="writer" />.
/// </summary>
static async Task WritePlainLineAsync(
    ReadOnlyMemory<byte> json,
    StreamWriter writer,
    bool useColor,
    CancellationToken cancellationToken,
    bool flush = true)
{
    string? line;
    try
    {
        line = FormatPlainLine(json, useColor);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[!] Plain format decode error: {ex.Message}");
        return;
    }

    if (line is null)
    {
        Console.Error.WriteLine("[!] Plain format decode error: could not parse event JSON.");
        return;
    }

    await writer.WriteLineAsync(line.AsMemory(), cancellationToken);
    if (flush)
        await writer.FlushAsync(cancellationToken);
}

// ---------------------------------------------------------------------------
// Native interop — Windows console VT processing
// ---------------------------------------------------------------------------

/// <summary>
///     Minimal P/Invoke surface for enabling ANSI/VT escape sequences on Windows.
/// </summary>
internal static class NativeConsole
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr GetStdHandle(int nStdHandle);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);
}
