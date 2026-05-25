using System.CommandLine;
using System.Globalization;

using Nefarius.Utilities.ETW;
using Nefarius.Utilities.ETW.Deserializer.WPP;

// ---------------------------------------------------------------------------
// Root command
// ---------------------------------------------------------------------------
RootCommand root = new("Nefarius ETW realtime decoder — streams decoded events as NDJSON on stdout.");

// ---------------------------------------------------------------------------
// 'realtime' subcommand
// ---------------------------------------------------------------------------
Command realtime = new(
    "realtime",
    "Attach to a realtime ETW session, enable a provider, and stream decoded events as NDJSON on stdout.");

Argument<Guid> providerArg = new(
    "provider-guid",
    "GUID of the ETW provider to enable (e.g. {12345678-...}).");

Option<string> keywordsOpt = new(
    "--keywords",
    () => "0xFFFFFFFFFFFFFFFF",
    "Match-any keyword mask. Accepts hex (0xABCD) or decimal. Default: all keywords.");

Option<string> matchAllOpt = new(
    "--match-all-keywords",
    () => "0",
    "Match-all keyword mask. Accepts hex (0xABCD) or decimal. Default: disabled (0).");

Option<TraceEventLevel> levelOpt = new(
    "--level",
    () => TraceEventLevel.Verbose,
    "Maximum event severity level to capture.");

Option<string> sessionNameOpt = new(
    "--session-name",
    () => $"NefariusEtwCli-{Environment.ProcessId}",
    "ETW session name. Defaults to NefariusEtwCli-<pid>.");

Option<string[]> symbolsOpt = new(
    "--symbols",
    "Path to a PDB file, TMF file, directory (searched recursively), or a glob pattern (e.g. " +
    "C:\\Symbols\\*.pdb). Repeat the flag for multiple paths.")
{
    Arity = ArgumentArity.ZeroOrMore,
    AllowMultipleArgumentsPerToken = false
};

Option<uint> bufferSizeOpt = new(
    "--buffer-size-kb",
    () => 64u,
    "ETW buffer size in kilobytes per buffer.");

Option<uint> flushSecondsOpt = new(
    "--flush-seconds",
    () => 1u,
    "How often (in seconds) the session flushes in-flight buffers.");

realtime.Add(providerArg);
realtime.Add(keywordsOpt);
realtime.Add(matchAllOpt);
realtime.Add(levelOpt);
realtime.Add(sessionNameOpt);
realtime.Add(symbolsOpt);
realtime.Add(bufferSizeOpt);
realtime.Add(flushSecondsOpt);

realtime.SetHandler(async ctx =>
{
    Guid provider = ctx.ParseResult.GetValue(providerArg);
    string keywordsRaw = ctx.ParseResult.GetValue(keywordsOpt)!;
    string matchAllRaw = ctx.ParseResult.GetValue(matchAllOpt)!;
    TraceEventLevel level = ctx.ParseResult.GetValue(levelOpt);
    string sessionName = ctx.ParseResult.GetValue(sessionNameOpt)!;
    string[] symbolPaths = ctx.ParseResult.GetValue(symbolsOpt) ?? [];
    uint bufferSizeKb = ctx.ParseResult.GetValue(bufferSizeOpt);
    uint flushSeconds = ctx.ParseResult.GetValue(flushSecondsOpt);

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
        ctx.ExitCode = 2;
        return;
    }

    ctx.ExitCode = await RunAsync(
        provider,
        matchAny,
        matchAll,
        level,
        sessionName,
        symbolPaths,
        bufferSizeKb,
        flushSeconds,
        ctx.GetCancellationToken());
});

root.Add(realtime);

return await root.InvokeAsync(args);

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

    // Ctrl+C: cancel gracefully without immediately terminating the process.
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        Console.Error.WriteLine("\r[*] Interrupt received — stopping session...");
        cts.Cancel();
    };

    // Process-exit: ensure the ETW session is torn down even when the process
    // exits via Environment.Exit() or an unhandled exception finaliser.
    AppDomain.CurrentDomain.ProcessExit += (_, _) => cts.Cancel();

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
        try
        {
            if (arg.Contains('*') || arg.Contains('?'))
            {
                // Glob pattern: split into directory root + file pattern.
                string root = Path.GetDirectoryName(arg) ?? ".";
                string pattern = Path.GetFileName(arg);

                if (!Directory.Exists(root))
                {
                    Console.Error.WriteLine($"[!] Glob root directory not found: {root}");
                    continue;
                }

                foreach (string file in Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories))
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

                foreach (string pdb in Directory.EnumerateFiles(arg, "*.pdb", SearchOption.AllDirectories))
                {
                    contexts.Add(new PdbFileDecodingContextType(pdb));
                    anyFound = true;
                }

                // Only add the directory as a TMF source when it actually contains TMF files,
                // so we don't waste time on symbol-only directories.
                if (Directory.EnumerateFiles(arg, "*.tmf", SearchOption.AllDirectories).Any())
                {
                    contexts.Add(new TmfFilesDirectoryDecodingContextType(arg));
                    anyFound = true;
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
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[!] Failed to load symbols from '{arg}': {ex.Message}");
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
/// </summary>
static void AddSymbolFile(List<DecodingContextType> contexts, string path)
{
    string ext = Path.GetExtension(path);

    if (ext.Equals(".pdb", StringComparison.OrdinalIgnoreCase))
    {
        contexts.Add(new PdbFileDecodingContextType(path));
    }
    else if (ext.Equals(".tmf", StringComparison.OrdinalIgnoreCase))
    {
        contexts.Add(new TmfFileDecodingContextType(path));
    }
    else
    {
        Console.Error.WriteLine($"[!] Unrecognized symbol file type (expected .pdb or .tmf): {path}");
    }
}
