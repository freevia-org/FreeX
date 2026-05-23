using System.Globalization;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed record PivotValueNumberFormatPreset(string Label, int? NumberFormatId, string FormatCode);

public static class PivotValueFieldSettingsInputParser
{
    public const int DefaultCustomNumberFormatId = 164;

    public static IReadOnlyList<PivotValueNumberFormatPreset> NumberFormatPresets { get; } =
    [
        new("General", null, BuiltInFormat(null)),
        new("Number 0 decimals", 1, BuiltInFormat(1)),
        new("Number", 2, BuiltInFormat(2)),
        new("Comma 0 decimals", 3, BuiltInFormat(3)),
        new("Number with thousands", 4, BuiltInFormat(4)),
        new("Currency 0 decimals", 5, BuiltInFormat(5)),
        new("Currency 0 decimals red negatives", 6, BuiltInFormat(6)),
        new("Currency", 7, BuiltInFormat(7)),
        new("Currency red negatives", 8, BuiltInFormat(8)),
        new("Percentage 0 decimals", 9, BuiltInFormat(9)),
        new("Percentage", 10, BuiltInFormat(10)),
        new("Scientific", 11, BuiltInFormat(11)),
        new("Fraction", 12, BuiltInFormat(12)),
        new("Fraction two digits", 13, BuiltInFormat(13)),
        new("Short Date", 14, BuiltInFormat(14)),
        new("Date", 14, BuiltInFormat(14)),
        new("Long Date", 15, BuiltInFormat(15)),
        new("Day Month", 16, BuiltInFormat(16)),
        new("Month Year", 17, BuiltInFormat(17)),
        new("Time AM/PM", 18, BuiltInFormat(18)),
        new("Time with seconds AM/PM", 19, BuiltInFormat(19)),
        new("Time hours minutes", 20, BuiltInFormat(20)),
        new("Time", 21, BuiltInFormat(21)),
        new("Date Time", 22, BuiltInFormat(22)),
        new("Comma 0 decimals parentheses", 37, BuiltInFormat(37)),
        new("Comma red negatives", 38, BuiltInFormat(38)),
        new("Comma parentheses", 39, BuiltInFormat(39)),
        new("Comma decimals red negatives", 40, BuiltInFormat(40)),
        new("Accounting no symbol 0 decimals", 41, BuiltInFormat(41)),
        new("Accounting 0 decimals", 42, BuiltInFormat(42)),
        new("Accounting no symbol", 43, BuiltInFormat(43)),
        new("Accounting", 44, BuiltInFormat(44)),
        new("Elapsed Minutes", 45, BuiltInFormat(45)),
        new("Elapsed Time", 46, BuiltInFormat(46)),
        new("Elapsed Minutes Tenths", 47, BuiltInFormat(47)),
        new("Scientific compact", 48, BuiltInFormat(48)),
        new("Text", 49, BuiltInFormat(49))
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
