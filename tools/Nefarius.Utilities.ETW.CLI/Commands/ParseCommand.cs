using System.CommandLine;
using System.Net.Http;
using System.Reflection;

using Nefarius.Utilities.ETW.Deserializer.WPP;

namespace Nefarius.Utilities.ETW.CLI;

/// <summary>
///     Builds and runs the <c>parse</c> subcommand: decode offline .etl files and emit events as
///     NDJSON or plain TSV to stdout or to per-file output in a target directory.
/// </summary>
internal static class ParseCommand
{
    /// <summary>
    ///     Constructs the <c>parse</c> <see cref="Command" />, wiring up its arguments, options,
    ///     and action handler.
    /// </summary>
    internal static Command Build()
    {
        // -----------------------------------------------------------------------
        // 'parse' subcommand – arguments and options
        // -----------------------------------------------------------------------
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

        Option<string?> parseColumnsOpt = new("--columns")
        {
            Description =
                $"Comma-separated column tokens for plain output. Known tokens: {PlainOutput.KnownColumnNames}. " +
                "Default: Timestamp,Provider,Level,Message. Ignored when --format is not plain."
        };

        Option<bool> parseHeaderOpt = new("--header")
        {
            Description = "Emit a TSV header line as the first output line (plain mode only). Ignored when --format is not plain."
        };

        Option<string?> parseFilterOpt = new("--filter")
        {
            Description =
                "DynamicExpresso predicate; events for which the expression returns false are skipped (plain mode only). " +
                $"Available identifiers: {PlainOutput.KnownColumnNames}. " +
                "Example: Provider != \"Foo\" && Message.StartsWith(\"Bar\"). Ignored when --format is not plain."
        };

        Option<bool> parseKeepOriginalProviderOpt = new("--keep-original-provider")
        {
            Description =
                "Keep the raw WPP Provider/GuidName value emitted by the decoder instead of rewriting it to the " +
                "friendly control-GUID name (e.g. BthPS3TraceGuid) recovered from PDB TMC: annotations. " +
                "By default the rewrite is active when PDB symbols containing TMC: records are loaded; " +
                "it is a silent no-op when no such records are found."
        };

        // -----------------------------------------------------------------------
        // 'parse' subcommand
        // -----------------------------------------------------------------------
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
            parsePreserveRawTimestampsOpt,
            parseColumnsOpt,
            parseHeaderOpt,
            parseFilterOpt,
            parseKeepOriginalProviderOpt
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
            string?  columnsRaw        = result.GetValue(parseColumnsOpt);
            bool     emitHeader        = result.GetValue(parseHeaderOpt);
            string?  filterExpr        = result.GetValue(parseFilterOpt);
            bool     keepOriginalProvider = result.GetValue(parseKeepOriginalProviderOpt);

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
            bool useColor = usePlain && outDir is null && ConsoleOutput.ResolveColor(colorRaw);

            if (useColor)
            {
                ConsoleOutput.TryEnableWindowsVt();
            }

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
                IReadOnlyList<PlainOutput.ColumnSpec>? parsedCols = PlainOutput.ParseColumns(columnsRaw, out string? colError);
                if (parsedCols is null)
                {
                    Console.Error.WriteLine(colError);
                    return 2;
                }
                columns = parsedCols;
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

            // --- Resolve ETL inputs ---
            List<string> etlFiles = PathGlobbing.ResolveEtlInputs(etlPathArgs);
            if (etlFiles.Count == 0)
            {
                Console.Error.WriteLine("[!] No .etl files found for the given inputs.");
                return 1;
            }

            Console.Error.WriteLine($"[*] Resolved {etlFiles.Count} .etl file(s).");

            // --- Resolve explicit --symbols (unconditional, same as 'realtime') ---
            (_, List<DecodingContextType> explicitTypes) = SymbolResolution.ResolveSymbols(symbolPaths);

            // --- Determine effective symbol-store config (CLI flags → _NT_SYMBOL_PATH → defaults) ---
            (string? effectiveServer, string effectiveCache) = SymbolResolution.ResolveSymbolStoreConfig(symbolServer, symbolCache);

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

                autoTypes = await SymbolResolution.ResolveAutoSymbolsAsync(
                    etlFiles, symbolSearchPaths, effectiveServer, effectiveCache, http, cancellationToken);
            }

            // Merge explicit + auto into one DecodingContext.
            List<DecodingContextType> allTypes = [..explicitTypes, ..autoTypes];
            DecodingContext? decodingContext = allTypes.Count > 0 ? new DecodingContext(allTypes) : null;

            bool rewriteProviderName = SymbolResolution.ShouldRewriteProviderName(allTypes, keepOriginalProvider);

            if (outDir is not null)
            {
                return await RunParsePerFileAsync(
                    etlFiles, outDir, decodingContext, usePlain, preserveRaw,
                    columns, emitHeader, filter, rewriteProviderName, cancellationToken);
            }

