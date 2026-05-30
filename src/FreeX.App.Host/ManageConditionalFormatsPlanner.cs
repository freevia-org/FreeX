using FreeX.Core.Model;

namespace FreeX.App.Host;

public enum ConditionalFormatRuleMoveDirection
{
    Up,
    Down
}

public static class ManageConditionalFormatsPlanner
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
        var matchingRuleCount = sheetRules.Count(rule => RangesOverlap(rule.AppliesTo, selection.Value));
        var editedRuleIndex = 0;

        foreach (var rule in sheetRules)
        {
            if (!RangesOverlap(rule.AppliesTo, selection.Value))
            {
                result.Add(rule);
                continue;
            }

            matchingRuleCount--;

            if (editedRuleIndex < editedRules.Count)
                result.Add(editedRules[editedRuleIndex++]);

            if (matchingRuleCount == 0)
            {
                while (editedRuleIndex < editedRules.Count)
                    result.Add(editedRules[editedRuleIndex++]);
            }
        }

        while (editedRuleIndex < editedRules.Count)
            result.Add(editedRules[editedRuleIndex++]);

        return Reprioritize(result);
    }

    public static IReadOnlyList<ConditionalFormat> DuplicateRule(
        IReadOnlyList<ConditionalFormat> rules,
        Guid ruleId,
        Guid? newId = null)
    {
        var result = Reprioritize(rules).ToList();
        var index = result.FindIndex(rule => rule.Id == ruleId);
        if (index < 0)
            return result;

        result.Insert(index + 1, CloneWithPriority(result[index], index + 2, newId ?? Guid.NewGuid()));
        return Reprioritize(result);
    }

    public static IReadOnlyList<ConditionalFormat> ReplaceRule(
        IReadOnlyList<ConditionalFormat> rules,
        ConditionalFormat editedRule)
    {
        var result = Reprioritize(rules).ToList();
        var index = result.FindIndex(rule => rule.Id == editedRule.Id);
        if (index < 0)
            return result;

        result[index] = CloneWithPriority(editedRule, index + 1);
        return Reprioritize(result);
    }

    public static IReadOnlyList<ConditionalFormat> DeleteRule(
        IReadOnlyList<ConditionalFormat> rules,
        Guid ruleId)
    {
        return Reprioritize(rules.Where(rule => rule.Id != ruleId).ToList());
    }

    public static IReadOnlyList<ConditionalFormat> MoveRule(
        IReadOnlyList<ConditionalFormat> rules,
        Guid ruleId,
        ConditionalFormatRuleMoveDirection direction)
    {
        var result = Reprioritize(rules).ToList();
        var index = result.FindIndex(rule => rule.Id == ruleId);
        if (index < 0)
            return result;

        var target = direction == ConditionalFormatRuleMoveDirection.Up ? index - 1 : index + 1;
        if (target < 0 || target >= result.Count)
            return result;

        (result[index], result[target]) = (result[target], result[index]);
        return Reprioritize(result);
    }

    public static IReadOnlyList<ConditionalFormat> ApplyRuleRange(
        IReadOnlyList<ConditionalFormat> rules,
        Guid ruleId,
        GridRange range)
    {
        var result = Reprioritize(rules).ToList();
        var index = result.FindIndex(rule => rule.Id == ruleId);
        if (index < 0)
            return result;

        var updated = CloneWithPriority(result[index], index + 1);
        updated.AppliesTo = range;
        result[index] = updated;
        return result;
    }

    public static IReadOnlyList<ConditionalFormat> Reprioritize(IReadOnlyList<ConditionalFormat> rules) =>
        rules.Select((rule, index) => CloneWithPriority(rule, index + 1)).ToList();

    public static ConditionalFormat CloneWithPriority(ConditionalFormat src, int priority, Guid? id = null)
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

    public static bool RangesOverlap(GridRange a, GridRange b)
    {
        if (a.Start.Sheet != b.Start.Sheet)
            return false;

        return a.Start.Row <= b.End.Row && a.End.Row >= b.Start.Row
            && a.Start.Col <= b.End.Col && a.End.Col >= b.Start.Col;
    }
}
