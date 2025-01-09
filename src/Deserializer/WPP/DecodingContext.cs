using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

using Windows.Win32.Foundation;

namespace Nefarius.Utilities.ETW.Deserializer.WPP;

/// <summary>
///     WPP decoding context used to extract TMF information from resources like <c>.PDB</c> or <c>.TMF</c> files.
/// </summary>
public sealed class DecodingContext : IDisposable
{
    [SuppressMessage("ReSharper", "PrivateFieldCanBeConvertedToLocalVariable")]
    private readonly IList<DecodingContextType> _decodingTypes;

    /// <summary>
    ///     New decoding context instance.
    /// </summary>
    /// <param name="decodingTypes">One or more <see cref="DecodingContextType" />s to look up decoding information in.</param>
    /// <exception cref="Win32Exception">One or more TDH API calls failed.</exception>
    public unsafe DecodingContext(params IList<DecodingContextType> decodingTypes)
    {
        ArgumentNullException.ThrowIfNull(decodingTypes);
        _decodingTypes = decodingTypes;

        TDH_CONTEXT* ctx = stackalloc TDH_CONTEXT[_decodingTypes.Count];
        for (int i = 0; i < _decodingTypes.Count; i++)
        {
            ctx[i] = _decodingTypes[i].AsContext();
        }

        TDH_HANDLE decodingHandle;
#pragma warning disable CA1416
        WIN32_ERROR ret = (WIN32_ERROR)PInvoke.TdhOpenDecodingHandle(&decodingHandle);
#pragma warning restore CA1416

        if (ret != WIN32_ERROR.ERROR_SUCCESS)
        {
            throw new Win32Exception((int)ret);
        }

#pragma warning disable CA1416
        ret = (WIN32_ERROR)PInvoke.TdhSetDecodingParameter(decodingHandle, ctx);
#pragma warning restore CA1416

        if (ret != WIN32_ERROR.ERROR_SUCCESS)
        {
            throw new Win32Exception((int)ret);
        }

        Handle = decodingHandle;
    }

    internal TDH_HANDLE Handle { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        ReleaseUnmanagedResources();
        GC.SuppressFinalize(this);
    }

    private void ReleaseUnmanagedResources()
    {
#pragma warning disable CA1416
        PInvoke.TdhCloseDecodingHandle(Handle);
#pragma warning restore CA1416
    }

    /// <inheritdoc />
    ~DecodingContext()
    {
        ReleaseUnmanagedResources();
    }
}