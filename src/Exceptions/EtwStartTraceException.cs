using System.ComponentModel;

using Windows.Win32.Foundation;

namespace Nefarius.Utilities.ETW.Exceptions;

/// <summary>
///     Thrown when <c>StartTraceW</c> fails to create a realtime ETW session.
/// </summary>
public sealed class EtwStartTraceException : Win32Exception
{
    internal EtwStartTraceException(uint errorCode, string sessionName)
        : base((int)errorCode, BuildMessage((WIN32_ERROR)errorCode, sessionName)) { }

    private static string BuildMessage(WIN32_ERROR error, string sessionName) =>
        error switch
        {
            WIN32_ERROR.ERROR_ACCESS_DENIED =>
                $"Session '{sessionName}': access denied. Starting an ETW trace session requires elevated " +
                "(administrator) privileges or the SeSystemProfilePrivilege privilege.",

            WIN32_ERROR.ERROR_ALREADY_EXISTS =>
                $"Session '{sessionName}': a session with this name already exists. " +
                $"Call EtwUtil.StopOrphanSession(\"{sessionName}\") to clean up " +
                "a leftover session before creating a new one.",

            WIN32_ERROR.ERROR_INVALID_PARAMETER =>
                $"Session '{sessionName}': the session parameters are invalid (ERROR_INVALID_PARAMETER).",

            WIN32_ERROR.ERROR_BAD_LENGTH =>
                $"Session '{sessionName}': the properties buffer size is incorrect (ERROR_BAD_LENGTH).",

            _ => $"Session '{sessionName}': StartTrace failed with Win32 error 0x{(uint)error:X8}."
        };
}
