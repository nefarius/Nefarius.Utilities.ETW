using System.CommandLine;
using System.Globalization;
using System.Net.Http;
using System.Reflection;

using Nefarius.Utilities.ETW.Deserializer.WPP;
using Nefarius.Utilities.ETW.Exceptions;

namespace Nefarius.Utilities.ETW.CLI;

/// <summary>
///     Builds and runs the <c>realtime</c> subcommand: attach to a realtime ETW session, enable a
///     provider, and stream decoded events on stdout.
/// </summary>
internal static class RealtimeCommand
{
    /// <summary>
    ///     Constructs the <c>realtime</c> <see cref="Command" />, wiring up its arguments, options,
    ///     and action handler.
    /// </summary>
    internal static Command Build()
    {
        // -----------------------------------------------------------------------
        // Arguments and options
        // -----------------------------------------------------------------------
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

        Option<string?> realtimeColumnsOpt = new("--columns")
        {
            Description =
                $"Comma-separated column tokens for plain output. Known tokens: {PlainOutput.KnownColumnNames}. " +
                "Default: Timestamp,Provider,Level,Message. Ignored when --format is not plain."
        };

        Option<bool> realtimeHeaderOpt = new("--header")
        {
            Description = "Emit a TSV header line as the first output line (plain mode only). Ignored when --format is not plain."
        };

        Option<string?> realtimeFilterOpt = new("--filter")
        {
            Description =
                "DynamicExpresso predicate; events for which the expression returns false are skipped (plain mode only). " +
                $"Available identifiers: {PlainOutput.KnownColumnNames}. " +
                "Example: Provider != \"Foo\" && Message.StartsWith(\"Bar\"). Ignored when --format is not plain."
        };

        Option<bool> realtimeKeepOriginalProviderOpt = new("--keep-original-provider")
        {
            Description =
                "Keep the raw WPP Provider/GuidName value emitted by the decoder instead of rewriting it to the " +
                "friendly control-GUID name (e.g. BthPS3TraceGuid) recovered from PDB TMC: annotations. " +
                "By default the rewrite is active when PDB symbols containing TMC: records are loaded; " +
                "it is a silent no-op when no such records are found."
        };

        Option<string[]> realtimeDriverOpt = new("--driver")
        {
            Description =
                "Driver service name (e.g. HidHide, BthPS3). Repeat for multiple drivers. " +
                "Each driver's binary is located via the registry ImagePath, its CodeView PDB reference is read, " +
                "the matching PDB is downloaded from the symbol server into the local cache, and all resolved " +
                "PDBs are used as implicit --symbols sources. Provider GUIDs are auto-derived from every " +
                "downloaded PDB unless provider-guid arguments are also supplied. " +
                "--driver-type applies to every named driver; --symbol-server and --symbol-cache are shared.",
            Arity = ArgumentArity.ZeroOrMore,
            AllowMultipleArgumentsPerToken = false
        };

        Option<string?> realtimeDriverTypeOpt = new("--driver-type")
        {
            Description =
                "Driver kind to target when resolving the service binary: 'kernel', 'umdf', or 'auto' (default). " +
                "'auto' prefers the kernel-mode candidate and falls back to UMDF when only a UMDF registration exists."
        };

        Option<string?> realtimeDriverBinaryOpt = new("--driver-binary")
        {
            Description =
                "Explicit path to the driver binary (.sys/.dll). " +
                "Overrides the ImagePath registry lookup performed by --driver. " +
                "Useful for UMDF drivers whose ImagePath is not stored under the standard services key. " +
                "Only valid when exactly one --driver is specified."
        };

        Option<string?> realtimeSymbolServerOpt = new("--symbol-server")
        {
            Description =
                "Microsoft-style symbol store root URL used to download the PDB referenced by --driver " +
                "(e.g. https://symbols.nefarius.at/download/symbols). " +
                "Falls back to _NT_SYMBOL_PATH when absent."
        };

        Option<string?> realtimeSymbolCacheOpt = new("--symbol-cache")
        {
            Description =
                "Local symstore-layout cache directory for PDBs downloaded via --driver. " +
                $"Defaults to %LOCALAPPDATA%\\Nefarius\\etwutils\\symcache. " +
                "Falls back to _NT_SYMBOL_PATH when this flag and --symbol-server are both absent."
        };

        // -----------------------------------------------------------------------
        // 'realtime' subcommand
        // -----------------------------------------------------------------------
        Command realtime = new(
            "realtime",
            "Attach to a realtime ETW session, enable a provider, and stream decoded events on stdout. " +
            "Pass --driver <service-name> to resolve the driver binary automatically, download its PDB " +
            "from a symbol server, and derive provider GUIDs without supplying any path by hand.")
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
            colorOpt,
            realtimeColumnsOpt,
            realtimeHeaderOpt,
            realtimeFilterOpt,
            realtimeKeepOriginalProviderOpt,
            realtimeDriverOpt,
            realtimeDriverTypeOpt,
            realtimeDriverBinaryOpt,
            realtimeSymbolServerOpt,
            realtimeSymbolCacheOpt
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
            string? columnsRaw = result.GetValue(realtimeColumnsOpt);
            bool emitHeader = result.GetValue(realtimeHeaderOpt);
            string? filterExpr = result.GetValue(realtimeFilterOpt);
            bool keepOriginalProvider = result.GetValue(realtimeKeepOriginalProviderOpt);
            string[] driverNames = result.GetValue(realtimeDriverOpt) ?? [];
            string? driverTypeRaw = result.GetValue(realtimeDriverTypeOpt);
            string? driverBinaryOverride = result.GetValue(realtimeDriverBinaryOpt);
            string? symbolServerRaw = result.GetValue(realtimeSymbolServerOpt);
            string? symbolCacheRaw = result.GetValue(realtimeSymbolCacheOpt);

            // --- Validate --driver-type ---
            ServiceKind? driverKind = null;
            if (driverTypeRaw is not null)
            {
                if (driverTypeRaw.Equals("kernel", StringComparison.OrdinalIgnoreCase))
                    driverKind = ServiceKind.Kernel;
                else if (driverTypeRaw.Equals("umdf", StringComparison.OrdinalIgnoreCase))
                    driverKind = ServiceKind.Umdf;
                else if (!driverTypeRaw.Equals("auto", StringComparison.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine(
                        $"[!] Unknown --driver-type value '{driverTypeRaw}'. Expected 'kernel', 'umdf', or 'auto'.");
                    return 2;
                }
            }

            // --driver-binary requires exactly one --driver.
            if (driverBinaryOverride is not null && driverNames.Length == 0)
            {
                Console.Error.WriteLine(
                    "[*] Warning: --driver-binary has no effect without --driver.");
            }
            if (driverBinaryOverride is not null && driverNames.Length > 1)
            {
                Console.Error.WriteLine(
                    "[!] --driver-binary can only be combined with a single --driver. " +
                    $"Got {driverNames.Length} --driver values.");
                return 2;
            }

            // --- Validate --symbol-server ---
            if (symbolServerRaw is not null)
            {
                if (!Uri.TryCreate(symbolServerRaw, UriKind.Absolute, out Uri? parsedUri) ||
                    (parsedUri.Scheme != Uri.UriSchemeHttp && parsedUri.Scheme != Uri.UriSchemeHttps))
                {
                    Console.Error.WriteLine(
                        $"[!] --symbol-server must be an absolute http(s) URL. Got: '{symbolServerRaw}'");
                    return 2;
                }
            }

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
            bool useColor = usePlain && ConsoleOutput.ResolveColor(colorRaw);

            // Warn when plain-only flags are supplied alongside --format ndjson.
            if (!usePlain && (columnsRaw is not null || emitHeader || filterExpr is not null))
            {
                Console.Error.WriteLine(
                    "[*] Warning: --columns, --header, and --filter are only active with --format plain; " +
                    "they are ignored for ndjson output.");
            }

            // Resolve column list.
            IReadOnlyList<PlainOutput.ColumnSpec> columns = PlainOutput.DefaultColumns;
            if (usePlain && columnsRaw is not null)
            {
                IReadOnlyList<PlainOutput.ColumnSpec>? parsed = PlainOutput.ParseColumns(columnsRaw, out string? colError);
                if (parsed is null)
                {
                    Console.Error.WriteLine(colError);
                    return 2;
                }
                columns = parsed;
            }

            // Compile filter predicate.
            Func<PlainOutput.PlainEvent, bool>? filter = null;
            if (usePlain && filterExpr is not null)
            {
                filter = PlainOutput.CompileFilter(filterExpr, out string? filterError);
                if (filter is null)
                {
                    Console.Error.WriteLine(filterError);
                    return 2;
                }
            }

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
                columns,
                emitHeader,
                filter,
                keepOriginalProvider,
                driverNames,
                driverKind,
                driverBinaryOverride,
                symbolServerRaw,
                symbolCacheRaw,
                cancellationToken);
        });

        return realtime;
    }

    private static ulong ParseKeywordMask(string raw)
    {
        ReadOnlySpan<char> s = raw.AsSpan().Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return ulong.Parse(s[2..], NumberStyles.HexNumber);
        }

        return ulong.Parse(s, NumberStyles.None);
    }

    private static async Task<int> RunAsync(
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
        IReadOnlyList<PlainOutput.ColumnSpec> columns,
        bool emitHeader,
        Func<PlainOutput.PlainEvent, bool>? filter,
        bool keepOriginalProvider,
        string[] driverNames,
        ServiceKind? driverKind,
        string? driverBinaryOverride,
        string? symbolServerCliValue,
        string? symbolCacheCliValue,
        CancellationToken cancellationToken)
    {
        // ---------------------------------------------------------------------------
        // --driver: for each named driver, resolve binary → read PDB identity → download/cache PDB
        // ---------------------------------------------------------------------------
        if (driverNames.Length > 0)
        {
            (string? effectiveServer, string effectiveCache) =
                SymbolResolution.ResolveSymbolStoreConfig(symbolServerCliValue, symbolCacheCliValue);

            string assemblyVersion =
                Assembly.GetExecutingAssembly()
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion ?? "0.0.0";

            using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"etwutils/{assemblyVersion}");

            Directory.CreateDirectory(effectiveCache);

            List<string> driverPdbs = [];

            foreach (string driverName in driverNames)
            {
                string tag = $"--driver '{driverName}'";

                try
                {
                    string? binaryPath = driverBinaryOverride is not null
                        ? SymbolResolution.ResolveDriverBinaryOverride(driverBinaryOverride)
                        : SymbolResolution.ResolveDriverBinaryPath(driverName, driverKind);

                    if (binaryPath is null)
                    {
                        Console.Error.WriteLine(
                            $"[*] {tag}: binary path could not be resolved; skipping this driver.");
                        continue;
                    }

                    Console.Error.WriteLine($"[*] {tag}: resolved binary: {binaryPath}");

                    PdbMetaData? pdbMeta = SymbolResolution.TryReadPdbReference(binaryPath);
                    if (pdbMeta is null)
                    {
                        Console.Error.WriteLine(
                            $"[*] {tag}: no PDB reference found in binary; skipping this driver.");
                        continue;
                    }

                    Console.Error.WriteLine(
                        $"[*] {tag}: PDB reference: {Path.GetFileName(pdbMeta.Value.PdbName)} " +
                        $"(guid={pdbMeta.Value.Guid:D}, age={pdbMeta.Value.Age})");

                    string? resolvedPdb = await SymbolResolution.ResolveSinglePdbAsync(
                        pdbMeta.Value, [], effectiveServer, effectiveCache, http, cancellationToken);

                    if (resolvedPdb is not null)
                    {
                        driverPdbs.Add(resolvedPdb);
                    }
                    else
                    {
                        Console.Error.WriteLine(
                            $"[*] {tag}: PDB could not be resolved; skipping this driver.");
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(
                        $"[!] {tag}: unexpected error during driver resolution: " +
                        $"{ex.GetType().Name}: {ex.Message}");
                }
            }

            if (driverPdbs.Count > 0)
            {
                symbolPaths = [..symbolPaths, ..driverPdbs];
            }
            else
            {
                // All driver resolutions failed — fatal only when nothing else can supply providers.
                if (explicitProviderGuids.Length == 0 && symbolPaths.Length == 0)
                {
                    Console.Error.WriteLine(
                        "[!] --driver: no PDBs could be resolved for any of the specified driver(s) " +
                        "and no explicit provider GUIDs or --symbols paths were supplied. " +
                        "Cannot determine ETW provider(s) to enable.");
                    return 2;
                }

                Console.Error.WriteLine(
                    "[*] --driver: no PDBs resolved; continuing with existing symbol/provider sources.");
            }
        }

        // Resolve --symbols paths into a DecodingContext and the raw context list.
        (DecodingContext? decodingContext, List<DecodingContextType> contextTypes) = SymbolResolution.ResolveSymbols(symbolPaths);

        bool rewriteProviderName = SymbolResolution.ShouldRewriteProviderName(contextTypes, keepOriginalProvider);

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
            ConsoleOutput.TryEnableWindowsVt();
        }

        Action<Guid, ushort, uint> missingFormatWarner = ConsoleOutput.CreateMissingFormatWarner();

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

            if (usePlain && emitHeader)
            {
                await plainOut!.WriteLineAsync(PlainOutput.FormatHeader(columns).AsMemory(), cts.Token);
                await plainOut.FlushAsync(cts.Token);
            }

            await foreach (ReadOnlyMemory<byte> json in EtwUtil.EnumerateRealtimeEventsAsync(
                               sessionName,
                               o =>
                               {
                                   o.WppDecodingContext      = decodingContext;
                                   o.OnWppFormatMissing      = missingFormatWarner;
                                   o.RewriteWppProviderName  = rewriteProviderName;
                               },
                               cts.Token))
            {
                // Stop emitting immediately once cancellation is requested so no
                // buffered events leak out after Ctrl+C.
                if (cts.Token.IsCancellationRequested) break;

                if (usePlain)
                {
                    await ConsoleOutput.WritePlainLineAsync(json, plainOut!, useColor, columns, filter, cts.Token);
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

            // ERROR_NO_SYSTEM_RESOURCES (0x5AA) means the provider is already enabled in the
            // maximum number of concurrent sessions, usually due to orphaned NefariusEtwCli-<pid>
            // sessions from previous killed runs.
            if (ex is EtwEnableTraceException { NativeErrorCode: 0x5AA })
            {
                Console.Error.WriteLine(
                    "[!] Tip: leftover sessions from previous killed runs may be holding this provider. " +
                    "Run 'etwutils sessions list' to inspect and 'etwutils sessions clean' to remove dead sessions.");
            }

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
}
