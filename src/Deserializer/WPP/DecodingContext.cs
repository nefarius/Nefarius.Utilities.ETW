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
        WIN32_ERROR ret = (WIN32_ERROR)PInvoke.TdhOpenDecodingHandle(&decodingHandle);

        if (ret != WIN32_ERROR.ERROR_SUCCESS)
        {
            throw new Win32Exception((int)ret);
        }
        
        ret = (WIN32_ERROR)PInvoke.TdhSetDecodingParameter(decodingHandle, ctx);

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
        PInvoke.TdhCloseDecodingHandle(Handle);
    }

    /// <inheritdoc />
    ~DecodingContext()
    {
        ReleaseUnmanagedResources();
    }
}