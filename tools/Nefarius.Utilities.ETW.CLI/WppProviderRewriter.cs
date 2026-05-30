using System.Buffers;
using System.Text.Json;

using Nefarius.Utilities.ETW.Deserializer.WPP;

namespace Nefarius.Utilities.ETW.CLI;

/// <summary>
///     Rewrites the <c>GuidName</c> property of WPP event buffers using the friendly control-GUID name
///     recovered from PDB <c>TMC:</c> annotations, providing a more meaningful provider label than the
///     folder-derived token the WPP decoder normally emits.
/// </summary>
internal static class WppProviderRewriter
{
    /// <summary>
    ///     Builds a <c>ControlGuid → friendly name</c> lookup from the loaded decoding contexts.
    ///     Only <see cref="PdbFileDecodingContextType" /> sources carry <c>TMC:</c> control records;
    ///     TMF-only sources contribute nothing and are silently skipped.
    ///     Entries whose <see cref="Deserializer.WPP.TMF.WppTraceControl.Name" /> is null or empty are
    ///     also skipped so a real value is never replaced by an empty string.
    /// </summary>
    /// <returns>
    ///     A dictionary of GUIDs to names. Empty when no TMC information is available.
    /// </returns>
    public static Dictionary<Guid, string> BuildNameMap(IEnumerable<DecodingContextType> types)
    {
        Dictionary<Guid, string> map = new();

        foreach (DecodingContextType type in types)
        {
            if (type is not PdbFileDecodingContextType pdb)
            {
                continue;
            }

            foreach (Deserializer.WPP.TMF.WppTraceControl ctrl in pdb.WppTraceControls)
            {
                if (string.IsNullOrEmpty(ctrl.Name))
                {
                    continue;
                }

                // First entry wins on duplicate GUIDs across multiple PDB files.
                map.TryAdd(ctrl.ControlGuid, ctrl.Name);
            }
        }

        return map;
    }

    /// <summary>
    ///     Rewrites the <c>GuidName</c> value inside a single WPP NDJSON event buffer using the
    ///     supplied provider-name map.  Returns the original buffer unchanged (zero-copy) in every
    ///     graceful-fallback case:
    ///     <list type="bullet">
    ///         <item>The event is not a WPP event (<c>Event.Name != "WPP"</c>).</item>
    ///         <item><c>Properties[0].TraceGuid</c> is absent or cannot be parsed.</item>
    ///         <item>The GUID is not present in <paramref name="map" />.</item>
    ///         <item>The mapped name is null/empty.</item>
    ///         <item>The mapped name is identical to the existing <c>GuidName</c> value.</item>
    ///         <item>The JSON cannot be parsed.</item>
    ///     </list>
    /// </summary>
    public static ReadOnlyMemory<byte> Rewrite(
        ReadOnlyMemory<byte> json,
        IReadOnlyDictionary<Guid, string> map)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return json;
        }

        using (doc)
        {
            JsonElement root = doc.RootElement;

            if (!root.TryGetProperty("Event", out JsonElement evt))
            {
                return json;
            }

            if (!evt.TryGetProperty("Name", out JsonElement nameEl) ||
                nameEl.GetString() != "WPP")
            {
                return json;
            }

            if (!evt.TryGetProperty("Properties", out JsonElement propsArr) ||
                propsArr.ValueKind != JsonValueKind.Array ||
                propsArr.GetArrayLength() == 0)
            {
                return json;
            }

            JsonElement props0 = propsArr[0];

            // Resolve the control GUID from TraceGuid.
            if (!props0.TryGetProperty("TraceGuid", out JsonElement traceGuidEl) ||
                traceGuidEl.ValueKind != JsonValueKind.String)
            {
                return json;
            }

            if (!Guid.TryParse(traceGuidEl.GetString(), out Guid traceGuid))
            {
                return json;
            }

            if (!map.TryGetValue(traceGuid, out string? newName) ||
                string.IsNullOrEmpty(newName))
            {
                return json;
            }

            // Read the existing GuidName; if it already matches, skip re-emission.
            if (props0.TryGetProperty("GuidName", out JsonElement existingGn) &&
                existingGn.GetString() == newName)
            {
                return json;
            }

            // Re-emit the full buffer substituting only GuidName inside Properties[0].
            ArrayBufferWriter<byte> bufferWriter = new(json.Length + 64);
            using Utf8JsonWriter writer = new(bufferWriter, new JsonWriterOptions { SkipValidation = true });

            // Root object
            writer.WriteStartObject();

            // "Event": { ... }
            writer.WritePropertyName("Event");
            writer.WriteStartObject();

            foreach (JsonProperty evtProp in evt.EnumerateObject())
            {
                if (evtProp.Name != "Properties")
                {
                    evtProp.WriteTo(writer);
                    continue;
                }

                writer.WritePropertyName("Properties");
                writer.WriteStartArray();

                int arrIdx = 0;
                foreach (JsonElement elem in propsArr.EnumerateArray())
                {
                    writer.WriteStartObject();

                    foreach (JsonProperty p in elem.EnumerateObject())
                    {
                        if (arrIdx == 0 && p.Name == "GuidName")
                        {
                            writer.WriteString("GuidName", newName);
                        }
                        else
                        {
                            p.WriteTo(writer);
                        }
                    }

                    writer.WriteEndObject();
                    arrIdx++;
                }

                writer.WriteEndArray();
            }

            writer.WriteEndObject(); // Event
            writer.WriteEndObject(); // root

            writer.Flush();
            return bufferWriter.WrittenMemory;
        }
    }
}
