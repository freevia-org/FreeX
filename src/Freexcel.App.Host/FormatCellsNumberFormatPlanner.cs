using Freexcel.Core.Calc;
using Freexcel.Core.Model;
using System.Globalization;

namespace Freexcel.App.Host;

internal sealed record FormatCellsNumberFormatOption(string Category, string Label, string Code, string Preview);

internal static class FormatCellsNumberFormatPlanner
{
    private const int NumberPreviewAccountingWidth = 14;

    public static IReadOnlyList<FormatCellsNumberFormatOption> Options { get; } =
    [
        new("General", "General", "General", "1234.56"),
        new("Number", "Number (#,##0.00)", "#,##0.00", "1,234.56"),
        new("Number", "0", "0", "1235"),
        new("Number", "0.00", "0.00", "1234.56"),
        new("Number", "#,##0", "#,##0", "1,235"),
        new("Number", "#,##0.00", "#,##0.00", "1,234.56"),
        new("Number", "#,##0_);[Red](#,##0)", "#,##0_);[Red](#,##0)", "1,235"),
        new("Number", "#,##0.00_);[Red](#,##0.00)", "#,##0.00_);[Red](#,##0.00)", "1,234.56"),
        new("Currency", "Currency ($#,##0.00)", "$#,##0.00", "$1,234.56"),
        new("Currency", "$#,##0", "$#,##0", "$1,235"),
        new("Currency", "$#,##0.00", "$#,##0.00", "$1,234.56"),
        new("Currency", "$#,##0;[Red]($#,##0)", "$#,##0;[Red]($#,##0)", "$1,235"),
        new("Currency", "$#,##0.00;[Red]($#,##0.00)", "$#,##0.00;[Red]($#,##0.00)", "$1,234.56"),
        new("Accounting", "Accounting ($#,##0.00)", "_($* #,##0.00_);_($* (#,##0.00);_($* \"-\"??_);_(@_)", "$  1,234.56"),
        new("Accounting", "_($* #,##0_);_($* (#,##0);_($* \"-\"_);_(@_)", "_($* #,##0_);_($* (#,##0);_($* \"-\"_);_(@_)", "$  1,235"),
        new("Accounting", "_($* #,##0.00_);_($* (#,##0.00);_($* \"-\"??_);_(@_)", "_($* #,##0.00_);_($* (#,##0.00);_($* \"-\"??_);_(@_)", "$  1,234.56"),
        new("Date", "Date (m/d/yyyy)", "m/d/yyyy", "5/21/2026"),
        new("Date", "m/d/yyyy", "m/d/yyyy", "5/21/2026"),
        new("Date", "d-mmm-yy", "d-mmm-yy", "21-May-26"),
        new("Date", "mmmm d, yyyy", "mmmm d, yyyy", "May 21, 2026"),
        new("Date", "m/d/yy h:mm", "m/d/yy h:mm", "5/21/26 13:30"),
        new("Date", "Long date ([$-F800])", "[$-F800]", "Thursday, May 21, 2026"),
        new("Time", "Time (h:mm AM/PM)", "h:mm AM/PM", "1:30 PM"),
        new("Time", "h:mm AM/PM", "h:mm AM/PM", "1:30 PM"),
        new("Time", "h:mm:ss AM/PM", "h:mm:ss AM/PM", "1:30:00 PM"),
        new("Time", "h:mm", "h:mm", "13:30"),
        new("Time", "h:mm:ss", "h:mm:ss", "13:30:00"),
        new("Time", "[h]:mm:ss", "[h]:mm:ss", "37:30:00"),
        new("Time", "Long time ([$-F400])", "[$-F400]", "1:30:00 PM"),
        new("Percentage", "Percentage (0%)", "0%", "123456%"),
        new("Percentage", "Percentage (0.00%)", "0.00%", "123456.00%"),
        new("Percentage", "0%", "0%", "123456%"),
        new("Percentage", "0.00%", "0.00%", "123456.00%"),
        new("Fraction", "Fraction (# ?/?)", "# ?/?", "1234 1/2"),
        new("Fraction", "# ?/?", "# ?/?", "1234 1/2"),
        new("Fraction", "# ??/??", "# ??/??", "1234 56/100"),
        new("Fraction", "# ?/2", "# ?/2", "1234 1/2"),
        new("Fraction", "# ?/4", "# ?/4", "1234 2/4"),
        new("Scientific", "Scientific (0.00E+00)", "0.00E+00", "1.23E+03"),
        new("Scientific", "0E+00", "0E+00", "1E+03"),
        new("Scientific", "0.00E+00", "0.00E+00", "1.23E+03"),
        new("Text", "Text (@)", "@", "1234.56"),
        new("Text", "@", "@", "1234.56"),
        new("Special", "Zip Code", "00000", "01235"),
        new("Special", "Zip Code + 4", "00000-0000", "01234-5600"),
        new("Special", "Social Security Number", "000-00-0000", "123-45-6789"),
        new("Special", "Phone Number", "[<=9999999]###-####;(###) ###-####", "(123) 456-7890"),
        new("Custom", "General", "General", "1234.56"),
        new("Custom", "#,##0.00", "#,##0.00", "1,234.56"),
        new("Custom", "$#,##0.00", "$#,##0.00", "$1,234.56"),
        new("Custom", "0.00%", "0.00%", "123456.00%"),
        new("Custom", "m/d/yyyy", "m/d/yyyy", "5/21/2026"),
        new("Custom", "h:mm AM/PM", "h:mm AM/PM", "1:30 PM")
    ];

