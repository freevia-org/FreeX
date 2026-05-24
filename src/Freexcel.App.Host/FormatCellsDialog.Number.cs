using Freexcel.Core.Calc;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public partial class FormatCellsDialog
{
    private sealed record NumberFormatOption(string Category, string Label, string Code, string Preview);

    private static readonly NumberFormatOption[] NumberFormatOptions =
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
        new("Accounting", "Accounting ($#,##0.00)", "$#,##0.00", "$1,234.56"),
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

    private static readonly string[] NumberCategories =
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

    private static readonly string[] NumberSymbols = ["$", "EUR", "GBP", "JPY", "€", "£", "¥", "None"];

    private static readonly string[] NegativeNumberOptions =
    [
        "-1234.10",
        "[Red] -1234.10",
        "(1234.10)",
        "[Red] (1234.10)"
    ];

    public static string? ResolveNumberFormat(string text, int selectedIndex)
    {
        var trimmedText = text.Trim();
        if (!string.IsNullOrWhiteSpace(trimmedText))
            return FindNumberFormatOption(trimmedText)?.Code ?? trimmedText;

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

    private static int? ParseDecimalPlaces(string? text)
    {
        if (!int.TryParse(text?.Trim(), out var decimals))
            return null;

        return Math.Clamp(decimals, 0, 30);
    }

    private static string NormalizeSymbol(string? symbol) =>
        string.IsNullOrWhiteSpace(symbol) || string.Equals(symbol, "None", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : symbol.Trim();

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

    private static NumberFormatOption? FindNumberFormatOption(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var trimmedText = text.Trim();
        return NumberFormatOptions.FirstOrDefault(option =>
            string.Equals(option.Label, trimmedText, StringComparison.OrdinalIgnoreCase)
            || string.Equals(option.Code, trimmedText, StringComparison.OrdinalIgnoreCase));
    }

    private string BuildSignedNumberFormat(string positivePattern)
    {
        var negativePattern = NumberNegativeNumbersList.SelectedIndex switch
        {
            1 => $"[Red]-{positivePattern}",
            2 => $"({positivePattern})",
            3 => $"[Red]({positivePattern})",
            _ => ""
        };

        return string.IsNullOrEmpty(negativePattern)
            ? positivePattern
            : $"{positivePattern};{negativePattern}";
    }

    private string NumberPattern(int decimals) => $"#,##0{DecimalPattern(decimals)}";

    private string CurrencyPattern(int decimals)
    {
        var symbol = SelectedCurrencySymbol();
        return string.IsNullOrEmpty(symbol)
            ? NumberPattern(decimals)
            : $"{symbol}{NumberPattern(decimals)}";
    }

    private string AccountingPattern(int decimals)
    {
        var symbol = SelectedCurrencySymbol();
        var symbolToken = string.IsNullOrEmpty(symbol) ? "" : symbol;
        var decimalPattern = DecimalPattern(decimals);
        var zeroPadding = decimals > 0 ? "??" : "";

        return $"_({symbolToken}* #,##0{decimalPattern}_);_({symbolToken}* (#,##0{decimalPattern});_({symbolToken}* \"-\"{zeroPadding}_);_(@_)";
    }

    private static string DecimalPattern(int decimals)
        => decimals <= 0 ? "" : "." + new string('0', decimals);

    private static int DecimalPlacesForFormat(string? format)
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

    private static string PreviewForFormat(string? text)
    {
        var format = FindNumberFormatOption(text)?.Code ?? text;
        if (string.IsNullOrWhiteSpace(format))
            return "1234.56";

        try
        {
            if (LooksLikeDateTimeFormat(format))
            {
                var sampleDate = new DateTime(2026, 5, 21, 13, 30, 0).ToOADate();
                return NumberFormatter.Format(new DateTimeValue(sampleDate), format);
            }

            if (format.Contains('@', StringComparison.Ordinal))
                return NumberFormatter.Format(new TextValue("Sample"), format);

            return NumberFormatter.Format(new NumberValue(1234.56), format);
        }
        catch
        {
            return FindNumberFormatOption(text)?.Preview ?? "1234.56";
        }
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
