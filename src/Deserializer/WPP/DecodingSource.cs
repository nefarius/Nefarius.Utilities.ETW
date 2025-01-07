namespace Nefarius.Utilities.ETW.Deserializer.WPP;

internal sealed class DecodingSource
{
    private readonly List<TDH_CONTEXT> _contexts = [];

    public unsafe void AddPDBFiles(IEnumerable<string> pdbPaths)
    {
        string paths = string.Join(';', pdbPaths);

        fixed (char* nativePaths = paths)
        {
            _contexts.Add(new TDH_CONTEXT
            {
                ParameterType = TDH_CONTEXT_TYPE.TDH_CONTEXT_PDB_PATH, ParameterValue = (ulong)nativePaths
            });
        }
    }

    public void AddTMFFile(string tmfPath)
    {
    }

    public unsafe void AddTMFSearchDirectories(IEnumerable<string> tmfSearchDirectories)
    {
        string paths = string.Join(';', tmfSearchDirectories);

        fixed (char* nativePaths = paths)
        {
            _contexts.Add(new TDH_CONTEXT
            {
               
            });
        }
    }
}