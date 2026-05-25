using System.ComponentModel;

using Windows.Win32.Foundation;

namespace Nefarius.Utilities.ETW.Exceptions;

/// <inheritdoc />
public sealed class EtwOpenTraceException : Win32Exception
{
    /// <param name="error">The Win32 error code returned by <c>OpenTrace</c>.</param>
    /// <param name="source">
    ///     A human-readable description of the source that failed to open:
    ///     a file path (e.g. <c>C:\trace.etl</c>) or a session name (e.g. <c>MySession</c>).
    /// </param>
    internal EtwOpenTraceException(WIN32_ERROR error, string source)
        : base((int)error, BuildMessage(error, source)) { }

    private static string BuildMessage(WIN32_ERROR error, string source) =>
        error switch
        {
            WIN32_ERROR.ERROR_INVALID_PARAMETER =>
                $"OpenTrace failed for '{source}' (0x57 – ERROR_INVALID_PARAMETER): the parameter is NULL or invalid.",
            WIN32_ERROR.ERROR_BAD_PATHNAME =>
                $"OpenTrace failed for '{source}' (0xA1 – ERROR_BAD_PATHNAME): the specified path is invalid.",
            WIN32_ERROR.ERROR_ACCESS_DENIED =>
                $"OpenTrace failed for '{source}' (0x05 – ERROR_ACCESS_DENIED): access is denied.",
            _ =>
                $"OpenTrace failed for '{source}' with Win32 error 0x{(uint)error:X8}."
        };
}
