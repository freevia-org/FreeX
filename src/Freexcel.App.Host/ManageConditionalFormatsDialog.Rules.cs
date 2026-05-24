using Freexcel.Core.Model;

namespace Freexcel.App.Host;

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
            MidThresholdType = src.MidThresholdType,
            MidThresholdValue = src.MidThresholdValue,
            MaxThresholdType = src.MaxThresholdType,
            MaxThresholdValue = src.MaxThresholdValue,
            DataBarColor = src.DataBarColor,
            DataBarMinThresholdType = src.DataBarMinThresholdType,
            DataBarMinThresholdValue = src.DataBarMinThresholdValue,
            DataBarMaxThresholdType = src.DataBarMaxThresholdType,
            DataBarMaxThresholdValue = src.DataBarMaxThresholdValue,
            DataBarShowValue = src.DataBarShowValue,
            DataBarMinLength = src.DataBarMinLength,
            DataBarMaxLength = src.DataBarMaxLength,
            DataBarGradient = src.DataBarGradient,
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
        };
        cf.IconSetThresholds.AddRange(src.IconSetThresholds);
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
