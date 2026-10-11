namespace Nefarius.Utilities.ETW.Symbols;

/// <summary>
///     Validates and expands filename globs used when discovering PDB and TMF inputs.
/// </summary>
public static class SymbolPathPatterns
{
    /// <summary>
    ///     Validates that <paramref name="glob" /> only uses a filename wildcard, optionally preceded by a
    ///     standalone <c>**</c> recursion marker.
    /// </summary>
    /// <returns>A human-readable error when the pattern is unsupported; otherwise <see langword="null" />.</returns>
    public static string? Validate(string glob)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(glob);

        string[] segments = glob.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < segments.Length - 1; i++)
        {
            string segment = segments[i];
            if (segment == "**")
            {
                continue;
            }

            if (segment.Contains('*') || segment.Contains('?'))
            {
                return $"[!] Wildcard in directory segment '{segment}' is not supported in '{glob}'. " +
                       "Use a filename-only wildcard (e.g. *.pdb) or '**' for recursion " +
                       "(e.g. Symbols\\**\\*.pdb).";
            }
        }

        string fileName = Path.GetFileName(glob);
        if (fileName == "**" || fileName.Length == 0)
        {
            return $"[!] Glob pattern '{glob}' has no filename component. " +
                   "Use a filename wildcard (e.g. *.pdb) or '**' with a filename " +
                   "(e.g. Symbols\\**\\*.pdb).";
        }

        return null;
    }

    /// <summary>
    ///     Returns the nearest directory portion of <paramref name="glob" /> that contains no wildcard characters.
    /// </summary>
    public static string NormalizeRoot(string glob)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(glob);

        string? directory = Path.GetDirectoryName(glob);
        if (string.IsNullOrEmpty(directory))
        {
            return ".";
        }

        while (directory.Contains('*') || directory.Contains('?'))
        {
            string? parent = Path.GetDirectoryName(directory);
            if (parent is null || parent.Length == 0 || parent == directory)
            {
                return ".";
            }

            directory = parent;
        }

        return directory;
    }

    /// <summary>
    ///     Enumerates files matched by a filename glob. The caller must already have validated <paramref name="glob" />.
    /// </summary>
    public static IEnumerable<string> Enumerate(string glob)
    {
        string root = NormalizeRoot(glob);
        string pattern = Path.GetFileName(glob);
        SearchOption search = glob.Contains("**", StringComparison.Ordinal)
            ? SearchOption.AllDirectories
            : SearchOption.TopDirectoryOnly;

        if (!Directory.Exists(root))
        {
            return [];
        }

        return Directory.EnumerateFiles(root, pattern, search);
    }
}
