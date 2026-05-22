using System.Globalization;

namespace Freexcel.App.Host;

public sealed record PivotValueNumberFormatPreset(string Label, int? NumberFormatId);

public static class PivotValueFieldSettingsInputParser
{
    public const int DefaultCustomNumberFormatId = 164;

    public static IReadOnlyList<PivotValueNumberFormatPreset> NumberFormatPresets { get; } =
    [
        new("General", null),
        new("Number 0 decimals", 1),
        new("Number", 2),
        new("Comma 0 decimals", 3),
        new("Number with thousands", 4),
        new("Currency 0 decimals", 5),
        new("Currency 0 decimals red negatives", 6),
        new("Currency", 7),
        new("Currency red negatives", 8),
        new("Percentage 0 decimals", 9),
        new("Percentage", 10),
        new("Scientific", 11),
        new("Fraction", 12),
        new("Fraction two digits", 13),
        new("Short Date", 14),
        new("Date", 14),
        new("Long Date", 15),
        new("Day Month", 16),
        new("Month Year", 17),
        new("Time AM/PM", 18),
        new("Time with seconds AM/PM", 19),
        new("Time hours minutes", 20),
        new("Time", 21),
        new("Date Time", 22),
        new("Comma 0 decimals parentheses", 37),
        new("Comma red negatives", 38),
        new("Comma parentheses", 39),
        new("Comma decimals red negatives", 40),
        new("Accounting", 44),
        new("Elapsed Minutes", 45),
        new("Elapsed Time", 46),
        new("Elapsed Minutes Tenths", 47),
        new("Scientific compact", 48),
        new("Text", 49)
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
}
