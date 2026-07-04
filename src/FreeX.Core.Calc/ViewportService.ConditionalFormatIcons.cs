using FreeX.Core.Model;

namespace FreeX.Core.Calc;

public sealed partial class ViewportService
{
    private static ConditionalFormatIcon? EvaluateConditionalIcon(
        Sheet sheet,
        CellAddress addr,
        ScalarValue value,
        Workbook workbook,
        CfEvaluationContext cfContext)
    {
        for (var i = 0; i < cfContext.IconRulesByPriority.Count; i++)
        {
            var rule = cfContext.IconRulesByPriority[i];
            if (!rule.AllRanges.Any(r => r.Contains(addr)))
                continue;
            if (!TryGetDouble(value, out var cellValue))
                return null;

            var style = string.IsNullOrWhiteSpace(rule.IconSetStyle) ? "3TrafficLights1" : rule.IconSetStyle!;
            var iconCount = ViewportConditionalFormatEvaluator.GetIconSetCount(style);
            cfContext.Aggregates.TryGetValue(rule, out var cache);
            var bucketIndex = ResolveIconSetIndex(rule, cellValue, cache, sheet, workbook, addr, iconCount, cfContext);
            if (!bucketIndex.HasValue)
                return null;

            if (rule.IconOverrides.Count == iconCount)
            {
                var ovr = rule.IconOverrides[bucketIndex.Value];
                return new ConditionalFormatIcon(ovr.IconSet, ovr.IconId, iconCount, rule.IconSetShowValue);
            }

            if (rule.IconSetReverse)
                bucketIndex = iconCount - 1 - bucketIndex.Value;

            return new ConditionalFormatIcon(style, bucketIndex.Value, iconCount, rule.IconSetShowValue);
        }

        return null;
    }

    private static int? ResolveIconSetIndex(
        ConditionalFormat rule,
        double value,
        CfAggregateCache? cache,
        Sheet sheet,
        Workbook workbook,
        CellAddress addr,
        int iconCount,
        CfEvaluationContext cfContext)
    {
        var thresholdCount = iconCount - 1;
        if (cfContext.IconSetThresholds.TryGetValue(rule, out var cachedThresholds))
        {
            return ResolveIconSetIndexFromThresholds(
                value,
                cachedThresholds.Values,
                cachedThresholds.GreaterThanOrEqual,
                iconCount);
        }

        if (cache is null)
            return null;

        Span<double> thresholdValues = stackalloc double[thresholdCount];
        Span<bool> thresholdComparisons = stackalloc bool[thresholdCount];
        if (TryResolveIconSetThresholds(
                rule,
                cache,
                sheet,
                workbook,
                addr,
                iconCount,
                cfContext,
                thresholdValues,
                thresholdComparisons))
        {
            return ResolveIconSetIndexFromThresholds(value, thresholdValues, thresholdComparisons, iconCount);
        }

        return ResolveInterpolatedIconSetIndex(value, cache.Min, cache.Max, iconCount);
    }

    private static int ResolveIconSetIndexFromThresholds(
        double value,
        ReadOnlySpan<double> thresholdValues,
        ReadOnlySpan<bool> thresholdComparisons,
        int iconCount)
    {
        var index = 0;
        for (var i = 0; i < thresholdValues.Length; i++)
        {
            if (thresholdComparisons[i] ? value >= thresholdValues[i] : value > thresholdValues[i])
                index++;
        }

        return Math.Clamp(index, 0, iconCount - 1);
    }

    private static int ResolveInterpolatedIconSetIndex(double value, double min, double max, int iconCount)
    {
        if (!double.IsFinite(value) || !double.IsFinite(min) || !double.IsFinite(max))
            return 0;
        if (max <= min)
            return iconCount - 1;

        var t = Math.Clamp((value - min) / (max - min), 0d, 1d);
        return Math.Clamp((int)Math.Floor(t * iconCount), 0, iconCount - 1);
    }

    private static bool TryResolveIconSetThresholds(
        ConditionalFormat rule,
        CfAggregateCache cache,
        Sheet sheet,
        Workbook workbook,
        CellAddress addr,
        int iconCount,
        CfEvaluationContext cfContext,
        Span<double> thresholdValues,
        Span<bool> thresholdComparisons)
    {
        if (rule.IconSetThresholds.Count < iconCount - 1)
            return false;

        var thresholdStartIndex = ViewportConditionalFormatEvaluator.GetIconSetThresholdStartIndex(rule, iconCount);
        if (rule.IconSetThresholds.Count - thresholdStartIndex < iconCount - 1)
            return false;

        for (var i = 0; i < iconCount - 1; i++)
        {
            var sourceIndex = thresholdStartIndex + i;
            var threshold = rule.IconSetThresholds[sourceIndex];
            if (!ViewportConditionalFormatEvaluator.TryResolveThreshold(
                    threshold.Type,
                    threshold.Value,
                    cache,
                    sheet,
                    workbook,
                    addr,
                    rule.AppliesTo.Start,
                    ViewportConditionalFormatEvaluator.GetStaticThresholdFormulaValue(
                        cfContext,
                        rule,
                        CfThresholdFormulaSlot.IconSet,
                        sourceIndex),
                    ViewportConditionalFormatEvaluator.GetThresholdFormula(
                        cfContext,
                        rule,
                        CfThresholdFormulaSlot.IconSet,
                        sourceIndex),
                    out var value))
                return false;

            thresholdValues[i] = value;
            thresholdComparisons[i] = threshold.GreaterThanOrEqual ?? true;
        }

        return true;
    }

}
