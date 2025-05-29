using System.Collections.ObjectModel;
using System.Text.Json;

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

    public static bool BthPs3EtlTraceDecoding()
    {
        const string etwFilePath = @".\traces\BthPS3_0.etl";

        JsonWriterOptions options = new() { Indented = true };

        using MemoryStream ms = new();
        using Utf8JsonWriter jsonWriter = new(ms, options);
        DecodingContext decodingContext = new(PdbFileDecodingContextType.CreateFrom(
            @".\symbols\BthPS3.pdb",
            @".\symbols\BthPS3PSM.pdb"
        ));

        if (!EtwUtil.ConvertToJson(jsonWriter, [etwFilePath], converterOptions =>
            {
                converterOptions.WppDecodingContext = decodingContext;
            }))
        {
            return false;
        }

        ms.Seek(0, SeekOrigin.Begin);

        using FileStream outFile = File.OpenWrite("BthPS3_0.json");
        ms.CopyTo(outFile);

        return true;
    }

    public static bool DsHidMiniEtlTraceDecoding()
    {
        const string etwFilePath = @".\traces\DsHidMini.etl";

        JsonWriterOptions options = new() { Indented = true };

        using MemoryStream ms = new();
        using Utf8JsonWriter jsonWriter = new(ms, options);
        DecodingContext decodingContext = new(new PdbFileDecodingContextType(@".\symbols\DsHidMini.pdb"));

        if (!EtwUtil.ConvertToJson(jsonWriter, [etwFilePath], converterOptions =>
            {
                converterOptions.WppDecodingContext = decodingContext;
            }))
        {
            return false;
        }

        ms.Seek(0, SeekOrigin.Begin);

        using FileStream outFile = File.OpenWrite("DsHidMini.json");
        ms.CopyTo(outFile);

        return true;
    }
}