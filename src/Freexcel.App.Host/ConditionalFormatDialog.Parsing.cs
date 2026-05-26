using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public partial class ConditionalFormatDialog
{
    private static string? BlankToNull(string text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static bool TryParseOptionalPercent(string text, out int? percent)
    {
        percent = null;
        var trimmed = text.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return true;

        if (!int.TryParse(trimmed, out var value) || value is < 0 or > 100)
            return false;

        percent = value;
        return true;
    }

    private static bool TryParseTopBottomRank(string text, out int rank)
    {
        rank = 0;
        return int.TryParse(text.Trim(), out rank) && rank is >= 1 and <= 1000;
    }

    private static string FormatRgb(RgbColor color) =>
        $"{color.R},{color.G},{color.B}";

    private static bool TryParseRgbColor(string text, out RgbColor color)
    {
        color = default;
        if (!ColorInputParser.TryParseRgbColorText(text, out var parsed))
            return false;

        color = new RgbColor(parsed.R, parsed.G, parsed.B);
        return true;
    }

    private static RgbColor? ParseOptionalRgbColor(string text) =>
        string.IsNullOrWhiteSpace(text) ? null
        : TryParseRgbColor(text, out var color) ? color : null;

    private static string? AxisPositionToXmlValue(string? label) =>
        label switch
        {
            "Middle" => "middle",
            "None"   => "none",
            _        => null
        };

    private static string AxisPositionToLabel(string? xmlValue) =>
        xmlValue switch
        {
            "middle" => "Middle",
            "none"   => "None",
            _        => "Automatic"
        };

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
