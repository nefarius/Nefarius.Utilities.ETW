using System.CommandLine;
using System.Globalization;
using System.Text.Json;

using Nefarius.Utilities.ETW;
using Nefarius.Utilities.ETW.Deserializer.WPP;
using Nefarius.Utilities.ETW.Deserializer.WPP.TMF;

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
// Root command
// ---------------------------------------------------------------------------
RootCommand root = new("etwutils — Nefarius ETW realtime decoder. Streams decoded events as NDJSON on stdout.")
{
    realtime,
    inspectPdb
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
    CancellationToken cancellationToken)
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
