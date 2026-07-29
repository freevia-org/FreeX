using System.Globalization;
using System.Text;

namespace FreeX.App.Presentation.Dialogs;

public readonly record struct SymbolPickerCatalogEntry(string Symbol, string Name, string Subset, string CodeText)
{
    public string SearchText => $"{Symbol} {Name} {Subset} U+{CodeText}";
    public string ToolTipText => $"{Name} (U+{CodeText})";
}

public readonly record struct SymbolPickerSpecialCharacter(string Name, string Symbol, string Shortcut = "")
{
    public string CodeText => SymbolPickerCatalogPlanner.FormatCodeText(Symbol);
    public string DisplaySymbol => SymbolPickerCatalogPlanner.CreateDisplaySymbol(Symbol);
    public string SearchText => $"{Name} {Symbol} {DisplaySymbol} {Shortcut} U+{CodeText}";
}

public sealed record SymbolPickerSelectionPlan(string Symbol, char SelectedChar, string CodeText);

public sealed record SymbolPickerSymbolListPlan(
    IReadOnlyList<SymbolPickerCatalogEntry> Entries,
    SymbolPickerCatalogEntry? SelectedEntry,
    bool HasResults);

/// <summary>
/// Portable catalog and selection policy for Insert Symbol. The shell owns the visuals; this planner owns
/// the Unicode subsets, default/recent symbols, filtering, character-code parsing, and selected result
/// state so other shells can render the same picker without cloning catalog logic.
/// </summary>
public static class SymbolPickerCatalogPlanner
{
    public const double DialogWidth = 840;
    public const double DialogHeight = 620;
    public const string DefaultSubsetName = "Latin-1 Supplement";
    public const int DefaultRecentSymbolCapacity = 12;
    public const string GenericSymbolName = "Symbol";

