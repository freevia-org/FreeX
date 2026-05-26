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

    private static ConditionalFormat ToConditionalFormat(ConditionalFormatDto formatDto, SheetId sheetId) =>
        new()
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
            DataBarMinThresholdType = formatDto.DataBarMinThresholdType,
            DataBarMinThresholdValue = formatDto.DataBarMinThresholdValue,
            DataBarMaxThresholdType = formatDto.DataBarMaxThresholdType,
            DataBarMaxThresholdValue = formatDto.DataBarMaxThresholdValue,
            DataBarShowValue = formatDto.DataBarShowValue,
            DataBarMinLength = formatDto.DataBarMinLength,
            DataBarMaxLength = formatDto.DataBarMaxLength,
            DataBarGradient = formatDto.DataBarGradient,
            DataBarBorder = formatDto.DataBarBorder,
            DataBarAxisPosition = formatDto.DataBarAxisPosition,
            DataBarAxisColor = formatDto.DataBarAxisColor,
            DataBarNegativeFillColor = formatDto.DataBarNegativeFillColor,
            DataBarNegativeBorderColor = formatDto.DataBarNegativeBorderColor,
            AboveAverage = formatDto.AboveAverage,
            FormulaText = formatDto.FormulaText,
            TopBottomRank = formatDto.TopBottomRank,
            TopBottomPercent = formatDto.TopBottomPercent,
            TextRuleText = formatDto.TextRuleText,
            DateOccurringPeriod = formatDto.DateOccurringPeriod,
            StopIfTrue = formatDto.StopIfTrue,
            NativeAttributes = formatDto.NativeAttributes,
            NativeChildXmls = formatDto.NativeChildXmls,
            NativePayloadAttributes = formatDto.NativePayloadAttributes,
            NativePayloadChildXmls = formatDto.NativePayloadChildXmls,
            NativeContainerAttributes = formatDto.NativeContainerAttributes,
            NativeContainerChildXmls = formatDto.NativeContainerChildXmls
        };

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
            DataBarMinThresholdType = format.DataBarMinThresholdType,
            DataBarMinThresholdValue = format.DataBarMinThresholdValue,
            DataBarMaxThresholdType = format.DataBarMaxThresholdType,
            DataBarMaxThresholdValue = format.DataBarMaxThresholdValue,
            DataBarShowValue = format.DataBarShowValue,
            DataBarMinLength = format.DataBarMinLength,
            DataBarMaxLength = format.DataBarMaxLength,
            DataBarGradient = format.DataBarGradient,
            DataBarBorder = format.DataBarBorder,
            DataBarAxisPosition = format.DataBarAxisPosition,
            DataBarAxisColor = format.DataBarAxisColor,
            DataBarNegativeFillColor = format.DataBarNegativeFillColor,
            DataBarNegativeBorderColor = format.DataBarNegativeBorderColor,
            AboveAverage = format.AboveAverage,
            FormulaText = format.FormulaText,
            TopBottomRank = format.TopBottomRank,
            TopBottomPercent = format.TopBottomPercent,
            TextRuleText = format.TextRuleText,
            DateOccurringPeriod = format.DateOccurringPeriod,
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
}