            return await RunParseStdoutAsync(
                etlFiles, decodingContext, usePlain, useColor, preserveRaw,
                columns, emitHeader, filter, rewriteProviderName, cancellationToken);
        });

        return parse;
    }

    /// <summary>
    ///     Parses all <paramref name="etlFiles" /> as a single time-merged stream and writes
    ///     events to stdout as NDJSON or plain TSV.
    /// </summary>
    private static async Task<int> RunParseStdoutAsync(
        List<string> etlFiles,
        DecodingContext? decodingContext,
        bool usePlain,
        bool useColor,
        bool preserveRawTimestamps,
        IReadOnlyList<PlainOutput.ColumnSpec> columns,
        bool emitHeader,
        Func<PlainOutput.PlainEvent, bool>? filter,
        bool rewriteProviderName,
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

        Action<Guid, ushort, uint> missingFormatWarner = ConsoleOutput.CreateMissingFormatWarner();

        Console.Error.WriteLine($"[*] Streaming {etlFiles.Count} .etl file(s) to stdout...");

        try
        {
            if (usePlain && emitHeader)
            {
                await plainOut!.WriteLineAsync(PlainOutput.FormatHeader(columns).AsMemory(), cts.Token);
                await plainOut.FlushAsync(cts.Token);
            }

            await foreach (ReadOnlyMemory<byte> json in EtwUtil.EnumerateEventsAsync(
                               etlFiles,
                               o =>
                               {
                                   o.WppDecodingContext     = decodingContext;
                                   o.PreserveRawTimestamps  = preserveRawTimestamps;
                                   o.OnWppFormatMissing     = missingFormatWarner;
                                   o.RewriteWppProviderName = rewriteProviderName;
                               },
                               cts.Token))
            {
                if (cts.Token.IsCancellationRequested) break;

                if (usePlain)
                {
                    await ConsoleOutput.WritePlainLineAsync(json, plainOut!, useColor, columns, filter, cts.Token);
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
    private static async Task<int> RunParsePerFileAsync(
        List<string> etlFiles,
        string outDir,
        DecodingContext? decodingContext,
        bool usePlain,
        bool preserveRawTimestamps,
        IReadOnlyList<PlainOutput.ColumnSpec> columns,
        bool emitHeader,
        Func<PlainOutput.PlainEvent, bool>? filter,
        bool rewriteProviderName,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource cts =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        cts.Token.Register(() =>
            Console.Error.WriteLine("\r[*] Interrupt received — stopping..."));

        ConsoleCancelEventHandler cancelKeyHandler = (_, e) =>
        {
            e.Cancel = true;
            try { cts.Cancel(); } catch (ObjectDisposedException) { }
        };

        EventHandler processExitHandler = (_, _) =>
        {
            try { cts.Cancel(); } catch (ObjectDisposedException) { }
        };

        Console.CancelKeyPress += cancelKeyHandler;
        AppDomain.CurrentDomain.ProcessExit += processExitHandler;

        try
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

            // One warner per command run so each provider GUID is reported at most once across all ETL files.
            Action<Guid, ushort, uint> missingFormatWarner = ConsoleOutput.CreateMissingFormatWarner();

            foreach (string etl in etlFiles)
            {
                if (cts.Token.IsCancellationRequested) break;

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

                        if (emitHeader)
                        {
                            await writer.WriteLineAsync(PlainOutput.FormatHeader(columns).AsMemory(), cts.Token);
                        }

                        await foreach (ReadOnlyMemory<byte> json in EtwUtil.EnumerateEventsAsync(
                                           [etl],
                                           o =>
                                           {
                                               o.WppDecodingContext     = decodingContext;
                                               o.PreserveRawTimestamps  = preserveRawTimestamps;
                                               o.OnWppFormatMissing     = missingFormatWarner;
                                               o.RewriteWppProviderName = rewriteProviderName;
                                           },
                                           cts.Token))
                        {
                            bool written = await ConsoleOutput.WritePlainLineAsync(
                                json, writer, false, columns, filter, cts.Token, flush: false);
                            if (written) eventCount++;
                        }

                        await writer.FlushAsync(cts.Token);
                    }
                    else
                    {
                        await using BufferedStream buffered = new(fileStream, 65536);

                        await foreach (ReadOnlyMemory<byte> json in EtwUtil.EnumerateEventsAsync(
                                           [etl],
                                           o =>
                                           {
                                               o.WppDecodingContext     = decodingContext;
                                               o.PreserveRawTimestamps  = preserveRawTimestamps;
                                               o.OnWppFormatMissing     = missingFormatWarner;
                                               o.RewriteWppProviderName = rewriteProviderName;
                                           },
                                           cts.Token))
                        {
                            await buffered.WriteAsync(json, cts.Token);
                            buffered.WriteByte((byte)'\n');
                            eventCount++;
                        }

                        await buffered.FlushAsync(cts.Token);
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
        finally
        {
            Console.CancelKeyPress -= cancelKeyHandler;
            AppDomain.CurrentDomain.ProcessExit -= processExitHandler;
        }
    }
}