    private static readonly UnicodeSubsetDefinition[] UnicodeSubsets =
    [
        new(DefaultSubsetName, [new(0x00A1, 0x00FF)]),
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

    private static readonly string[] PreferredFontChoicesValue =
    [
        "Segoe UI Symbol",
        "Segoe UI Emoji",
        "Segoe UI Historic",
        "Segoe UI",
        "Calibri",
        "Cambria Math",
        "Arial",
        "Times New Roman",
        "Courier New",
        "Consolas",
        "Symbol",
        "Wingdings",
        "Wingdings 2",
        "Wingdings 3",
        "Webdings"
    ];

    // R91-commands-insert-object-5-3: these are the "Symbol charset" dingbat fonts whose glyphs
    // are not part of the Unicode symbol tables above -- choosing one of them must swap the
    // catalog to that font's own glyph set rather than leave the fixed Unicode table on screen.
    // "Symbol" itself is deliberately excluded: it is also used as GenericSymbolName for unknown
    // Unicode code points elsewhere in this class, and its glyphs mostly already exist as ordinary
    // Unicode (Greek/math) code points in the tables above, unlike the four private-use dingbat fonts.
    private static readonly string[] SymbolFontChoicesValue =
    [
        "Wingdings",
        "Wingdings 2",
        "Wingdings 3",
        "Webdings"
    ];

    // Windows maps a "Symbol charset" TrueType font's raw byte codes (0x20-0xFF) into the Basic
    // Multilingual Plane's Private Use Area starting at U+F000 so the glyphs can round-trip as
    // ordinary Unicode text tagged with that font (this is also how Excel/OOXML <rFont>-tagged
    // runs for Wingdings/Webdings persist their characters, e.g. U+F0FC for a Wingdings check mark).
    private const int SymbolFontPrivateUseBase = 0xF000;
    private const int SymbolFontCodeRangeStart = 0x20;
    private const int SymbolFontCodeRangeEnd = 0xFF;

    private static readonly string[] DefaultRecentSymbolsValue =
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

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<SymbolPickerCatalogEntry>> SymbolsBySubset = BuildSymbolsBySubset();
    private static readonly IReadOnlyList<SymbolPickerCatalogEntry> AllSymbols = SymbolsBySubset.Values.SelectMany(static symbols => symbols).ToArray();

    private static readonly SymbolPickerSpecialCharacter[] SpecialCharacters =
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

    public static IReadOnlyList<string> DefaultRecentSymbols => DefaultRecentSymbolsValue;

    public static IReadOnlyList<string> GetPreferredFontChoices() => PreferredFontChoicesValue;

    public static IReadOnlyList<string> GetSubsetNames() => SubsetChoices;

    public static IReadOnlyList<string> GetSymbolsForSubset(string? subset) =>
        GetSymbolEntriesForSubset(subset)
            .Select(static symbol => symbol.Symbol)
            .ToArray();

    public static IReadOnlyList<SymbolPickerCatalogEntry> GetSymbolEntriesForSubset(string? subset) =>
        SymbolsBySubset.TryGetValue(NormalizeSubset(subset), out var symbols)
            ? symbols
            : SymbolsBySubset[DefaultSubsetName];

    public static IReadOnlyList<SymbolPickerCatalogEntry> SearchSymbolEntries(string? searchText)
    {
        var terms = (searchText ?? "")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length == 0)
            return GetSymbolEntriesForSubset(DefaultSubsetName);

        return AllSymbols
            .Where(symbol => terms.All(term => symbol.SearchText.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    /// <summary>
    /// True when <paramref name="fontName"/> is a "Symbol charset" dingbat font (Wingdings/Webdings
    /// family) whose glyph set is not part of the Unicode subset tables and must replace them in
    /// the catalog rather than merely change the display typeface.
    /// </summary>
    public static bool IsSymbolFont(string? fontName) =>
        !string.IsNullOrWhiteSpace(fontName) &&
        SymbolFontChoicesValue.Contains(fontName.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Builds the full glyph-code catalog for a Symbol-charset dingbat font (see
    /// <see cref="IsSymbolFont"/>), using the same Private Use Area codepoints Windows/OOXML use
    /// to represent that font's characters as Unicode text. Empty when <paramref name="fontName"/>
    /// is not a recognized symbol font.
    /// </summary>
    public static IReadOnlyList<SymbolPickerCatalogEntry> GetSymbolFontEntries(string? fontName)
    {
        if (!IsSymbolFont(fontName))
            return [];

        var name = fontName!.Trim();
        var entries = new List<SymbolPickerCatalogEntry>(SymbolFontCodeRangeEnd - SymbolFontCodeRangeStart + 1);
        for (var rawCode = SymbolFontCodeRangeStart; rawCode <= SymbolFontCodeRangeEnd; rawCode++)
        {
            var codePoint = SymbolFontPrivateUseBase + rawCode;
            var symbol = char.ConvertFromUtf32(codePoint);
            var codeText = codePoint.ToString("X4", CultureInfo.InvariantCulture);
            entries.Add(new SymbolPickerCatalogEntry(symbol, $"{name} Character 0x{rawCode:X2}", name, codeText));
        }

        return entries;
    }

    public static SymbolPickerSymbolListPlan PlanSymbolList(string? subset, string? searchText, string? selectedSymbol, string? fontName = null)
    {
        var query = searchText?.Trim() ?? "";
        IReadOnlyList<SymbolPickerCatalogEntry> entries;
        if (IsSymbolFont(fontName))
        {
            var fontEntries = GetSymbolFontEntries(fontName);
            entries = query.Length == 0 ? fontEntries : FilterEntries(fontEntries, query);
        }
        else
        {
            entries = query.Length == 0
                ? GetSymbolEntriesForSubset(subset)
                : SearchSymbolEntries(query);
        }

        var selectedEntry = FindEntry(entries, selectedSymbol);
        if (selectedEntry is null && entries.Count > 0)
            selectedEntry = entries[0];

        return new SymbolPickerSymbolListPlan(entries, selectedEntry, entries.Count > 0);
    }

    private static IReadOnlyList<SymbolPickerCatalogEntry> FilterEntries(
        IReadOnlyList<SymbolPickerCatalogEntry> entries,
        string query)
    {
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length == 0)
            return entries;

        return entries
            .Where(symbol => terms.All(term => symbol.SearchText.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    public static SymbolPickerSelectionPlan CreateDefaultSelection() =>
        CreateSelection(GetSymbolsForSubset(DefaultSubsetName).FirstOrDefault() ?? "");

    public static SymbolPickerSelectionPlan CreateSelection(string? symbol)
    {
        var safeSymbol = symbol ?? "";
        return new SymbolPickerSelectionPlan(
            safeSymbol,
            safeSymbol.Length == 1 ? safeSymbol[0] : '\0',
            FormatCodeText(safeSymbol));
    }

    public static SymbolPickerCatalogEntry CreateSymbolEntry(string symbol, string fallbackSubset)
    {
        var existing = FindSymbolEntry(symbol);
        if (existing is not null)
            return existing.Value;

        var codeText = FormatCodeText(symbol);
        return new SymbolPickerCatalogEntry(
            symbol,
            string.IsNullOrEmpty(codeText) ? GenericSymbolName : $"Unicode U+{codeText}",
            fallbackSubset,
            codeText);
    }

    public static SymbolPickerCatalogEntry? FindSymbolEntry(string? symbol)
    {
        foreach (var entry in AllSymbols)
        {
            if (string.Equals(entry.Symbol, symbol, StringComparison.Ordinal))
                return entry;
        }

        return null;
    }

    public static IReadOnlyList<SymbolPickerSpecialCharacter> GetSpecialCharacters() => SpecialCharacters;

    public static bool TryParseCharacterCode(string? text, out string symbol)
    {
        symbol = "";
        var normalized = (text ?? "").Trim();
        if (normalized.StartsWith("U+", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[2..];

        if (normalized.Length == 0 || !int.TryParse(normalized, NumberStyles.HexNumber, null, out var codePoint))
            return false;

        if (!Rune.IsValid(codePoint) || (codePoint >= 0xD800 && codePoint <= 0xDFFF))
            return false;

        symbol = char.ConvertFromUtf32(codePoint);
        return true;
    }

    public static IReadOnlyList<string> PromoteRecentSymbol(
        IEnumerable<string> currentSymbols,
        string selectedSymbol,
        int capacity = DefaultRecentSymbolCapacity)
    {
        if (string.IsNullOrEmpty(selectedSymbol) || capacity <= 0)
            return [];

        return currentSymbols
            .Where(symbol => !string.Equals(symbol, selectedSymbol, StringComparison.Ordinal))
            .Prepend(selectedSymbol)
            .Take(capacity)
            .ToArray();
    }

    public static string FormatCodeText(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        foreach (var rune in value.EnumerateRunes())
            return rune.Value.ToString("X4", CultureInfo.InvariantCulture);

        return "";
    }

    public static string CreateDisplaySymbol(string value) =>
        value switch
        {
            "\u00a0" => "NBSP",
            "\u00ad" => "SHY",
            _ => value
        };

    private static string NormalizeSubset(string? subset) =>
        string.IsNullOrWhiteSpace(subset) ? DefaultSubsetName : subset.Trim();

    private static SymbolPickerCatalogEntry? FindEntry(
        IReadOnlyList<SymbolPickerCatalogEntry> entries,
        string? symbol)
    {
        if (string.IsNullOrEmpty(symbol))
            return null;

        foreach (var entry in entries)
        {
            if (string.Equals(entry.Symbol, symbol, StringComparison.Ordinal))
                return entry;
        }

        return null;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<SymbolPickerCatalogEntry>> BuildSymbolsBySubset()
    {
        var subsets = new Dictionary<string, IReadOnlyList<SymbolPickerCatalogEntry>>(StringComparer.Ordinal);
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

    private static SymbolPickerCatalogEntry CreateCatalogEntry(int codePoint, string subset)
    {
        var symbol = char.ConvertFromUtf32(codePoint);
        var codeText = codePoint.ToString("X4", CultureInfo.InvariantCulture);
        var name = FriendlySymbolNames.TryGetValue(symbol, out var friendlyName)
            ? friendlyName
            : $"{subset} U+{codeText}";
        return new SymbolPickerCatalogEntry(symbol, name, subset, codeText);
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
