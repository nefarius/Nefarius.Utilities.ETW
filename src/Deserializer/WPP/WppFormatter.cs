using System.ComponentModel;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

using Nefarius.Utilities.ETW.Deserializer.WPP.TMF;

namespace Nefarius.Utilities.ETW.Deserializer.WPP;

/// <summary>
///     Pure formatting logic for WPP trace messages, decoupled from event-record I/O so it can be
///     tested independently of a live <see cref="EventRecordReader" />.
/// </summary>
internal static partial class WppFormatter
{
    [GeneratedRegex(@"(0[xX])?%(\d+)!([^!]*)!", RegexOptions.Compiled)]
    internal static partial Regex PlaceholderRegex();

    [GeneratedRegex(@"^(?<pad>0)?(?<width>\d+)?(?<modifier>I\d+|ll|l|hh|h|I)?(?<specifier>[XxduUi])$")]
    internal static partial Regex NumericFormatTokenRegex();

    // Matches %!NAME! context markers (e.g. %!FUNC!, %!LEVEL!)
    [GeneratedRegex(@"%!(?<name>[A-Za-z_][A-Za-z0-9_]*)!", RegexOptions.Compiled)]
    internal static partial Regex ContextMarkerRegex();

    // Matches the %0 standard-prefix sentinel at the start of the string (with optional trailing space)
    [GeneratedRegex(@"^%0 ?")]
    internal static partial Regex StdPrefixRegex();

    /// <summary>
    ///     Substitutes context markers (<c>%!FUNC!</c>, <c>%!LEVEL!</c>, <c>%!FLAGS!</c>, <c>%!FILE!</c>, etc.)
    ///     and strips the <c>%0</c> STDPREFIX sentinel from a raw message-format string.
    /// </summary>
    internal static string SubstituteContext(TraceMessageFormat format, string message)
    {
        // Strip leading %0 / "%0 " (the STDPREFIX sentinel injected by USEPREFIX(*,"%!STDPREFIX!"))
        message = StdPrefixRegex().Replace(message, string.Empty);

        // Replace %!NAME! context markers
        message = ContextMarkerRegex().Replace(message, match =>
        {
            string name = match.Groups["name"].Value;
            return name.ToUpperInvariant() switch
            {
                "FUNC"     => format.Function ?? string.Empty,
                "LEVEL"    => format.Level    ?? string.Empty,
                "FLAGS"    => format.Flags    ?? string.Empty,
                "KEYWORDS" => format.Flags    ?? string.Empty, // KEYWORDS is the manifest alias of FLAGS
                "FILE"     => format.FileName ?? string.Empty,
                // These are compile-time constants (e.g. __LINE__, __COMPNAME__); no TMF metadata available
                "LINE"     => string.Empty,
                "COMPNAME" => string.Empty,
                // Leave unknown markers intact so the caller can see them
                _          => match.Value
            };
        });

        return message;
    }

    /// <summary>
    ///     Substitutes placeholders in <paramref name="format" />'s message-format string using
    ///     <paramref name="parameterValues" />, which maps each parameter index to the already-read
    ///     (<see cref="FunctionParameter" />, value) pair.
    /// </summary>
    internal static string Substitute(
        TraceMessageFormat format,
        IReadOnlyDictionary<int, (FunctionParameter Parameter, object Value)> parameterValues)
    {
        // First pass: resolve context markers and strip %0
        string message = SubstituteContext(format, format.MessageFormat);

        if (!format.FunctionParameters.Any())
        {
            return message;
        }

        // Second pass: substitute %N!fmt! parameter placeholders
        return PlaceholderRegex().Replace(message, match =>
        {
            bool isHexPrefixed = match.Groups[1].Success;
            string formatSpec = match.Groups[3].Value;

            if (!int.TryParse(match.Groups[2].Value, out int index) ||
                !parameterValues.TryGetValue(index, out (FunctionParameter Parameter, object Value) pair))
            {
                return match.Value; // leave placeholder intact when index is not present
            }

            try
            {
                switch (formatSpec)
                {
                    case "s":
                        return ItemToString(pair.Parameter, pair.Value);

                    case "p":
                        return pair.Value switch
                        {
                            IntPtr ptr => $"0x{ptr:X}",
                            long l     => $"0x{l:X}",
                            int i      => $"0x{i:X}",
                            _          => $"0x{Convert.ToUInt64(pair.Value):X}"
                        };

                    default:
                    {
                        Match numberMatch = NumericFormatTokenRegex().Match(formatSpec);

                        if (!numberMatch.Success)
                        {
                            return string.Format($"{{0:{formatSpec}}}", pair.Value);
                        }

                        string pad  = numberMatch.Groups["pad"].Success ? "0" : "";
                        string width = numberMatch.Groups["width"].Value;
                        // WPP's "%u" means unsigned decimal; .NET has no "U" format specifier,
                        // so map it to "D". All other specifiers (x/X → "X", d → "D") are safe
                        // to uppercase directly.
                        string rawSpecifier = numberMatch.Groups["specifier"].Value;
                        // u/U = unsigned decimal; i = signed decimal (C alias of d); both map to .NET "D"
                        string specifier = rawSpecifier is "u" or "U" or "i" ? "D" : rawSpecifier.ToUpperInvariant();
                        string suffix    = $"{pad}{width}";

                        string finalFormat = isHexPrefixed
                            ? $"0x{{0:{specifier}{suffix}}}"
                            : $"{{0:{specifier}{suffix}}}";

                        return string.Format(finalFormat, pair.Value);
                    }
                }
            }
            catch
            {
                return $"<format error for %{index}!{formatSpec}!>";
            }
        });
    }

