using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

public sealed partial class NativeJsonAdapter
{
    private static void LoadConditionalFormats(Sheet sheet, IEnumerable<ConditionalFormatDto>? formats)
    {
        foreach (var formatDto in formats ?? [])
        {
            if (string.IsNullOrWhiteSpace(formatDto?.AppliesTo))
                continue;
            if (!IsSupportedConditionalFormat(formatDto))
                continue;

            try
            {
                sheet.ConditionalFormats.Add(ToConditionalFormat(formatDto, sheet.Id));
            }
            catch (FormatException)
            {
                // Skip conditional formats with unparseable ranges.
            }
        }
    }

    private static List<ConditionalFormatDto> ToConditionalFormatDtos(
        IEnumerable<ConditionalFormat> formats,
        SheetId sheetId) =>
        formats
            .Where(format =>
                format.AppliesTo.Start.Sheet == sheetId &&
                format.AppliesTo.End.Sheet == sheetId &&
                IsSupportedConditionalFormat(format))
            .Select(FromConditionalFormat)
            .ToList();

    private static ConditionalFormat ToConditionalFormat(ConditionalFormatDto formatDto, SheetId sheetId)
    {
        var format = new ConditionalFormat
        {
            AppliesTo = GridRange.Parse(formatDto.AppliesTo!, sheetId),
            Priority = formatDto.Priority < 1 ? 1 : formatDto.Priority,
            RuleType = formatDto.RuleType,
            Operator = formatDto.Operator,
            Value1 = formatDto.Value1,
            Value2 = formatDto.Value2,
            FormatIfTrue = ToCellStyle(formatDto.FormatIfTrue),
            MinColor = formatDto.MinColor,
            MidColor = formatDto.MidColor,
            MaxColor = formatDto.MaxColor,
            UseThreeColorScale = formatDto.UseThreeColorScale,
            MinThresholdGreaterThanOrEqual = formatDto.MinThresholdGreaterThanOrEqual,
            MidThresholdGreaterThanOrEqual = formatDto.MidThresholdGreaterThanOrEqual,
            MaxThresholdGreaterThanOrEqual = formatDto.MaxThresholdGreaterThanOrEqual,
            DataBarColor = formatDto.DataBarColor,
            DataBarMinThresholdType = ValidCfThresholdTypeOrDefault(formatDto.DataBarMinThresholdType, CfThresholdType.Min),
            DataBarMinThresholdValue = formatDto.DataBarMinThresholdValue,
            DataBarMaxThresholdType = ValidCfThresholdTypeOrDefault(formatDto.DataBarMaxThresholdType, CfThresholdType.Max),
            DataBarMaxThresholdValue = formatDto.DataBarMaxThresholdValue,
            DataBarShowValue = formatDto.DataBarShowValue,
            DataBarMinLength = ValidDataBarLengthOrNull(formatDto.DataBarMinLength),
            DataBarMaxLength = ValidDataBarLengthOrNull(formatDto.DataBarMaxLength),
            DataBarGradient = formatDto.DataBarGradient,
            DataBarBorder = formatDto.DataBarBorder,
            DataBarAxisPosition = formatDto.DataBarAxisPosition,
            DataBarAxisColor = formatDto.DataBarAxisColor,
            DataBarNegativeFillColor = formatDto.DataBarNegativeFillColor,
            DataBarNegativeBorderColor = formatDto.DataBarNegativeBorderColor,
            AboveAverage = formatDto.AboveAverage,
            FormulaText = formatDto.FormulaText,
            IconSetStyle = formatDto.IconSetStyle,
            IconSetShowValue = formatDto.IconSetShowValue,
            IconSetReverse = formatDto.IconSetReverse,
            TopBottomRank = ValidTopBottomRankOrDefault(formatDto.TopBottomRank),
            TopBottomPercent = formatDto.TopBottomPercent,
            TextRuleText = formatDto.TextRuleText,
            DateOccurringPeriod = ValidDateOccurringPeriodOrDefault(formatDto.DateOccurringPeriod),
            StopIfTrue = formatDto.StopIfTrue,
            NativeAttributes = formatDto.NativeAttributes,
            NativeChildXmls = formatDto.NativeChildXmls,
            NativePayloadAttributes = formatDto.NativePayloadAttributes,
            NativePayloadChildXmls = formatDto.NativePayloadChildXmls,
            NativeContainerAttributes = formatDto.NativeContainerAttributes,
            NativeContainerChildXmls = formatDto.NativeContainerChildXmls
        };
        format.IconSetThresholds.AddRange((formatDto.IconSetThresholds ?? [])
            .Where(threshold => Enum.IsDefined(threshold.Type)));
        format.IconOverrides.AddRange((formatDto.IconOverrides ?? []).Where(IsValidCfIconOverride));
        return format;
    }

