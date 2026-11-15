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

        string fileName = PathHelper.GetFileName(glob);
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

        string? directory = PathHelper.GetDirectoryName(glob);
        if (string.IsNullOrEmpty(directory))
        {
            return ".";
        }

        while (directory.Contains('*') || directory.Contains('?'))
        {
            string? parent = PathHelper.GetDirectoryName(directory);
            if (parent is null || parent.Length == 0 || parent == directory)
            {
                return ".";
            }

            directory = parent;
        }

        return directory;
    }

    /// <summary>
    ///     Enumerates files matched by a glob. The caller must already have validated <paramref name="glob" />.
    ///     Directory segments after the wildcard-free root are honoured: a literal segment must exist, and a
    ///     standalone <c>**</c> matches zero or more directories, so <c>root\**\sub\*.pdb</c> only returns files
    ///     inside directories named <c>sub</c> below <c>root</c>.
    /// </summary>
    public static IEnumerable<string> Enumerate(string glob)
    {
        string root = PathHelper.ToHostPath(NormalizeRoot(glob));
        if (!Directory.Exists(root))
        {
            return [];
        }

        string[] segments = glob.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        string[] directories = segments[..^1];
        int firstWildcard = Array.FindIndex(directories, s => s.Contains('*') || s.Contains('?'));
        string[] rest = firstWildcard < 0 ? [] : directories[firstWildcard..];

        return Walk(root, rest, 0, PathHelper.GetFileName(glob)).Distinct(StringComparer.Ordinal);
    }

    private static readonly EnumerationOptions DirectoryOptions = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint
    };

    private static IEnumerable<string> Walk(string directory, string[] rest, int index, string filePattern)
    {
        if (index == rest.Length)
        {
            foreach (string file in Directory.EnumerateFiles(directory, filePattern, SearchOption.TopDirectoryOnly))
            {
                yield return file;
            }

            yield break;
        }

        if (rest[index] == "**")
        {
            // zero directories
            foreach (string file in Walk(directory, rest, index + 1, filePattern))
            {
                yield return file;
            }

            // one or more directories
            foreach (string child in Directory.EnumerateDirectories(directory, "*", DirectoryOptions))
            {
                foreach (string file in Walk(child, rest, index, filePattern))
                {
                    yield return file;
                }
            }

            yield break;
        }

        string literal = Path.Combine(directory, rest[index]);
        if (Directory.Exists(literal))
        {
            foreach (string file in Walk(literal, rest, index + 1, filePattern))
            {
                yield return file;
            }
        }
    }
}