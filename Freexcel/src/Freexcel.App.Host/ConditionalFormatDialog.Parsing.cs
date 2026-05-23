using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public partial class ConditionalFormatDialog
{
    private static string? BlankToNull(string text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static int? ParseOptionalPercent(string text)
    {
        if (!int.TryParse(text.Trim(), out var value))
            return null;

        return Math.Clamp(value, 0, 100);
    }

    private static int ParseTopBottomRank(string text) =>
        int.TryParse(text.Trim(), out var value)
            ? Math.Clamp(value, 1, 1000)
            : 10;

    private static string FormatRgb(RgbColor color) =>
        $"{color.R},{color.G},{color.B}";

    private static RgbColor ParseRgbOrFallback(string text, RgbColor fallback) =>
        ColorInputParser.TryParseRgbColorText(text, out var color)
            ? new RgbColor(color.R, color.G, color.B)
            : fallback;

    private static CfRuleType DuplicateValuesRuleType(string? label) =>
        string.Equals(label, "Unique", StringComparison.OrdinalIgnoreCase)
            ? CfRuleType.UniqueValues
            : CfRuleType.DuplicateValues;

    private static string DatePeriodValue(string? label) =>
        DateOccurringPeriods.FirstOrDefault(period => period.Label == label) is var period
            && period.Label is not null
                ? period.Value
                : "today";

    private static string DatePeriodLabel(string? value) =>
        DateOccurringPeriods.FirstOrDefault(period => period.Value == value) is var period
            && period.Label is not null
                ? period.Label
                : "Today";
}
