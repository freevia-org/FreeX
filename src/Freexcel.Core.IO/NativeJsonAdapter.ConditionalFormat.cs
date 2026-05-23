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
            DataBarColor = formatDto.DataBarColor,
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
            DataBarColor = format.DataBarColor,
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
