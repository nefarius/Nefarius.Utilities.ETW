using System.Collections.ObjectModel;

using Nefarius.Utilities.ETW.Deserializer.WPP;
using Nefarius.Utilities.ETW.Deserializer.WPP.TMF;

namespace Nefarius.Utilities.ETW.Tests;

/// <summary>
///     Code shared between tests and benchmark projects.
/// </summary>
public static class Shared
{
    public static ReadOnlyCollection<TraceMessageFormat> ExtractFromFormatFiles()
    {
        return TmfFilesDirectoryDecodingContextType
            .CreateFrom(@".\symbols")
            .SelectMany(item => item.TraceMessageFormats)
            .Distinct()
            .ToList()
            .AsReadOnly();
    }

    public static ReadOnlyCollection<TraceMessageFormat> ExtractFromSymbolFiles()
    {
        return PdbFileDecodingContextType
            .CreateFrom(
                @".\symbols\BthPS3.pdb",
                @".\symbols\BthPS3PSM.pdb",
                @".\symbols\DsHidMini.pdb"
                )
            .SelectMany(pdb => pdb.TraceMessageFormats)
            .Distinct()
            .ToList()
            .AsReadOnly();
    }
}