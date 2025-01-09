using System.ComponentModel;

using Windows.Win32.Foundation;

namespace Nefarius.Utilities.ETW.Exceptions;

/// <inheritdoc />
public sealed class TdhGetPropertyException : Win32Exception
{
    internal TdhGetPropertyException(WIN32_ERROR error) : base((int)error) { }
}