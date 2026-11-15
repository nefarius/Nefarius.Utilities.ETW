namespace Nefarius.Utilities.ETW;

internal static class PlatformGuard
{
    /// <summary>
    ///     Real-time sessions and session management go through the Windows ETW controller APIs.
    /// </summary>
    public static void ThrowIfNotWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Real-time ETW sessions and session management are only available on Windows. " +
                "Reading .etl files works on every platform.");
        }
    }
}
