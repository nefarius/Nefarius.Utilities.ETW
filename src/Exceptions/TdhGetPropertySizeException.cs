using System.ComponentModel;

using Windows.Win32.Foundation;

namespace Nefarius.Utilities.ETW.Exceptions;

/// <inheritdoc />
public sealed class TdhGetPropertySizeException : Win32Exception
{
    internal TdhGetPropertySizeException(WIN32_ERROR error) : base((int)error) { }
}