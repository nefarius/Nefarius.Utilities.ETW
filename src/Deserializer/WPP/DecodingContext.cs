using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

using Windows.Win32.Foundation;

namespace Nefarius.Utilities.ETW.Deserializer.WPP;

public sealed class DecodingContext : IDisposable
{
    [SuppressMessage("ReSharper", "PrivateFieldCanBeConvertedToLocalVariable")]
    private readonly IList<DecodingContextType> _decodingTypes;

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

    ~DecodingContext()
    {
        ReleaseUnmanagedResources();
    }
}