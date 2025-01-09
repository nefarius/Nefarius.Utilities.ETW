using System.ComponentModel;

using Windows.Win32.Foundation;

namespace Nefarius.Utilities.ETW.Exceptions;

/// <inheritdoc />
public sealed class TdhGetEventInformationException : Win32Exception
{
    internal TdhGetEventInformationException(WIN32_ERROR error) : base((int)error) { }
}