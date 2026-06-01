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
    internal static string? ValidateGlobPattern(string globArg)
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
    internal static string NormalizeGlobRoot(string globArg)
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
