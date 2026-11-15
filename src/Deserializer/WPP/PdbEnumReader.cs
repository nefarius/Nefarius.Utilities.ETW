using Kaitai;

namespace Nefarius.Utilities.ETW.Deserializer.WPP;

/// <summary>
///     Reads enumerator names and values of enum types from the type stream (TPI) of a PDB, so WPP
///     <c>ItemEnum</c>/<c>ItemFlagsEnum</c> parameters can be rendered with names.
/// </summary>
internal static class PdbEnumReader
{
    /// <summary>
    ///     Returns, for every requested enum type name that has a complete (non forward-reference) definition, its
    ///     enumerators. Missing or unreadable types are left out.
    /// </summary>
    internal static Dictionary<string, IReadOnlyDictionary<long, string>> Read(MsPdb pdb,
        IReadOnlySet<string> wantedNames)
    {
        Dictionary<string, IReadOnlyDictionary<long, string>> result = new(StringComparer.Ordinal);
        if (wantedNames.Count == 0)
        {
            return result;
        }

        try
        {
            List<MsPdb.TpiType>? types = pdb.TpiStream?.Types?.Types;
            if (types is null)
            {
                return result;
            }

            Dictionary<uint, MsPdb.TpiType> byIndex = new();
            foreach (MsPdb.TpiType type in types)
            {
                byIndex[type.Ti] = type;
            }

            foreach (MsPdb.TpiType type in types)
            {
                if (type.Data?.Body is not MsPdb.LfEnum lfEnum || lfEnum.TypeProperties.ForwardReference)
                {
                    continue;
                }

                string? name = lfEnum.Name?.Value;
                if (name is null || !wantedNames.Contains(name) || result.ContainsKey(name))
                {
                    continue;
                }

                if (!byIndex.TryGetValue(lfEnum.FieldType.Index, out MsPdb.TpiType? fieldType) ||
                    fieldType.Data?.Body is not MsPdb.LfFieldlist fieldList)
                {
                    continue;
                }

                Dictionary<long, string> values = new();
                foreach (MsPdb.TpiTypeData member in fieldList.Data)
                {
                    if (member.Body is MsPdb.LfEnumerate enumerate && enumerate.FieldName is { } fieldName &&
                        TryGetValue(enumerate.Value.Value, out long value))
                    {
                        values.TryAdd(value, fieldName);
                    }
                }

                result[name] = values;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Partial or damaged type streams only cost the enum names; the raw values are still shown.
        }

        return result;
    }

    private static bool TryGetValue(object? raw, out long value)
    {
        // Small values are wrapped in a literal node; larger ones are carried by the numeric type directly.
        if (raw is MsPdb.CvNumericLiteral literal)
        {
            raw = literal.Value;
        }

        switch (raw)
        {
            case ulong u:
                value = unchecked((long)u);
                return true;
            case null:
                value = 0;
                return false;
            default:
                try
                {
                    value = Convert.ToInt64(raw);
                    return true;
                }
                catch (Exception ex) when (ex is InvalidCastException or OverflowException or FormatException)
                {
                    value = 0;
                    return false;
                }
        }
    }
}