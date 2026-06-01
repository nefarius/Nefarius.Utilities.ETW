namespace Nefarius.Utilities.ETW.CLI;

/// <summary>
///     Console-output helpers shared by the streaming subcommands: colour resolution, Windows VT
///     enablement, plain-TSV line writing, and the one-time missing-WPP-format warner.
/// </summary>
internal static class ConsoleOutput
{
    /// <summary>
    ///     Resolves whether ANSI colour should be active based on the <c>--color</c> option,
    ///     the <c>NO_COLOR</c> environment variable, and whether stdout is redirected.
    /// </summary>
    internal static bool ResolveColor(string colorOpt)
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
    ///     Returns a callback suitable for <see cref="EtwJsonConverterOptions.OnWppFormatMissing" /> that
    ///     prints a single <c>[!]</c> warning to <c>stderr</c> the first time each provider GUID is seen
    ///     without format information.  The deduplication is per-instance so callers should create one
    ///     warner per command invocation and share it across all option-lambdas in that run.
    /// </summary>
    internal static Action<Guid, ushort, uint> CreateMissingFormatWarner()
    {
        HashSet<Guid> warned = [];
        return (guid, _, _) =>
        {
            lock (warned)
            {
                if (!warned.Add(guid)) return;
                Console.Error.WriteLine(
                    $"[!] No WPP format information found for provider {{{guid}}}. " +
                    "This usually means the loaded PDB does not match the binary that produced the trace " +
                    "(wrong PDB age/GUID). Affected events will appear as " +
                    "'GUID=... - No format information found.' and may not match --filter expressions " +
                    "targeting Message, FunctionName, Function, or Level.");
            }
        };
    }

    /// <summary>
    ///     Enables ANSI/VT escape processing on Windows consoles (conhost) so that legacy
    ///     hosts render colour sequences correctly. No-op on non-Windows and on failure.
    /// </summary>
    internal static void TryEnableWindowsVt()
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
    ///     Decodes one NDJSON event buffer, optionally filters it, formats it as a plain TSV line,
    ///     and writes it to <paramref name="writer" />.
    ///     Returns <see langword="true" /> when the event was written, <see langword="false" /> when
    ///     it was dropped (filtered out, decode failure, or filter evaluation error).
    /// </summary>
    internal static async Task<bool> WritePlainLineAsync(
        ReadOnlyMemory<byte> json,
        StreamWriter writer,
        bool useColor,
        IReadOnlyList<PlainOutput.ColumnSpec> columns,
        Func<PlainOutput.PlainEvent, bool>? filter,
        CancellationToken cancellationToken,
        bool flush = true)
    {
        // Decode failures and filter evaluation exceptions are propagated as fatal errors so the
        // caller's outer try-catch logs them and returns exit code 1. A filter returning false
        // is a normal, non-fatal drop and still returns false here.
        PlainOutput.PlainEvent evt = PlainOutput.Decode(json)
            ?? throw new InvalidOperationException("Could not parse event JSON.");

        if (filter is not null && !filter(evt))
            return false;

        string line = PlainOutput.FormatLine(evt, columns, useColor);
        await writer.WriteLineAsync(line.AsMemory(), cancellationToken);
        if (flush)
            await writer.FlushAsync(cancellationToken);
        return true;
    }
}
