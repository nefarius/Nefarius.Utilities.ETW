using Nefarius.Utilities.ETW.Symbols;

namespace Nefarius.Utilities.ETW.CLI;

/// <summary>
///     Helpers for validating and expanding glob patterns, glob roots, and the raw
///     <c>etl-path</c> argument values shared across the CLI subcommands.
/// </summary>
internal static class PathGlobbing
{
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
    ///     <see cref="Directory.EnumerateFiles(string, string, SearchOption)" />.
    /// </remarks>
    internal static string? ValidateGlobPattern(string globArg) => SymbolPathPatterns.Validate(globArg);

    /// <summary>
    ///     Returns the last path-directory segment of <paramref name="globArg" /> that does not
    ///     contain any wildcard characters (<c>*</c> or <c>?</c>).
    /// </summary>
    internal static string NormalizeGlobRoot(string globArg) => SymbolPathPatterns.NormalizeRoot(globArg);

    /// <summary>
    ///     Expands the raw <c>etl-path</c> argument values into a deduplicated, ordered list of
    ///     absolute .etl file paths. Accepts files, directories (top-level <c>*.etl</c>), and
    ///     glob patterns.
    /// </summary>
    internal static List<string> ResolveEtlInputs(string[] rawArgs)
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
}