    public static IReadOnlyList<string> Categories { get; } =
    [
        "General",
        "Number",
        "Currency",
        "Accounting",
        "Date",
        "Time",
        "Percentage",
        "Fraction",
        "Scientific",
        "Text",
        "Special",
        "Custom"
    ];

    public static IReadOnlyList<string> NegativeOptions { get; } =
    [
        "-1234.10",
        "[Red] -1234.10",
        "(1234.10)",
        "[Red] (1234.10)"
    ];

    private static readonly IReadOnlyDictionary<string, string> SymbolLabelMap = BuildSymbolLabelMap();

    public static IReadOnlyList<string> Symbols { get; } = SymbolLabelMap.Keys.ToArray();

    public static IReadOnlyList<string> LabelsForCategory(string category) =>
        Options
            .Where(option => option.Category == category)
            .Select(option => option.Label)
            .Distinct()
            .ToArray();

    public static string? ResolveNumberFormat(string text, int selectedIndex)
    {
        var trimmedText = text.Trim();
        if (!string.IsNullOrWhiteSpace(trimmedText))
            return FindOption(trimmedText)?.Code ?? trimmedText;

        return null;
    }

    public static string? ResolveNumberFormat(
        string text,
        int selectedIndex,
        string? category,
        string? decimalPlacesText,
        string? symbol,
        int negativeIndex)
    {
        var baseFormat = ResolveNumberFormat(text, selectedIndex);
        if (baseFormat is null)
            return null;

        var decimals = ParseDecimalPlaces(decimalPlacesText);
        if (decimals is null)
            return baseFormat;

        return category switch
        {
            "Number" => BuildNumberFormat(decimals.Value, negativeIndex),
            "Currency" => BuildCurrencyFormat(decimals.Value, NormalizeSymbol(symbol), negativeIndex),
            "Accounting" => BuildAccountingFormat(decimals.Value, NormalizeSymbol(symbol)),
            "Percentage" => BuildPercentageFormat(decimals.Value),
            "Scientific" => $"0{DecimalPart(decimals.Value)}E+00",
            _ => baseFormat
        };
    }

    public static string? ResolveSelectedNumberFormat(
        string? category,
        string text,
        int selectedIndex,
        string? decimalPlacesText,
        string? symbol,
        int negativeIndex)
    {
        var decimals = SelectedDecimalPlaces(decimalPlacesText);

        return category switch
        {
            "Number" => BuildNumberFormat(decimals, negativeIndex),
            "Currency" => BuildCurrencyFormat(decimals, NormalizeSymbol(symbol), negativeIndex),
            "Accounting" => BuildAccountingFormat(decimals, NormalizeSymbol(symbol)),
            "Percentage" => $"0{DecimalPart(decimals)}%",
            "Scientific" => $"0{DecimalPart(decimals)}E+00",
            _ => ResolveNumberFormat(text, selectedIndex)
        };
    }

    public static int SelectedDecimalPlaces(string? text) =>
        ParseDecimalPlaces(text) ?? 2;

    public static int DecimalPlacesForFormat(string? format)
    {
        if (string.IsNullOrWhiteSpace(format))
            return 2;

        var firstSection = format.Split(';')[0];
        var dotIndex = firstSection.IndexOf('.');
        if (dotIndex < 0)
            return 0;

        var count = 0;
        for (var i = dotIndex + 1; i < firstSection.Length && firstSection[i] is '0' or '#'; i++)
            count++;

        return Math.Clamp(count, 0, 30);
    }

