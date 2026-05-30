using System.Globalization;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed record PivotValueNumberFormatPreset(string Label, int? NumberFormatId, string FormatCode);

public static class PivotValueFieldSettingsInputParser
{
    public const int DefaultCustomNumberFormatId = 164;

    public static IReadOnlyList<PivotValueNumberFormatPreset> NumberFormatPresets { get; } =
    [
        new(UiText.Get("PivotValueFieldSettings_NumberFormatGeneral"), null, BuiltInFormat(null)),
        new(UiText.Get("PivotValueFieldSettings_NumberFormatNumber0Decimals"), 1, BuiltInFormat(1)),
        new(UiText.Get("PivotValueFieldSettings_NumberFormatNumber"), 2, BuiltInFormat(2)),
        new(UiText.Get("PivotValueFieldSettings_NumberFormatComma0Decimals"), 3, BuiltInFormat(3)),
        new(UiText.Get("PivotValueFieldSettings_NumberFormatNumberWithThousands"), 4, BuiltInFormat(4)),
        new(UiText.Get("PivotValueFieldSettings_NumberFormatCurrency0Decimals"), 5, BuiltInFormat(5)),
        new(UiText.Get("PivotValueFieldSettings_NumberFormatCurrency0DecimalsRedNegatives"), 6, BuiltInFormat(6)),
        new(UiText.Get("PivotValueFieldSettings_NumberFormatCurrency"), 7, BuiltInFormat(7)),
        new(UiText.Get("PivotValueFieldSettings_NumberFormatCurrencyRedNegatives"), 8, BuiltInFormat(8)),
        new(UiText.Get("PivotValueFieldSettings_NumberFormatPercentage0Decimals"), 9, BuiltInFormat(9)),
        new(UiText.Get("PivotValueFieldSettings_NumberFormatPercentage"), 10, BuiltInFormat(10)),
        new(UiText.Get("PivotValueFieldSettings_NumberFormatScientific"), 11, BuiltInFormat(11)),
        new(UiText.Get("PivotValueFieldSettings_NumberFormatFraction"), 12, BuiltInFormat(12)),
        new(UiText.Get("PivotValueFieldSettings_NumberFormatFractionTwoDigits"), 13, BuiltInFormat(13)),
        new(UiText.Get("PivotValueFieldSettings_NumberFormatShortDate"), 14, BuiltInFormat(14)),
        new(UiText.Get("PivotValueFieldSettings_NumberFormatDate"), 14, BuiltInFormat(14)),
        new(UiText.Get("PivotValueFieldSettings_NumberFormatLongDate"), 15, BuiltInFormat(15)),
        new(UiText.Get("PivotValueFieldSettings_NumberFormatDayMonth"), 16, BuiltInFormat(16)),
        new(UiText.Get("PivotValueFieldSettings_NumberFormatMonthYear"), 17, BuiltInFormat(17)),
        new(UiText.Get("PivotValueFieldSettings_NumberFormatTimeAmPm"), 18, BuiltInFormat(18)),
        new(UiText.Get("PivotValueFieldSettings_NumberFormatTimeWithSecondsAmPm"), 19, BuiltInFormat(19)),
        new(UiText.Get("PivotValueFieldSettings_NumberFormatTimeHoursMinutes"), 20, BuiltInFormat(20)),
        new(UiText.Get("PivotValueFieldSettings_NumberFormatTime"), 21, BuiltInFormat(21)),
        new(UiText.Get("PivotValueFieldSettings_NumberFormatDateTime"), 22, BuiltInFormat(22)),
        new(UiText.Get("PivotValueFieldSettings_NumberFormatComma0DecimalsParentheses"), 37, BuiltInFormat(37)),
        new(UiText.Get("PivotValueFieldSettings_NumberFormatCommaRedNegatives"), 38, BuiltInFormat(38)),
        new(UiText.Get("PivotValueFieldSettings_NumberFormatCommaParentheses"), 39, BuiltInFormat(39)),
        new(UiText.Get("PivotValueFieldSettings_NumberFormatCommaDecimalsRedNegatives"), 40, BuiltInFormat(40)),
        new(UiText.Get("PivotValueFieldSettings_NumberFormatAccountingNoSymbol0Decimals"), 41, BuiltInFormat(41)),
        new(UiText.Get("PivotValueFieldSettings_NumberFormatAccounting0Decimals"), 42, BuiltInFormat(42)),
        new(UiText.Get("PivotValueFieldSettings_NumberFormatAccountingNoSymbol"), 43, BuiltInFormat(43)),
        new(UiText.Get("PivotValueFieldSettings_NumberFormatAccounting"), 44, BuiltInFormat(44)),
        new(UiText.Get("PivotValueFieldSettings_NumberFormatElapsedMinutes"), 45, BuiltInFormat(45)),
        new(UiText.Get("PivotValueFieldSettings_NumberFormatElapsedTime"), 46, BuiltInFormat(46)),
        new(UiText.Get("PivotValueFieldSettings_NumberFormatElapsedMinutesTenths"), 47, BuiltInFormat(47)),
        new(UiText.Get("PivotValueFieldSettings_NumberFormatScientificCompact"), 48, BuiltInFormat(48)),
        new(UiText.Get("PivotValueFieldSettings_NumberFormatText"), 49, BuiltInFormat(49))
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

        return BuiltInNumberFormatCatalog.TryResolveNumberFormatIdForCode(formatCode, out numberFormatId);
    }

    private static string BuiltInFormat(int? numberFormatId) =>
        BuiltInNumberFormatCatalog.TryResolveFormatCode(numberFormatId, out var formatCode)
            ? formatCode
            : "General";
}
