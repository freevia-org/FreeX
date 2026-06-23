using System.Globalization;
using System.Text;
using FreeX.App.Services;

namespace FreeX.App.Host;

public sealed partial class SymbolPickerDialog
{
    private static readonly UnicodeSubsetDefinition[] UnicodeSubsets =
    [
        new("Latin-1 Supplement", [new(0x00A1, 0x00FF)]),
        new("Latin Extended-A", [new(0x0100, 0x017F)]),
        new("Spacing Modifier Letters", [new(0x02B0, 0x02FF)]),
        new("Greek and Coptic", [new(0x0370, 0x03FF)]),
        new("Cyrillic", [new(0x0400, 0x04FF)]),
        new("Hebrew", [new(0x0590, 0x05FF)]),
        new("Arabic", [new(0x0600, 0x06FF)]),
        new("Currency Symbols", [new(0x00A2, 0x00A5), new(0x20A0, 0x20CF)]),
        new("Letterlike Symbols", [new(0x2100, 0x214F)]),
        new("Number Forms", [new(0x2150, 0x218F)]),
        new("Arrows", [new(0x2190, 0x21FF), new(0x27F5, 0x27FF)]),
        new("Mathematical Operators", [new(0x2200, 0x22FF)]),
        new("Miscellaneous Technical", [new(0x2300, 0x23FF)]),
        new("Box Drawing", [new(0x2500, 0x257F)]),
        new("Block Elements", [new(0x2580, 0x259F)]),
        new("Geometric Shapes", [new(0x25A0, 0x25FF)]),
        new("Miscellaneous Symbols", [new(0x2600, 0x26FF)]),
        new("Dingbats", [new(0x2700, 0x27BF)]),
        new("Supplemental Arrows", [new(0x2900, 0x297F)])
    ];

    private static readonly string[] SubsetChoices = UnicodeSubsets
        .Select(static subset => subset.Name)
        .ToArray();

    private static readonly string[] CommonSymbols =
    [
        "\u20ac",
        "\u00a3",
        "\u00a5",
        "\u00a9",
        "\u00ae",
        "\u2122",
        "\u00b0",
        "\u00b1",
        "\u2192",
        "\u03c0",
        "\u221e",
        "\u2713"
    ];