    public static string PreviewForFormat(string? text)
    {
        var format = FindOption(text)?.Code ?? text;
        if (string.IsNullOrWhiteSpace(format))
            return "1234.56";

        try
        {
            if (LooksLikeDateTimeFormat(format))
            {
                var sampleDate = new DateTime(2026, 5, 21, 13, 30, 0).ToOADate();
                return NumberFormatter.Format(new DateTimeValue(sampleDate), format);
            }

            if (HasNumericLayoutDirective(format))
                return NumberFormatter.Format(new NumberValue(1234.56), format, NumberPreviewAccountingWidth);

            if (format.Contains('@', StringComparison.Ordinal))
                return NumberFormatter.Format(new TextValue("Sample"), format);

            return NumberFormatter.Format(new NumberValue(1234.56), format);
        }
        catch
        {
            return FindOption(text)?.Preview ?? "1234.56";
        }
    }

    public static FormatCellsNumberFormatOption? FindOption(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var trimmedText = text.Trim();
        return Options.FirstOrDefault(option =>
            string.Equals(option.Label, trimmedText, StringComparison.OrdinalIgnoreCase)
            || string.Equals(option.Code, trimmedText, StringComparison.OrdinalIgnoreCase));
    }

    private static int? ParseDecimalPlaces(string? text)
    {
        if (!int.TryParse(text?.Trim(), out var decimals))
            return null;

        return Math.Clamp(decimals, 0, 30);
    }

    private static string NormalizeSymbol(string? symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol) || string.Equals(symbol, "None", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        var trimmed = symbol.Trim();
        return SymbolLabelMap.TryGetValue(trimmed, out var mappedSymbol)
            ? mappedSymbol
            : trimmed;
    }

    private static string BuildNumberFormat(int decimals, int negativeIndex)
    {
        var format = $"#,##0{DecimalPart(decimals)}";
        return ApplyNegativeFormat(format, negativeIndex);
    }

    private static string BuildCurrencyFormat(int decimals, string symbol, int negativeIndex)
    {
        var format = $"{symbol}#,##0{DecimalPart(decimals)}";
        return ApplyNegativeFormat(format, negativeIndex);
    }

    private static string BuildAccountingFormat(int decimals, string symbol)
    {
        var decimalPart = DecimalPart(decimals);
        var zeroPadding = decimals > 0 ? new string('?', decimals) : string.Empty;
        var zeroPart = decimals > 0 ? $"\"-\"{zeroPadding}" : "\"-\"";
        return $"_({symbol}* #,##0{decimalPart}_);_({symbol}* (#,##0{decimalPart});_({symbol}* {zeroPart}_);_(@_)";
    }

    private static string BuildPercentageFormat(int decimals) =>
        $"0{DecimalPart(decimals)}%";

    private static string ApplyNegativeFormat(string format, int negativeIndex) =>
        negativeIndex switch
        {
            1 => $"{format};[Red]-{format}",
            2 => $"{format};({format})",
            3 => $"{format};[Red]({format})",
            _ => format
        };

    private static string DecimalPart(int decimals) =>
        decimals > 0 ? "." + new string('0', decimals) : string.Empty;

    private static IReadOnlyDictionary<string, string> BuildSymbolLabelMap()
    {
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        AddSymbolLabel(labels, "$", "$");
        AddSymbolLabel(labels, "EUR", "EUR");
        AddSymbolLabel(labels, "GBP", "GBP");
        AddSymbolLabel(labels, "JPY", "JPY");
        AddSymbolLabel(labels, "\u20ac", "\u20ac");
        AddSymbolLabel(labels, "\u00a3", "\u00a3");
        AddSymbolLabel(labels, "\u00a5", "\u00a5");

        foreach (var cultureName in new[]
        {
            "en-US", "en-GB", "fr-FR", "de-DE", "ja-JP", "zh-CN",
            "en-CA", "fr-CA", "en-AU", "de-CH", "sv-SE", "pl-PL",
            "uk-UA", "hi-IN", "ko-KR", "pt-BR", "es-MX"
        })
        {
            try
            {
                var region = new RegionInfo(cultureName);
                var currencyName = string.IsNullOrWhiteSpace(region.CurrencyNativeName)
                    ? region.CurrencyEnglishName
                    : region.CurrencyNativeName;

                if (!string.IsNullOrWhiteSpace(region.CurrencySymbol)
                    && !string.IsNullOrWhiteSpace(currencyName))
                {
                    AddSymbolLabel(labels, $"{region.CurrencySymbol} {currencyName}", region.CurrencySymbol);
                }

                var culture = CultureInfo.GetCultureInfo(cultureName);
                if (!string.IsNullOrWhiteSpace(region.CurrencySymbol)
                    && !string.IsNullOrWhiteSpace(culture.EnglishName))
                {
                    AddSymbolLabel(labels, $"{region.CurrencySymbol} {culture.EnglishName}", region.CurrencySymbol);
                }
            }
            catch (ArgumentException)
            {
            }
        }

        AddSymbolLabel(labels, "None", string.Empty);
        return labels;
    }

