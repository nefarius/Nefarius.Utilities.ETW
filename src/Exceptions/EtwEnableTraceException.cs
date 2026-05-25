using System.ComponentModel;

using Windows.Win32.Foundation;

namespace Nefarius.Utilities.ETW.Exceptions;

/// <summary>
///     Thrown when <c>EnableTraceEx2</c> fails to enable or disable a provider on a realtime ETW session.
/// </summary>
public sealed class EtwEnableTraceException : Win32Exception
{
    internal EtwEnableTraceException(uint errorCode, Guid providerGuid, string sessionName)
        : base((int)errorCode, BuildMessage((WIN32_ERROR)errorCode, providerGuid, sessionName)) { }

    private static string BuildMessage(WIN32_ERROR error, Guid providerGuid, string sessionName) =>
        error switch
        {
            WIN32_ERROR.ERROR_ACCESS_DENIED =>
                $"Cannot modify provider {{{providerGuid}}} on session '{sessionName}': access denied.",

            WIN32_ERROR.ERROR_INVALID_PARAMETER =>
                $"Cannot modify provider {{{providerGuid}}} on session '{sessionName}': invalid parameter. " +
                "Verify that the session handle is valid and the provider GUID is correct.",

            WIN32_ERROR.ERROR_NOT_FOUND =>
                $"Cannot modify provider {{{providerGuid}}} on session '{sessionName}': provider not registered " +
                "on this system. The provider GUID may be incorrect.",

            _ => $"Cannot modify provider {{{providerGuid}}} on session '{sessionName}': " +
                 $"EnableTraceEx2 failed with Win32 error 0x{(uint)error:X8}."
        };
}