    private static readonly IReadOnlyDictionary<string, string> FriendlySymbolNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["\u00a1"] = "Inverted Exclamation Mark",
        ["\u00a2"] = "Cent Sign",
        ["\u00a3"] = "Pound Sign",
        ["\u00a4"] = "Currency Sign",
        ["\u00a5"] = "Yen Sign",
        ["\u00a7"] = "Section Sign",
        ["\u00a9"] = "Copyright Sign",
        ["\u00ae"] = "Registered Sign",
        ["\u00b0"] = "Degree Sign",
        ["\u00b1"] = "Plus-Minus Sign",
        ["\u00b5"] = "Micro Sign",
        ["\u00b6"] = "Pilcrow Sign",
        ["\u00d7"] = "Multiplication Sign",
        ["\u00f7"] = "Division Sign",
        ["\u0394"] = "Greek Capital Letter Delta",
        ["\u03a9"] = "Greek Capital Letter Omega",
        ["\u03b1"] = "Greek Small Letter Alpha",
        ["\u03b2"] = "Greek Small Letter Beta",
        ["\u03bc"] = "Greek Small Letter Mu",
        ["\u03c0"] = "Greek Small Letter Pi",
        ["\u03c3"] = "Greek Small Letter Sigma",
        ["\u20ac"] = "Euro Sign",
        ["\u20b9"] = "Indian Rupee Sign",
        ["\u20ba"] = "Turkish Lira Sign",
        ["\u20bd"] = "Ruble Sign",
        ["\u20bf"] = "Bitcoin Sign",
        ["\u2122"] = "Trademark Sign",
        ["\u2126"] = "Ohm Sign",
        ["\u2190"] = "Left Arrow",
        ["\u2191"] = "Up Arrow",
        ["\u2192"] = "Right Arrow",
        ["\u2193"] = "Down Arrow",
        ["\u21d0"] = "Left Double Arrow",
        ["\u21d2"] = "Right Double Arrow",
        ["\u21d4"] = "Left Right Double Arrow",
        ["\u2202"] = "Partial Differential",
        ["\u2206"] = "Increment",
        ["\u220f"] = "N-Ary Product",
        ["\u2211"] = "N-Ary Summation",
        ["\u2212"] = "Minus Sign",
        ["\u221a"] = "Square Root",
        ["\u221e"] = "Infinity",
        ["\u222b"] = "Integral",
        ["\u2248"] = "Almost Equal To",
        ["\u2260"] = "Not Equal To",
        ["\u2264"] = "Less-Than Or Equal To",
        ["\u2265"] = "Greater-Than Or Equal To",
        ["\u2500"] = "Box Drawings Light Horizontal",
        ["\u2502"] = "Box Drawings Light Vertical",
        ["\u250c"] = "Box Drawings Light Down And Right",
        ["\u25a0"] = "Black Square",
        ["\u25a1"] = "White Square",
        ["\u25b2"] = "Black Up-Pointing Triangle",
        ["\u25bc"] = "Black Down-Pointing Triangle",
        ["\u25c6"] = "Black Diamond",
        ["\u2605"] = "Black Star",
        ["\u2606"] = "White Star",
        ["\u2611"] = "Ballot Box With Check",
        ["\u2713"] = "Check Mark",
        ["\u2717"] = "Ballot X"
    };

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<SymbolCatalogEntry>> SymbolsBySubset = BuildSymbolsBySubset();
    private static readonly IReadOnlyList<SymbolCatalogEntry> AllSymbols = SymbolsBySubset.Values.SelectMany(static symbols => symbols).ToArray();

    private static readonly SpecialCharacter[] SpecialCharacters =
    [
        new("Em Dash", "\u2014"),
        new("En Dash", "\u2013"),
        new("Figure Dash", "\u2012"),
        new("Horizontal Bar", "\u2015"),
        new("Nonbreaking Space", "\u00a0"),
        new("Nonbreaking Hyphen", "\u2011"),
        new("Optional Hyphen", "\u00ad"),
        new("Copyright", "\u00a9"),
        new("Registered", "\u00ae"),
        new("Trademark", "\u2122"),
        new("Section", "\u00a7"),
        new("Paragraph", "\u00b6"),
        new("Ellipsis", "\u2026"),
        new("Degree", "\u00b0"),
        new("Bullet", "\u2022"),
        new("Middle Dot", "\u00b7"),
        new("Single Opening Quote", "\u2018"),
        new("Single Closing Quote", "\u2019"),
        new("Double Opening Quote", "\u201c"),
        new("Double Closing Quote", "\u201d"),
        new("Single Left Angle Quote", "\u2039"),
        new("Single Right Angle Quote", "\u203a"),
        new("Left Double Angle Quote", "\u00ab"),
        new("Right Double Angle Quote", "\u00bb"),
        new("Dagger", "\u2020"),
        new("Double Dagger", "\u2021"),
        new("Per Mille", "\u2030"),
        new("Numero Sign", "\u2116"),
        new("Euro", "\u20ac"),
        new("Pound", "\u00a3"),
        new("Yen", "\u00a5"),
        new("Cent", "\u00a2"),
        new("Plus-Minus", "\u00b1"),
        new("Multiplication", "\u00d7"),
        new("Division", "\u00f7"),
        new("Less-Than Or Equal", "\u2264"),
        new("Greater-Than Or Equal", "\u2265"),
        new("Not Equal", "\u2260"),
        new("Approximately Equal", "\u2248"),
        new("Infinity", "\u221e"),
        new("Micro", "\u00b5"),
        new("Ohm", "\u2126"),
        new("Pi", "\u03c0"),
        new("Check Mark", "\u2713"),
        new("Ballot X", "\u2717")
    ];

    public static IReadOnlyList<string> GetSymbolsForSubset(string subset) =>
        SymbolsBySubset.TryGetValue(subset, out var symbols)
            ? symbols.Select(static symbol => symbol.Symbol).ToArray()
            : GetSymbolsForSubset(SubsetChoices[0]);

    public static IReadOnlyList<SymbolCatalogEntry> GetSymbolEntriesForSubset(string subset) =>
        SymbolsBySubset.TryGetValue(subset, out var symbols)
            ? symbols
            : SymbolsBySubset[SubsetChoices[0]];

    public static IReadOnlyList<SymbolCatalogEntry> SearchSymbolEntries(string searchText)
    {
        var terms = searchText
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length == 0)
            return GetSymbolEntriesForSubset(SubsetChoices[0]);

        return AllSymbols
            .Where(symbol => terms.All(term => symbol.SearchText.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    public static SymbolCatalogEntry CreateSymbolEntry(string symbol, string fallbackSubset)
    {
        var existing = FindSymbolEntry(symbol);
        if (existing is not null)
            return existing.Value;

        var codeText = SymbolPickerSelectionPlanner.FormatCodeText(symbol);
        return new SymbolCatalogEntry(
            symbol,
            string.IsNullOrEmpty(codeText) ? UiText.Get("SymbolPicker_Symbol") : $"Unicode U+{codeText}",
            fallbackSubset,
            codeText);
    }

    public static SymbolCatalogEntry? FindSymbolEntry(string symbol)
    {
        foreach (var entry in AllSymbols)
        {
            if (string.Equals(entry.Symbol, symbol, StringComparison.Ordinal))
                return entry;
        }

        return null;
    }

    public static IReadOnlyList<string> GetSubsetNames() => SubsetChoices;

    public static IReadOnlyList<SpecialCharacter> GetSpecialCharacters() => SpecialCharacters;

    public static bool TryParseCharacterCode(string text, out string symbol)
    {
        symbol = "";
        var normalized = text.Trim();
        if (normalized.StartsWith("U+", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[2..];

        if (normalized.Length == 0 || !int.TryParse(normalized, NumberStyles.HexNumber, null, out var codePoint))
            return false;

        if (!Rune.IsValid(codePoint) || (codePoint >= 0xD800 && codePoint <= 0xDFFF))
            return false;

        symbol = char.ConvertFromUtf32(codePoint);
        return true;
    }

    public static IReadOnlyList<string> PromoteRecentSymbol(IEnumerable<string> currentSymbols, string selectedSymbol, int capacity = 12) =>
        SymbolPickerSelectionPlanner.PromoteRecentSymbol(currentSymbols, selectedSymbol, capacity);

    private static IReadOnlyDictionary<string, IReadOnlyList<SymbolCatalogEntry>> BuildSymbolsBySubset()
    {
        var subsets = new Dictionary<string, IReadOnlyList<SymbolCatalogEntry>>(StringComparer.Ordinal);
        foreach (var subset in UnicodeSubsets)
        {
            subsets[subset.Name] = subset.Ranges
                .SelectMany(static range => Enumerable.Range(range.Start, range.End - range.Start + 1))
                .Distinct()
                .Where(IsDisplayableSymbolCodePoint)
                .Select(codePoint => CreateCatalogEntry(codePoint, subset.Name))
                .ToArray();
        }

        return subsets;
    }

    private static SymbolCatalogEntry CreateCatalogEntry(int codePoint, string subset)
    {
        var symbol = char.ConvertFromUtf32(codePoint);
        var codeText = codePoint.ToString("X4", CultureInfo.InvariantCulture);
        var name = FriendlySymbolNames.TryGetValue(symbol, out var friendlyName)
            ? friendlyName
            : $"{subset} U+{codeText}";
        return new SymbolCatalogEntry(symbol, name, subset, codeText);
    }

    private static bool IsDisplayableSymbolCodePoint(int codePoint)
    {
        if (!Rune.IsValid(codePoint) || (codePoint >= 0xD800 && codePoint <= 0xDFFF))
            return false;

        var symbol = char.ConvertFromUtf32(codePoint);
        return CharUnicodeInfo.GetUnicodeCategory(symbol, 0) is not
            UnicodeCategory.Control and not
            UnicodeCategory.Format and not
            UnicodeCategory.Surrogate and not
            UnicodeCategory.PrivateUse and not
            UnicodeCategory.OtherNotAssigned and not
            UnicodeCategory.NonSpacingMark and not
            UnicodeCategory.SpacingCombiningMark and not
            UnicodeCategory.EnclosingMark and not
            UnicodeCategory.SpaceSeparator and not
            UnicodeCategory.LineSeparator and not
            UnicodeCategory.ParagraphSeparator;
    }

    private sealed record UnicodeRange(int Start, int End);

    private sealed record UnicodeSubsetDefinition(string Name, IReadOnlyList<UnicodeRange> Ranges);
}
