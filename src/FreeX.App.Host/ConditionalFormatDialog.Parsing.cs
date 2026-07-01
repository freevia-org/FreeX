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
        ConditionalFormatDialogCatalog.AxisPositionValueForKey(AxisPositionKeyForLabel(label));

    private static string AxisPositionToLabel(string? xmlValue) =>
        UiText.Get(ConditionalFormatDialogCatalog.AxisPositionKeyForValue(xmlValue));

    private static CfRuleType DuplicateValuesRuleType(string? label) =>
        string.Equals(label, UiText.Get("ConditionalFormatDialog_DuplicateKind_Unique"), StringComparison.OrdinalIgnoreCase)
            ? CfRuleType.UniqueValues
            : CfRuleType.DuplicateValues;

    private static string DatePeriodValue(string? label)
        => ConditionalFormatDialogCatalog.DatePeriodValueForKey(DatePeriodKeyForLabel(label));

    private static string DatePeriodLabel(string? value)
        => UiText.Get(ConditionalFormatDialogCatalog.DatePeriodKeyForValue(value));

    private static string[] DataBarAxisPositionLabels() =>
        ConditionalFormatDialogCatalog.AxisPositionOptions
            .Select(option => UiText.Get(option.LabelKey))
            .ToArray();

    private static string? DatePeriodKeyForLabel(string? label)
    {
        foreach (var option in ConditionalFormatDialogCatalog.DatePeriodOptions)
        {
            if (label == UiText.Get(option.LabelKey))
                return option.LabelKey;
        }

        return null;
    }

    private static string? AxisPositionKeyForLabel(string? label)
    {
        foreach (var option in ConditionalFormatDialogCatalog.AxisPositionOptions)
        {
            if (label == UiText.Get(option.LabelKey))
                return option.LabelKey;
        }

        return null;
    }
}
