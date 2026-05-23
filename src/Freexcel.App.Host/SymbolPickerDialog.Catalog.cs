using System.Text;

namespace Freexcel.App.Host;

public sealed partial class SymbolPickerDialog
{
    private static readonly string[] SubsetChoices =
    [
        "Latin-1 Supplement",
        "Greek and Coptic",
        "Cyrillic",
        "Currency Symbols",
        "Arrows",
        "Mathematical Operators",
        "Box Drawing",
        "Geometric Shapes"
    ];

    private static readonly string[] CommonSymbols = ["\u20ac", "\u00a3", "\u00a5", "\u00a9", "\u00ae", "\u2122", "\u00b0", "\u00b1"];

    private static readonly IReadOnlyDictionary<string, char[]> SymbolsBySubset = new Dictionary<string, char[]>
    {
        ["Latin-1 Supplement"] = ['\u00a1', '\u00a2', '\u00a3', '\u00a4', '\u00a5', '\u00a7', '\u00a9', '\u00ab', '\u00ac', '\u00ae', '\u00b0', '\u00b1', '\u00b5', '\u00b6', '\u00bb', '\u00bf', '\u00c6', '\u00d7', '\u00d8', '\u00df', '\u00e6', '\u00f1', '\u00f7', '\u00f8'],
        ["Greek and Coptic"] = ['\u0391', '\u0392', '\u0393', '\u0394', '\u0398', '\u039b', '\u039e', '\u03a0', '\u03a3', '\u03a6', '\u03a9', '\u03b1', '\u03b2', '\u03b3', '\u03b4', '\u03b8', '\u03bb', '\u03bc', '\u03c0', '\u03c3', '\u03c6', '\u03c9'],
        ["Cyrillic"] = ['\u0401', '\u0410', '\u0411', '\u0412', '\u0413', '\u0414', '\u0415', '\u0416', '\u0417', '\u0418', '\u0419', '\u041a', '\u041b', '\u041c', '\u041d', '\u041e', '\u041f', '\u0420', '\u0421', '\u0422', '\u0423', '\u0424', '\u0425', '\u0426', '\u0427', '\u0428', '\u0429', '\u042e', '\u042f'],
        ["Currency Symbols"] = ['\u20ac', '\u00a3', '\u00a5', '\u00a2', '\u00a4', '\u20a9', '\u20aa', '\u20ab', '\u20ad', '\u20ae', '\u20af', '\u20b4', '\u20b8', '\u20ba', '\u20bd', '\u20bf'],
        ["Arrows"] = ['\u2190', '\u2191', '\u2192', '\u2193', '\u2194', '\u2195', '\u2196', '\u2197', '\u2198', '\u2199', '\u21a9', '\u21aa', '\u21d0', '\u21d2', '\u21d4', '\u21e6', '\u21e7', '\u21e8', '\u21e9'],
        ["Mathematical Operators"] = ['\u00b1', '\u00d7', '\u00f7', '\u2212', '\u221e', '\u2211', '\u221a', '\u221b', '\u222b', '\u2248', '\u2260', '\u2264', '\u2265', '\u2282', '\u2283', '\u22c5'],
        ["Box Drawing"] = ['\u2500', '\u2502', '\u250c', '\u2510', '\u2514', '\u2518', '\u251c', '\u2524', '\u252c', '\u2534', '\u253c', '\u2550', '\u2551', '\u2554', '\u2557', '\u255a', '\u255d', '\u256c'],
        ["Geometric Shapes"] = ['\u25cf', '\u25cb', '\u25a0', '\u25a1', '\u25b2', '\u25b3', '\u25bc', '\u25bd', '\u25c6', '\u25c7', '\u25ca', '\u25d8', '\u25d9', '\u2605', '\u2606']
    };

    private static readonly SpecialCharacter[] SpecialCharacters =
    [
        new("Em Dash", "\u2014"),
        new("En Dash", "\u2013"),
        new("Nonbreaking Space", "\u00a0"),
        new("Copyright", "\u00a9"),
        new("Registered", "\u00ae"),
        new("Trademark", "\u2122"),
        new("Section", "\u00a7"),
        new("Paragraph", "\u00b6"),
        new("Ellipsis", "\u2026"),
        new("Degree", "\u00b0")
    ];

    public static IReadOnlyList<char> GetSymbolsForSubset(string subset) =>
        SymbolsBySubset.TryGetValue(subset, out var symbols)
            ? symbols
            : SymbolsBySubset[SubsetChoices[0]];

    public static IReadOnlyList<string> GetSubsetNames() => SubsetChoices;

    public static IReadOnlyList<SpecialCharacter> GetSpecialCharacters() => SpecialCharacters;

    public static bool TryParseCharacterCode(string text, out string symbol)
    {
        symbol = "";
        var normalized = text.Trim();
        if (normalized.StartsWith("U+", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[2..];

        if (normalized.Length == 0 || !int.TryParse(normalized, System.Globalization.NumberStyles.HexNumber, null, out var codePoint))
            return false;

        if (!Rune.IsValid(codePoint) || (codePoint >= 0xD800 && codePoint <= 0xDFFF))
            return false;

        symbol = char.ConvertFromUtf32(codePoint);
        return true;
    }

    public static IReadOnlyList<string> PromoteRecentSymbol(IEnumerable<string> currentSymbols, string selectedSymbol, int capacity = 8)
    {
        if (string.IsNullOrEmpty(selectedSymbol) || capacity <= 0)
            return [];

        return currentSymbols
            .Where(symbol => !string.Equals(symbol, selectedSymbol, StringComparison.Ordinal))
            .Prepend(selectedSymbol)
            .Take(capacity)
            .ToArray();
    }

    private static string ToCodeText(string value) =>
        value.EnumerateRunes().FirstOrDefault().Value.ToString("X4");
}