    private static ConditionalFormatDto FromConditionalFormat(ConditionalFormat format) =>
        new()
        {
            AppliesTo = format.AppliesTo.ToString(),
            Priority = format.Priority < 1 ? 1 : format.Priority,
            RuleType = format.RuleType,
            Operator = format.Operator,
            Value1 = format.Value1,
            Value2 = format.Value2,
            FormatIfTrue = FromCellStyle(format.FormatIfTrue),
            MinColor = format.MinColor,
            MidColor = format.MidColor,
            MaxColor = format.MaxColor,
            UseThreeColorScale = format.UseThreeColorScale,
            MinThresholdGreaterThanOrEqual = format.MinThresholdGreaterThanOrEqual,
            MidThresholdGreaterThanOrEqual = format.MidThresholdGreaterThanOrEqual,
            MaxThresholdGreaterThanOrEqual = format.MaxThresholdGreaterThanOrEqual,
            DataBarColor = format.DataBarColor,
            DataBarMinThresholdType = ValidCfThresholdTypeOrDefault(format.DataBarMinThresholdType, CfThresholdType.Min),
            DataBarMinThresholdValue = format.DataBarMinThresholdValue,
            DataBarMaxThresholdType = ValidCfThresholdTypeOrDefault(format.DataBarMaxThresholdType, CfThresholdType.Max),
            DataBarMaxThresholdValue = format.DataBarMaxThresholdValue,
            DataBarShowValue = format.DataBarShowValue,
            DataBarMinLength = ValidDataBarLengthOrNull(format.DataBarMinLength),
            DataBarMaxLength = ValidDataBarLengthOrNull(format.DataBarMaxLength),
            DataBarGradient = format.DataBarGradient,
            DataBarBorder = format.DataBarBorder,
            DataBarAxisPosition = format.DataBarAxisPosition,
            DataBarAxisColor = format.DataBarAxisColor,
            DataBarNegativeFillColor = format.DataBarNegativeFillColor,
            DataBarNegativeBorderColor = format.DataBarNegativeBorderColor,
            AboveAverage = format.AboveAverage,
            FormulaText = format.FormulaText,
            IconSetStyle = format.IconSetStyle,
            IconSetShowValue = format.IconSetShowValue,
            IconSetReverse = format.IconSetReverse,
            IconSetThresholds = [.. format.IconSetThresholds.Where(threshold => Enum.IsDefined(threshold.Type))],
            IconOverrides = [.. format.IconOverrides.Where(IsValidCfIconOverride)],
            TopBottomRank = ValidTopBottomRankOrDefault(format.TopBottomRank),
            TopBottomPercent = format.TopBottomPercent,
            TextRuleText = format.TextRuleText,
            DateOccurringPeriod = ValidDateOccurringPeriodOrDefault(format.DateOccurringPeriod),
            StopIfTrue = format.StopIfTrue,
            NativeAttributes = format.NativeAttributes,
            NativeChildXmls = format.NativeChildXmls,
            NativePayloadAttributes = format.NativePayloadAttributes,
            NativePayloadChildXmls = format.NativePayloadChildXmls,
            NativeContainerAttributes = format.NativeContainerAttributes,
            NativeContainerChildXmls = format.NativeContainerChildXmls
        };

    private static bool IsSupportedConditionalFormat(ConditionalFormat format) =>
        Enum.IsDefined(format.RuleType) && Enum.IsDefined(format.Operator);

    private static bool IsSupportedConditionalFormat(ConditionalFormatDto format) =>
        Enum.IsDefined(format.RuleType) && Enum.IsDefined(format.Operator);

    private static CfThresholdType ValidCfThresholdTypeOrDefault(CfThresholdType value, CfThresholdType fallback) =>
        Enum.IsDefined(value) ? value : fallback;

    private static bool IsValidCfIconOverride(CfIconOverride icon) =>
        !string.IsNullOrWhiteSpace(icon.IconSet) && icon.IconId >= 0;

    private static int? ValidDataBarLengthOrNull(int? value) =>
        value is >= 0 and <= 100 ? value : null;

    private static int ValidTopBottomRankOrDefault(int value) =>
        value is >= 1 and <= 1000 ? value : 10;

    private static string ValidDateOccurringPeriodOrDefault(string? value)
    {
        var normalized = value?.Trim();
        return normalized is "yesterday" or "today" or "tomorrow" or "last7Days" or
            "lastWeek" or "thisWeek" or "nextWeek" or
            "lastMonth" or "thisMonth" or "nextMonth"
            ? normalized
            : "today";
    }
}