    /// <summary>
    ///     Converts a single parameter value to its string representation, handling enum lists,
    ///     NTSTATUS, WINERROR, HRESULT, timestamps, IP addresses, and all other WPP ItemTypes.
    /// </summary>
    internal static string ItemToString(FunctionParameter parameter, object value)
    {
        // List-byte enum (ItemListByte, ItemListShort, ItemListLong): look up by zero-based index
        if (parameter.ListItems is not null && parameter.Type is
                ItemType.ItemListByte or
                ItemType.ItemListShort or
                ItemType.ItemListLong)
        {
            int idx = parameter.Type switch
            {
                ItemType.ItemListByte  => (byte)value,
                ItemType.ItemListShort => (short)value,
                _                      => (int)value
            };
            return parameter.ListItems.TryGetValue(idx, out string? label) ? label : idx.ToString();
        }

        // Set-bit enums (ItemSetByte, ItemSetShort, ItemSetLong): comma-join the names of each set bit
        if (parameter.ListItems is not null && parameter.Type is
                ItemType.ItemSetByte or
                ItemType.ItemSetShort or
                ItemType.ItemSetLong)
        {
            ulong bits = parameter.Type switch
            {
                ItemType.ItemSetByte  => (byte)value,
                ItemType.ItemSetShort => (ushort)value,
                _                     => (uint)value
            };

            List<string> names = [];
            for (int i = 0; i < parameter.ListItems.Count; i++)
            {
                ulong mask = 1UL << i;
                if ((bits & mask) != 0 && parameter.ListItems.TryGetValue(i, out string? bitName))
                {
                    names.Add(bitName);
                }
            }

            return names.Count > 0 ? string.Join(", ", names) : $"0x{bits:X}";
        }

        switch (parameter.Type)
        {
            case ItemType.ItemNTSTATUS:
            case ItemType.ItemNDIS_STATUS:
            {
                uint ntStatus = (uint)value;
                return NtStatus.Values.TryGetValue(ntStatus, out string? status)
                    ? $"{status} (0x{ntStatus:X8})"
                    : $"Unknown NTSTATUS Error code: 0x{ntStatus:X8}";
            }
            case ItemType.ItemWINERROR:
            {
                uint error = (uint)value;
                return new Win32Exception((int)error).Message;
            }
            case ItemType.ItemHRESULT:
            {
                int hr = (int)value;
                return Marshal.GetExceptionForHR(hr)?.Message
                       ?? $"Unknown HRESULT Error code: 0x{hr:X8}";
            }
            case ItemType.ItemNDIS_OID:
            {
                uint oid = (uint)value;
                return $"0x{oid:X8}";
            }
            // GUID-based types — all formatted identically since we have no registry to look up names
            case ItemType.ItemGuid:
            case ItemType.ItemCLSID:
            case ItemType.ItemLIBID:
            case ItemType.ItemIID:
            {
                return ((Guid)value).ToString("B").ToUpperInvariant();
            }
            // IPv4 address stored as a UInt32 in network byte order
            case ItemType.ItemIPAddr:
            {
                uint raw = (uint)value;
                return new IPAddress(BitConverter.GetBytes(raw)).ToString();
            }
            // Port number stored as a UInt16 in network byte order
            case ItemType.ItemPort:
            {
                ushort raw = (ushort)value;
                // Convert from network byte order (big-endian) to host byte order
                ushort hostPort = (ushort)IPAddress.NetworkToHostOrder((short)raw);
                return hostPort.ToString();
            }
            // Absolute timestamp types: FILETIME Int64 → ISO-8601
            // Backs %!TIMESTAMP!, %!TIME!, %!DATE!, %!datetime!, %!WAITTIME!
            case ItemType.ItemTimestamp:
            {
                long ft = (long)value;
                return DateTime.FromFileTimeUtc(ft).ToString("o");
            }
            // Duration types: LONGLONG in milliseconds → day~h:mm:ss
            // %!delta! (ItemTimeDelta) and %!due! (ItemWaitTime) both use this format per the WPP docs
            case ItemType.ItemTimeDelta:
            case ItemType.ItemWaitTime:
            {
                long ms = (long)value;
                long totalSecs = Math.Abs(ms) / 1000;
                long days    = totalSecs / 86400;
                long hours   = totalSecs % 86400 / 3600;
                long minutes = totalSecs % 3600 / 60;
                long secs    = totalSecs % 60;
                return $"{days}~{hours}:{minutes:D2}:{secs:D2}";
            }
            // FOURCC / four-char-code: render each byte as a printable ASCII character (dot for non-printable)
            case ItemType.ItemChar4:
            {
                int raw = (int)value;
                byte[] bytes = BitConverter.GetBytes(raw);
                StringBuilder sb = new(4);
                foreach (byte b in bytes)
                {
                    sb.Append(b >= 32 && b < 127 ? (char)b : '.');
                }
                return sb.ToString();
            }
            // Enum types: PDB-based lookup is out of scope; emit raw value
            case ItemType.ItemEnum:
            {
                return Convert.ToUInt32(value).ToString();
            }
            case ItemType.ItemFlagsEnum:
            {
                return $"0x{Convert.ToUInt32(value):X8}";
            }
            default:
                return value.ToString() ?? throw new InvalidOperationException("Unexpected null value.");
        }
    }
}
