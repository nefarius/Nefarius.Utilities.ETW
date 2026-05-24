using System.ComponentModel;
using System.Runtime.InteropServices;
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

    [GeneratedRegex(@"^(?<pad>0)?(?<width>\d+)?(?<modifier>I\d+)?(?<specifier>[XxduU])$")]
    internal static partial Regex NumericFormatTokenRegex();

    /// <summary>
    ///     Substitutes placeholders in <paramref name="format" />'s message-format string using
    ///     <paramref name="parameterValues" />, which maps each parameter index to the already-read
    ///     (<see cref="FunctionParameter" />, value) pair.
    /// </summary>
    internal static string Substitute(
        TraceMessageFormat format,
        IReadOnlyDictionary<int, (FunctionParameter Parameter, object Value)> parameterValues)
    {
        if (!format.FunctionParameters.Any())
        {
            return format.MessageFormat;
        }

        return PlaceholderRegex().Replace(format.MessageFormat, match =>
        {
            bool isHexPrefixed = match.Groups[1].Success;
            int index = int.Parse(match.Groups[2].Value);
            string formatSpec = match.Groups[3].Value;

            if (!parameterValues.TryGetValue(index, out (FunctionParameter Parameter, object Value) pair))
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
                        string specifier = rawSpecifier is "u" or "U" ? "D" : rawSpecifier.ToUpperInvariant();
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
    ///     NTSTATUS, WINERROR, and HRESULT translations.
    /// </summary>
    internal static string ItemToString(FunctionParameter parameter, object value)
    {
        if (parameter is { Type: ItemType.ItemListByte, ListItems: not null })
        {
            return parameter.ListItems[(byte)value];
        }

        switch (parameter.Type)
        {
            case ItemType.ItemNTSTATUS:
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
            default:
                return value.ToString() ?? throw new InvalidOperationException("Unexpected null value.");
        }
    }
}
