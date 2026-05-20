using System.Globalization;

namespace Freexcel.App.Host;

public sealed record PivotValueNumberFormatPreset(string Label, int? NumberFormatId);

public static class PivotValueFieldSettingsInputParser
{
    public const int DefaultCustomNumberFormatId = 164;

    public static IReadOnlyList<PivotValueNumberFormatPreset> NumberFormatPresets { get; } =
    [
        new("General", null),
        new("Number", 2),
        new("Number with thousands", 4),
        new("Currency", 7),
        new("Accounting", 44),
        new("Short Date", 14),
        new("Date", 14),
        new("Long Date", 15),
        new("Time", 21),
        new("Percentage", 10),
        new("Fraction", 12),
        new("Scientific", 11),
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
