using System.Collections.ObjectModel;
using System.Text;
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

    /// <summary>
    ///     Decodes <c>BthPS3_0.etl</c> with both PDB files and returns the resulting JSON as a string.
    ///     Also writes the JSON to disk for reference.
    /// </summary>
    public static string BthPs3EtlTraceDecodeToString()
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
            throw new InvalidOperationException("BthPS3 ETL decode failed.");
        }

        using FileStream outFile = File.Create("BthPS3_0.json");
        ms.Position = 0;
        ms.CopyTo(outFile);

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>
    ///     Decodes <c>BthPS3_0.etl</c> with only <c>BthPS3.pdb</c> (deliberately omitting
    ///     <c>BthPS3PSM.pdb</c>) to exercise the "no format information found" fallback path.
    /// </summary>
    public static string BthPs3EtlIncompleteContextDecodeToString()
    {
        const string etwFilePath = @".\traces\BthPS3_0.etl";

        JsonWriterOptions options = new() { Indented = true };

        using MemoryStream ms = new();
        using Utf8JsonWriter jsonWriter = new(ms, options);
        DecodingContext decodingContext = new(PdbFileDecodingContextType.CreateFrom(
            @".\symbols\BthPS3.pdb"
        ));

        if (!EtwUtil.ConvertToJson(jsonWriter, [etwFilePath], converterOptions =>
            {
                converterOptions.WppDecodingContext = decodingContext;
            }))
        {
            throw new InvalidOperationException("BthPS3 partial ETL decode failed.");
        }

        using FileStream outFile = File.Create("BthPS3_0_partial.json");
        ms.Position = 0;
        ms.CopyTo(outFile);

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>
    ///     Decodes <c>DsHidMini.etl</c> with <c>DsHidMini.pdb</c> and returns the resulting JSON as a string.
    ///     Also writes the JSON to disk for reference.
    /// </summary>
    public static string DsHidMiniEtlTraceDecodeToString()
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
            throw new InvalidOperationException("DsHidMini ETL decode failed.");
        }

        using FileStream outFile = File.Create("DsHidMini.json");
        ms.Position = 0;
        ms.CopyTo(outFile);

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    // Kept for backward compatibility with benchmarks.
    public static bool BthPs3EtlTraceDecoding()
    {
        try
        {
            BthPs3EtlTraceDecodeToString();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool DsHidMiniEtlTraceDecoding()
    {
        try
        {
            DsHidMiniEtlTraceDecodeToString();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
