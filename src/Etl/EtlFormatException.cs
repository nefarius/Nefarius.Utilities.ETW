using Windows.Win32.Foundation;

namespace Nefarius.Utilities.ETW.Etl;

/// <summary>
///     Thrown by the managed ETL reader when a file cannot be opened or is not a supported ETL file.
/// </summary>
internal sealed class EtlFormatException : Exception
{
    public EtlFormatException(WIN32_ERROR error, string message, Exception? inner = null) : base(message, inner)
    {
        Error = error;
    }

    /// <summary>
    ///     The Win32 error code the native <c>OpenTrace</c> would most plausibly have reported.
    /// </summary>
    public WIN32_ERROR Error { get; }
}
