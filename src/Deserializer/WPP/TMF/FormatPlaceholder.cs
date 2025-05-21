using System.Text.RegularExpressions;

namespace Nefarius.Utilities.ETW.Deserializer.WPP.TMF;

public enum FormatCategory
{
    String,
    Integer,
    Pointer,
    Hexadecimal,
    Unknown
}

public partial class FormatPlaceholder
{
    public int? ZeroPaddingWidth { get; set; } // e.g., 2 for "02X", 12 for "012I64X"
    public string SizeSpecifier { get; set; }  // e.g., "I64"
    public char TypeSpecifier { get; set; }    // e.g., 'd', 'X', 's', 'p'
    public FormatCategory Category { get; set; }

    public override string ToString()
    {
        return $"Type: {TypeSpecifier}, Padding: {ZeroPaddingWidth}, Size: {SizeSpecifier}, Category: {Category}";
    }

    public static FormatPlaceholder Parse(string input)
    {
        // Match patterns like: 012I64X, 02X, s, d, u, p, etc.
        var match = FormatSpecifierExtractionRegex().Match(input);
        if (!match.Success)
        {
            return new FormatPlaceholder
            {
                TypeSpecifier = input.LastOrDefault(),
                Category = FormatCategory.Unknown
            };
        }

        int? padding = match.Groups[1].Success
            ? int.Parse(match.Groups[1].Value)
            : (int?)null;

        string size = match.Groups[2].Success ? match.Groups[2].Value : null;
        char type = match.Groups[3].Value[0];

        return new FormatPlaceholder
        {
            ZeroPaddingWidth = padding,
            SizeSpecifier = size,
            TypeSpecifier = type,
            Category = GetCategory(type)
        };
    }

    private static FormatCategory GetCategory(char type)
    {
        return type switch
        {
            's' => FormatCategory.String,
            'd' or 'u' => FormatCategory.Integer,
            'p' => FormatCategory.Pointer,
            'X' => FormatCategory.Hexadecimal,
            _ => FormatCategory.Unknown
        };
    }

    [GeneratedRegex(@"^(0\d+)?(I\d+)?([a-zA-Z])$")]
    private static partial Regex FormatSpecifierExtractionRegex();
}
