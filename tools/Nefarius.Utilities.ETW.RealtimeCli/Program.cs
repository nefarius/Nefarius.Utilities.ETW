using System.CommandLine;
using System.Globalization;

using Nefarius.Utilities.ETW;
using Nefarius.Utilities.ETW.Deserializer.WPP;

// ---------------------------------------------------------------------------
// Arguments and options
// ---------------------------------------------------------------------------
Argument<Guid> providerArg = new("provider-guid")
{
    Description = "GUID of the ETW provider to enable (e.g. {12345678-...})."
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
    Guid provider = result.GetValue(providerArg);
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
    catch (FormatException)
    {
        Console.Error.WriteLine(
            $"[!] Invalid keyword value. Use decimal (255) or hex (0xFF). " +
            $"Got: keywords='{keywordsRaw}' match-all='{matchAllRaw}'");
        return 2;
    }

    return await RunAsync(
        provider,
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
// Root command
// ---------------------------------------------------------------------------
RootCommand root = new("realtimewpp — Nefarius ETW realtime decoder. Streams decoded events as NDJSON on stdout.")
{
    realtime
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
    Guid providerGuid,
    ulong matchAnyKeyword,
    ulong matchAllKeyword,
    TraceEventLevel level,
    string sessionName,
    string[] symbolPaths,
    uint bufferSizeKb,
    uint flushSeconds,
    CancellationToken cancellationToken)
{
    // Resolve --symbols paths into a DecodingContext (null when no symbols supplied).
    DecodingContext? decodingContext = ResolveSymbols(symbolPaths);

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

        session.EnableProvider(providerGuid, level, matchAnyKeyword, matchAllKeyword);

        Console.Error.WriteLine(
            $"[*] Session '{sessionName}' started. " +
            $"Provider {{{providerGuid}}} | level={level} | " +
            $"keywords=0x{matchAnyKeyword:X} | matchAll=0x{matchAllKeyword:X}");
        Console.Error.WriteLine("[*] Streaming events... (Ctrl+C to stop)");

        await foreach (ReadOnlyMemory<byte> json in EtwUtil.EnumerateRealtimeEventsAsync(
                           sessionName,
                           o => o.WppDecodingContext = decodingContext,
                           cts.Token))
        {
            // Stop emitting immediately once cancellation is requested so no
            // buffered events leak out after Ctrl+C.
            if (cts.Token.IsCancellationRequested) break;

            // Each buffer is a self-contained JSON object; append a newline for NDJSON.
            await stdout.WriteAsync(json, cts.Token);
            stdout.WriteByte((byte)'\n');
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
///     Converts the raw <c>--symbols</c> arguments into a <see cref="DecodingContext" />.
///     Returns <see langword="null" /> when no symbol paths were supplied, which disables
///     WPP message formatting (raw events are still decoded via TDH/manifest).
/// </summary>
static DecodingContext? ResolveSymbols(string[] symbolPaths)
{
    if (symbolPaths.Length == 0)
    {
        return null;
    }

    List<DecodingContextType> contexts = [];

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
        return null;
    }

    Console.Error.WriteLine($"[*] Loaded {contexts.Count} symbol source(s) for WPP decoding.");
    return new DecodingContext(contexts);
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
