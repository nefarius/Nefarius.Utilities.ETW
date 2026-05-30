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

            WIN32_ERROR.ERROR_NO_SYSTEM_RESOURCES =>
                $"Cannot modify provider {{{providerGuid}}} on session '{sessionName}': the provider is already " +
                "enabled in the maximum number of concurrent trace sessions. Modern (manifest-based / TraceLogging) " +
                "providers allow up to 8 sessions; legacy WPP/MOF providers allow only 1. " +
                "This is usually caused by leftover sessions from previous runs that were killed before they could " +
                "stop cleanly. Run 'logman query -ets' to list them and 'logman stop <name> -ets' to remove " +
                "each one, or use 'etwutils sessions clean' if available.",

            _ => $"Cannot modify provider {{{providerGuid}}} on session '{sessionName}': " +
                 $"EnableTraceEx2 failed with Win32 error 0x{(uint)error:X8}."
        };
}
