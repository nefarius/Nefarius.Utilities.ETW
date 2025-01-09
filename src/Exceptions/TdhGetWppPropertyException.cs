using System.ComponentModel;

using Windows.Win32.Foundation;

namespace Nefarius.Utilities.ETW.Exceptions;

/// <inheritdoc />
public sealed class TdhGetWppPropertyException : Win32Exception
{
    internal TdhGetWppPropertyException(WIN32_ERROR error) : base((int)error) { }
}