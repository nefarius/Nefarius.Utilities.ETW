namespace Nefarius.Utilities.ETW;

/// <summary>
///     Path helpers that treat both <c>\</c> and <c>/</c> as separators on every platform. PDB names and symbol
///     globs recorded in traces are Windows paths, but they may be processed on a host where only <c>/</c> is a
///     separator.
/// </summary>
internal static class PathHelper
{
    private static readonly char[] Separators = ['\\', '/'];

    public static string GetFileName(string path)
    {
        int index = path.LastIndexOfAny(Separators);
        return index < 0 ? path : path[(index + 1)..];
    }

    public static string? GetDirectoryName(string path)
    {
        int index = path.LastIndexOfAny(Separators);
        if (index < 0)
        {
            return null;
        }

        // Keep a root such as "C:\" or "/" intact.
        if (index == 0 || (index == 2 && path[1] == ':'))
        {
            return path[..(index + 1)];
        }

        return path[..index];
    }

    /// <summary>
    ///     Rewrites separators so a Windows-style path can be passed to the host filesystem.
    /// </summary>
    public static string ToHostPath(string path) =>
        Path.DirectorySeparatorChar == '\\'
            ? path
            : path.Replace('\\', '/');
}
