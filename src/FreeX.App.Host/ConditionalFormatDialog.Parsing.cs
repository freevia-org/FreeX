using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class ConditionalFormatDialog
{
    private static string? BlankToNull(string text) =>
        ConditionalFormatInputParser.BlankToNull(text);

    private static bool TryParseOptionalPercent(string text, out int? percent) =>
        ConditionalFormatInputParser.TryParseOptionalPercent(text, out percent);

    private static bool TryParseTopBottomRank(string text, out int rank) =>
        ConditionalFormatInputParser.TryParseTopBottomRank(text, out rank);

    private static string FormatRgb(RgbColor color) =>
        ConditionalFormatInputParser.FormatRgb(color);

    private static bool TryParseRgbColor(string text, out RgbColor color) =>
        ConditionalFormatInputParser.TryParseRgbColor(text, out color);

    private static RgbColor? ParseOptionalRgbColor(string text) =>
        ConditionalFormatInputParser.ParseOptionalRgbColor(text);

    private static string? AxisPositionToXmlValue(string? label) =>
        label switch
        {
            var value when value == UiText.Get("ConditionalFormatDialog_AxisPosition_Middle") => "middle",
            var value when value == UiText.Get("ConditionalFormatDialog_AxisPosition_None") => "none",
            _        => null
        };

    private static string AxisPositionToLabel(string? xmlValue) =>
        xmlValue switch
        {
            "middle" => UiText.Get("ConditionalFormatDialog_AxisPosition_Middle"),
            "none"   => UiText.Get("ConditionalFormatDialog_AxisPosition_None"),
            _        => UiText.Get("ConditionalFormatDialog_AxisPosition_Automatic")
        };

    private static CfRuleType DuplicateValuesRuleType(string? label) =>
        string.Equals(label, UiText.Get("ConditionalFormatDialog_DuplicateKind_Unique"), StringComparison.OrdinalIgnoreCase)
            ? CfRuleType.UniqueValues
            : CfRuleType.DuplicateValues;

    private static string DatePeriodValue(string? label)
    {
        foreach (var period in DateOccurringPeriods)
        {
            if (period.Label == label)
            {
                return period.Value;
            }
        }

        return "today";
    }

    private static string DatePeriodLabel(string? value)
    {
        foreach (var period in DateOccurringPeriods)
        {
            if (period.Value == value)
            {
                return period.Label;
            }
        }

        return UiText.Get("ConditionalFormatDialog_DatePeriod_Today");
    }

    private static string[] DataBarAxisPositionLabels() =>
        [
            UiText.Get("ConditionalFormatDialog_AxisPosition_Automatic"),
            UiText.Get("ConditionalFormatDialog_AxisPosition_Middle"),
            UiText.Get("ConditionalFormatDialog_AxisPosition_None")
        ];
}
