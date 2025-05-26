using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

using Windows.Win32.Foundation;

using Nefarius.Utilities.ETW.Deserializer.WPP.TMF;

namespace Nefarius.Utilities.ETW.Deserializer.WPP;

/// <summary>
///     WPP decoding context used to extract TMF information from resources like <c>.PDB</c> or <c>.TMF</c> files.
/// </summary>
[SuppressMessage("ReSharper", "UnusedMember.Global")]
public sealed class DecodingContext : IDisposable
{
    private readonly ReadOnlyCollection<DecodingContextType> _decodingTypes;

    /// <summary>
    ///     New decoding context instance.
    /// </summary>
    /// <param name="decodingTypes">One or more <see cref="DecodingContextType" />s to look up decoding information in.</param>
    /// <exception cref="Win32Exception">One or more TDH API calls failed.</exception>
    public unsafe DecodingContext(params IList<DecodingContextType> decodingTypes)
    {
        ArgumentNullException.ThrowIfNull(decodingTypes);
        _decodingTypes = decodingTypes.AsReadOnly();

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

    /// <summary>
    ///     Collection of extracted <see cref="TraceMessageFormat" />s of this <see cref="DecodingContext" />.
    /// </summary>
    public IEnumerable<TraceMessageFormat> TraceMessageFormats => _decodingTypes
        .SelectMany(t => t.TraceMessageFormats);

    [Obsolete]
    internal TDH_HANDLE Handle { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        ReleaseUnmanagedResources();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    ///     Creates a new <see cref="DecodingContext" /> instance with additionally provided <see cref="DecodingContextType" />
    ///     s.
    /// </summary>
    /// <param name="additionalDecodingTypes">
    ///     One or more <see cref="DecodingContextType" />s to be added to this instances'
    ///     types.
    /// </param>
    /// <returns>The extended <see cref="DecodingContext" /> instance.</returns>
    public DecodingContext ExtendWith(params IList<DecodingContextType> additionalDecodingTypes)
    {
        return new DecodingContext((IList<DecodingContextType>)_decodingTypes.Concat(additionalDecodingTypes));
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