    private static void AddSymbolLabel(Dictionary<string, string> labels, string label, string symbol)
    {
        if (!labels.ContainsKey(label))
            labels.Add(label, symbol);
    }

    private static bool HasNumericLayoutDirective(string format)
    {
        var sections = SplitFormatSections(format);
        var numericSectionCount = Math.Min(sections.Count, 3);
        for (var i = 0; i < numericSectionCount; i++)
        {
            if (SectionHasActiveLayoutDirective(sections[i]) &&
                SectionHasNumericPlaceholder(sections[i]) &&
                !SectionHasTextPlaceholder(sections[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static List<string> SplitFormatSections(string format)
    {
        var sections = new List<string>();
        var section = new System.Text.StringBuilder();
        var inQuote = false;
        var inBracket = false;

        for (var i = 0; i < format.Length; i++)
        {
            var c = format[i];
            if (c == '"' && !inBracket)
            {
                inQuote = !inQuote;
                section.Append(c);
            }
            else if (c == '\\' && !inQuote && i + 1 < format.Length)
            {
                section.Append(c);
                section.Append(format[++i]);
            }
            else if (c == '[' && !inQuote)
            {
                inBracket = true;
                section.Append(c);
            }
            else if (c == ']' && !inQuote)
            {
                inBracket = false;
                section.Append(c);
            }
            else if (c == ';' && !inQuote && !inBracket)
            {
                sections.Add(section.ToString());
                section.Clear();
            }
            else
            {
                section.Append(c);
            }
        }

        sections.Add(section.ToString());
        return sections;
    }

    private static bool SectionHasActiveLayoutDirective(string section)
    {
        var inQuote = false;
        for (var i = 0; i < section.Length; i++)
        {
            var c = section[i];
            if (c == '"')
            {
                inQuote = !inQuote;
                continue;
            }

            if (!inQuote && c == '\\' && i + 1 < section.Length)
            {
                i++;
                continue;
            }

            if (!inQuote && c is '_' or '*')
                return true;
        }

        return false;
    }

    private static bool SectionHasNumericPlaceholder(string section)
    {
        var inQuote = false;
        for (var i = 0; i < section.Length; i++)
        {
            var c = section[i];
            if (c == '"')
            {
                inQuote = !inQuote;
                continue;
            }

            if (!inQuote && c == '\\' && i + 1 < section.Length)
            {
                i++;
                continue;
            }

            if (!inQuote && c is '0' or '#' or '?')
                return true;
        }

        return false;
    }

    private static bool SectionHasTextPlaceholder(string section)
    {
        var inQuote = false;
        for (var i = 0; i < section.Length; i++)
        {
            var c = section[i];
            if (c == '"')
            {
                inQuote = !inQuote;
                continue;
            }

            if (!inQuote && c == '\\' && i + 1 < section.Length)
            {
                i++;
                continue;
            }

            if (!inQuote && c == '@')
                return true;
        }

        return false;
    }

    private static bool LooksLikeDateTimeFormat(string format)
    {
        var lower = format.ToLowerInvariant();
        return lower.Contains('y')
            || lower.Contains("am/pm", StringComparison.Ordinal)
            || lower.Contains("[h]", StringComparison.Ordinal)
            || lower.Contains("h:", StringComparison.Ordinal)
            || lower.Contains(":mm", StringComparison.Ordinal)
            || lower.Contains("m/d", StringComparison.Ordinal)
            || lower.Contains("d-m", StringComparison.Ordinal)
            || lower.Contains("mmmm", StringComparison.Ordinal);
    }
}
