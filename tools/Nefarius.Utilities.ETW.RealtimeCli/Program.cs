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

// ---------------------------------------------------------------------------
// 'realtime' subcommand
// ---------------------------------------------------------------------------
Command realtime = new(
    "realtime",
    "Attach to a realtime ETW session, enable a provider, and stream decoded events as NDJSON on stdout.")
{
    providerArg,
    keywordsOpt,
    matchAllOpt,
    levelOpt,
    sessionNameOpt,
    symbolsOpt,
    bufferSizeOpt,
    flushSecondsOpt
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
    bool useNdjson = result.GetValue(inspectFormatOpt)!
        .Equals("ndjson", StringComparison.OrdinalIgnoreCase);

    // Collect PDB contexts only — TMF sources never carry a control GUID.
    List<PdbFileDecodingContextType> pdbContexts = [];

    foreach (string arg in paths)
    {
        if (arg.Contains('*') || arg.Contains('?'))
        {
            string root = Path.GetDirectoryName(arg) ?? ".";
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
RootCommand root = new("realtimewpp — Nefarius ETW realtime decoder. Streams decoded events as NDJSON on stdout.")
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

    // Write a buffered stdout stream so high-frequency events aren't flushed
    // one byte at a time through the console interop layer.
    Stream stdout = new BufferedStream(Console.OpenStandardOutput(), 65536);

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

            // Each buffer is a self-contained JSON object; append a newline for NDJSON,
            // then flush immediately so events appear in realtime rather than waiting
            // for the buffer to fill.
            await stdout.WriteAsync(json, cts.Token);
            stdout.WriteByte((byte)'\n');
            await stdout.FlushAsync(cts.Token);
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
        await stdout.FlushAsync(CancellationToken.None);
        await stdout.DisposeAsync();
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
            string root = Path.GetDirectoryName(arg) ?? ".";
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
            // Directory: collect all PDB files recursively and treat the directory
            // itself as a TMF search path (TmfFilesDirectoryDecodingContextType parses
            // every *.tmf inside it).
            bool anyFound = false;

            foreach (string pdb in Directory.EnumerateFiles(arg, "*.pdb", SearchOption.TopDirectoryOnly))
            {
                if (AddSymbolFile(contexts, pdb))
                {
                    anyFound = true;
                }
            }

            // Only add the directory as a TMF source when it actually contains TMF files,
            // so we don't waste time on symbol-only directories.
            if (Directory.EnumerateFiles(arg, "*.tmf", SearchOption.TopDirectoryOnly).Any())
            {
                try
                {
                    contexts.Add(new TmfFilesDirectoryDecodingContextType(arg));
                    Console.Error.WriteLine($"    + {arg} (TMF directory)");
                    anyFound = true;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[!] Failed to load TMF directory '{arg}': {ex.Message}");
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
