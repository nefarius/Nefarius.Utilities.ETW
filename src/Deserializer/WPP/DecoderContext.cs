namespace Nefarius.Utilities.ETW.Deserializer.WPP;

internal sealed class DecoderContext : IDisposable
{
    private DecoderContext(TDH_HANDLE handle)
    {
        Handle = handle;
    }

    public TDH_HANDLE Handle { get; }

    public void Dispose()
    {
        ReleaseUnmanagedResources();
        GC.SuppressFinalize(this);
    }

    public static unsafe DecoderContext CreateFromPDBs(IEnumerable<string> pdbPaths)
    {
        string paths = string.Join(';', pdbPaths);

        fixed (char* nativePaths = paths)
        {
            TDH_CONTEXT* ctx = stackalloc TDH_CONTEXT[1];
            ctx->ParameterType = TDH_CONTEXT_TYPE.TDH_CONTEXT_PDB_PATH;
            ctx->ParameterValue = (ulong)nativePaths;

            TDH_HANDLE decodingHandle;

            uint ret = PInvoke.TdhOpenDecodingHandle(&decodingHandle);

            return new DecoderContext(decodingHandle);
        }
    }

    public static unsafe DecoderContext CreateFromTMFFile(string filePath)
    {
        fixed (char* nativePaths = filePath)
        {
            TDH_CONTEXT* ctx = stackalloc TDH_CONTEXT[1];
            ctx->ParameterType = TDH_CONTEXT_TYPE.TDH_CONTEXT_WPP_TMFFILE;
            ctx->ParameterValue = (ulong)nativePaths;

            TDH_HANDLE decodingHandle;

            uint ret = PInvoke.TdhOpenDecodingHandle(&decodingHandle);

            return new DecoderContext(decodingHandle);
        }
    }

    public static unsafe DecoderContext CreateFromTMFDirectories(IEnumerable<string> tmfSearchPaths)
    {
        string paths = string.Join(';', tmfSearchPaths);

        fixed (char* nativePaths = paths)
        {
            TDH_CONTEXT* ctx = stackalloc TDH_CONTEXT[1];
            ctx->ParameterType = TDH_CONTEXT_TYPE.TDH_CONTEXT_WPP_TMFSEARCHPATH;
            ctx->ParameterValue = (ulong)nativePaths;

            TDH_HANDLE decodingHandle;

            uint ret = PInvoke.TdhOpenDecodingHandle(&decodingHandle);

            return new DecoderContext(decodingHandle);
        }
    }

    private void ReleaseUnmanagedResources()
    {
        PInvoke.TdhCloseDecodingHandle(Handle);
    }

    ~DecoderContext()
    {
        ReleaseUnmanagedResources();
    }
}