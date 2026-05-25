using System.ComponentModel;

using Windows.Win32.Foundation;

namespace Nefarius.Utilities.ETW.Exceptions;

/// <inheritdoc />
public sealed class EtwOpenTraceException : Win32Exception
{
    internal EtwOpenTraceException(WIN32_ERROR error, string filePath)
        : base((int)error, BuildMessage(error, filePath)) { }

    private static string BuildMessage(WIN32_ERROR error, string filePath) =>
        error switch
        {
            WIN32_ERROR.ERROR_INVALID_PARAMETER => $"For file: {filePath} Windows returned 0x57 -- The Logfile parameter is NULL.",
            WIN32_ERROR.ERROR_BAD_PATHNAME => $"For file: {filePath} Windows returned 0xA1 -- The specified path is invalid.",
            WIN32_ERROR.ERROR_ACCESS_DENIED => $"For file: {filePath} Windows returned 0x5 -- Access is denied.",
            _ => $"For file: {filePath} Windows returned an unknown error."
        };
}
