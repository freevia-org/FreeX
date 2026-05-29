using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed partial class ManageConditionalFormatsDialog
{
    public static IReadOnlyList<ConditionalFormat> BuildResultRules(
        IReadOnlyList<ConditionalFormat> sheetRules,
        GridRange? selection,
        bool filterToSelection,
        IReadOnlyList<ConditionalFormat> editedRules)
    {
        if (!filterToSelection || selection is null)
            return Reprioritize(editedRules);

        var result = new List<ConditionalFormat>();
        var insertedEditedRules = false;

        foreach (var rule in sheetRules)
        {
            if (!RangesOverlap(rule.AppliesTo, selection.Value))
            {
                result.Add(rule);
                continue;
            }

            if (insertedEditedRules)
                continue;

            result.AddRange(editedRules);
            insertedEditedRules = true;
        }

        if (!insertedEditedRules)
            result.AddRange(editedRules);

        return Reprioritize(result);
    }

    private static IReadOnlyList<ConditionalFormat> Reprioritize(IReadOnlyList<ConditionalFormat> rules) =>
        rules.Select((rule, index) => CloneWithPriority(rule, index + 1)).ToList();

    private static ConditionalFormat CloneWithPriority(ConditionalFormat src, int priority, Guid? id = null)
    {
        var cf = new ConditionalFormat
        {
            Id = id ?? src.Id,
            AppliesTo = src.AppliesTo,
            Priority = priority,
            RuleType = src.RuleType,
            Operator = src.Operator,
            Value1 = src.Value1,
            Value2 = src.Value2,
            FormatIfTrue = src.FormatIfTrue?.Clone(),
            MinColor = src.MinColor,
            MidColor = src.MidColor,
            MaxColor = src.MaxColor,
            UseThreeColorScale = src.UseThreeColorScale,
            MinThresholdType = src.MinThresholdType,
            MinThresholdValue = src.MinThresholdValue,
            MinThresholdGreaterThanOrEqual = src.MinThresholdGreaterThanOrEqual,
            MidThresholdType = src.MidThresholdType,
            MidThresholdValue = src.MidThresholdValue,
            MidThresholdGreaterThanOrEqual = src.MidThresholdGreaterThanOrEqual,
            MaxThresholdType = src.MaxThresholdType,
            MaxThresholdValue = src.MaxThresholdValue,
            MaxThresholdGreaterThanOrEqual = src.MaxThresholdGreaterThanOrEqual,
            DataBarColor = src.DataBarColor,
            DataBarMinThresholdType = src.DataBarMinThresholdType,
            DataBarMinThresholdValue = src.DataBarMinThresholdValue,
            DataBarMaxThresholdType = src.DataBarMaxThresholdType,
            DataBarMaxThresholdValue = src.DataBarMaxThresholdValue,
            DataBarShowValue = src.DataBarShowValue,
            DataBarMinLength = src.DataBarMinLength,
            DataBarMaxLength = src.DataBarMaxLength,
            DataBarGradient = src.DataBarGradient,
            DataBarBorder = src.DataBarBorder,
            DataBarAxisPosition = src.DataBarAxisPosition,
            DataBarAxisColor = src.DataBarAxisColor,
            DataBarNegativeFillColor = src.DataBarNegativeFillColor,
            DataBarNegativeBorderColor = src.DataBarNegativeBorderColor,
            AboveAverage = src.AboveAverage,
            FormulaText = src.FormulaText,
            IconSetStyle = src.IconSetStyle,
            IconSetShowValue = src.IconSetShowValue,
            IconSetReverse = src.IconSetReverse,
            TopBottomRank = src.TopBottomRank,
            TopBottomPercent = src.TopBottomPercent,
            TextRuleText = src.TextRuleText,
            DateOccurringPeriod = src.DateOccurringPeriod,
            StopIfTrue = src.StopIfTrue,
            NativeAttributes = src.NativeAttributes,
            NativeChildXmls = id.HasValue && id.Value != src.Id
                ? ConditionalFormatNativeMetadata.RemoveX14IdNativeChildXmls(src.NativeChildXmls)
                : src.NativeChildXmls,
            NativePayloadAttributes = src.NativePayloadAttributes,
            NativePayloadChildXmls = src.NativePayloadChildXmls,
            NativeContainerAttributes = src.NativeContainerAttributes,
            NativeContainerChildXmls = src.NativeContainerChildXmls
        };
        cf.IconSetThresholds.AddRange(src.IconSetThresholds);
        cf.IconOverrides.AddRange(src.IconOverrides);
        return cf;
    }

    private static bool RangesOverlap(GridRange a, GridRange b)
    {
        if (a.Start.Sheet != b.Start.Sheet)
            return false;
        return a.Start.Row <= b.End.Row && a.End.Row >= b.Start.Row
            && a.Start.Col <= b.End.Col && a.End.Col >= b.Start.Col;
    }

    public static ConditionalFormatAppliesToRangeSelectionRequest CreateAppliesToRangeSelectionRequest(
        Guid ruleId,
        string currentText) =>
        new(ruleId, currentText.Trim(), CollapseDialog: true);
}
