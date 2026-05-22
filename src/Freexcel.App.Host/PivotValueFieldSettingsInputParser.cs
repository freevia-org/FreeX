using System.Globalization;

namespace Freexcel.App.Host;

public sealed record PivotValueNumberFormatPreset(string Label, int? NumberFormatId, string FormatCode);

public static class PivotValueFieldSettingsInputParser
{
    public const int DefaultCustomNumberFormatId = 164;

    public static IReadOnlyList<PivotValueNumberFormatPreset> NumberFormatPresets { get; } =
    [
        new("General", null, "General"),
        new("Number 0 decimals", 1, "0"),
        new("Number", 2, "0.00"),
        new("Comma 0 decimals", 3, "#,##0"),
        new("Number with thousands", 4, "#,##0.00"),
        new("Currency 0 decimals", 5, "$#,##0"),
        new("Currency 0 decimals red negatives", 6, "$#,##0;[Red]($#,##0)"),
        new("Currency", 7, "$#,##0.00"),
        new("Currency red negatives", 8, "$#,##0.00;[Red]($#,##0.00)"),
        new("Percentage 0 decimals", 9, "0%"),
        new("Percentage", 10, "0.00%"),
        new("Scientific", 11, "0.00E+00"),
        new("Fraction", 12, "# ?/?"),
        new("Fraction two digits", 13, "# ??/??"),
        new("Short Date", 14, "m/d/yy"),
        new("Date", 14, "m/d/yy"),
        new("Long Date", 15, "d-mmm-yy"),
        new("Day Month", 16, "d-mmm"),
        new("Month Year", 17, "mmm-yy"),
        new("Time AM/PM", 18, "h:mm AM/PM"),
        new("Time with seconds AM/PM", 19, "h:mm:ss AM/PM"),
        new("Time hours minutes", 20, "h:mm"),
        new("Time", 21, "h:mm:ss"),
        new("Date Time", 22, "m/d/yy h:mm"),
        new("Comma 0 decimals parentheses", 37, "#,##0;(#,##0)"),
        new("Comma red negatives", 38, "#,##0;[Red](#,##0)"),
        new("Comma parentheses", 39, "#,##0.00;(#,##0.00)"),
        new("Comma decimals red negatives", 40, "#,##0.00;[Red](#,##0.00)"),
        new("Accounting", 44, "_($* #,##0.00_);_($* (#,##0.00);_($* \"-\"??_);_(@_)"),
        new("Elapsed Minutes", 45, "mm:ss"),
        new("Elapsed Time", 46, "[h]:mm:ss"),
        new("Elapsed Minutes Tenths", 47, "mm:ss.0"),
        new("Scientific compact", 48, "##0.0E+0"),
        new("Text", 49, "@")
    ];

    public static bool TryParseOptionalNumberFormatId(string input, out int? numberFormatId)
    {
        numberFormatId = null;
        var trimmed = input.Trim();
        if (trimmed.Length == 0)
            return true;

        if (!int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            return false;

        numberFormatId = parsed;
        return true;
    }

    public static string? ResolveOptionalNumberFormatCode(string input)
    {
        var trimmed = input.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    public static int? ResolveNumberFormatIdForCode(int? numberFormatId, string? numberFormatCode)
    {
        if (string.IsNullOrWhiteSpace(numberFormatCode))
            return numberFormatId;

        return numberFormatId is >= DefaultCustomNumberFormatId
            ? numberFormatId
            : DefaultCustomNumberFormatId;
    }

    public static int? ResolvePresetNumberFormatId(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return null;

        return NumberFormatPresets
            .FirstOrDefault(preset => string.Equals(preset.Label, label.Trim(), StringComparison.OrdinalIgnoreCase))
            ?.NumberFormatId;
    }

    public static string? ResolvePresetNumberFormatCode(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return null;

        return NumberFormatPresets
            .FirstOrDefault(preset => string.Equals(preset.Label, label.Trim(), StringComparison.OrdinalIgnoreCase))
            ?.FormatCode;
    }

    public static int? ResolveBuiltInNumberFormatIdForCode(string? formatCode) =>
        TryResolveBuiltInNumberFormatIdForCode(formatCode, out var numberFormatId)
            ? numberFormatId
            : null;

    public static bool TryResolveBuiltInNumberFormatIdForCode(string? formatCode, out int? numberFormatId)
    {
        numberFormatId = null;
        if (string.IsNullOrWhiteSpace(formatCode))
            return false;

        var preset = NumberFormatPresets
            .FirstOrDefault(preset => string.Equals(preset.FormatCode, formatCode.Trim(), StringComparison.OrdinalIgnoreCase));
        if (preset is null)
            return false;

        numberFormatId = preset.NumberFormatId;
        return true;
    }
}
