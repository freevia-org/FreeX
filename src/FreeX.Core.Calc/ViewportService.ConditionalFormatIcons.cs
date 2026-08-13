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

            // A higher-priority rule of ANY kind (style, icon set, or data bar) whose condition is
            // met and which is marked Stop If True suppresses this icon, matching Excel's standard
            // "stop if true hides lower-priority icon sets/data bars" idiom.
            if (ViewportConditionalFormatEvaluator.IsSuppressedByHigherPriorityStopIfTrue(
                    rule, sheet, addr, value, workbook, cfContext, MatchesFormula))
                return null;

            if (!TryGetDouble(value, out var cellValue))
                return null;

            var style = string.IsNullOrWhiteSpace(rule.IconSetStyle) ? "3TrafficLights1" : rule.IconSetStyle!;
            var iconCount = ConditionalFormatEvaluationMath.GetIconSetCount(style);
            cfContext.Aggregates.TryGetValue(rule, out var cache);
            var bucketIndex = ResolveIconSetIndex(rule, cellValue, cache, sheet, workbook, addr, iconCount, cfContext);
            if (!bucketIndex.HasValue)
                return null;

            // Reverse Icon Order mirrors which bucket maps to which icon; apply it before picking
            // an icon so it takes effect whether the bucket resolves to a per-threshold custom
            // icon override or to the rule's default icon-set style.
            if (rule.IconSetReverse)
                bucketIndex = iconCount - 1 - bucketIndex.Value;

            if (rule.IconOverrides.Count == iconCount)
            {
                var ovr = rule.IconOverrides[bucketIndex.Value];
                if (string.Equals(ovr.IconSet, "NoIcons", StringComparison.OrdinalIgnoreCase))
                    return null;

                // An override may pick an icon from a different family than the rule's own
                // icon-set style (e.g. a 5-arrow glyph plugged into a 3-icon rule). The glyph
                // shape/color/rating-bar math downstream keys off IconCount, so it must reflect
                // the override's OWN family size, not the rule-bucket count used to resolve
                // which threshold bucket we're in.
                var overrideIconCount = ConditionalFormatEvaluationMath.GetIconSetCount(ovr.IconSet);
                return new ConditionalFormatIcon(ovr.IconSet, ovr.IconId, overrideIconCount, rule.IconSetShowValue);
            }

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
            return ConditionalFormatEvaluationMath.ResolveIconBucket(
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
            return ConditionalFormatEvaluationMath.ResolveIconBucket(value, thresholdValues, thresholdComparisons, iconCount);
        }

        return ConditionalFormatEvaluationMath.ResolveInterpolatedIconBucket(value, cache.Min, cache.Max, iconCount);
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

        var thresholdStartIndex = ConditionalFormatEvaluationMath.GetIconSetThresholdStartIndex(rule.IconSetThresholds.Count, iconCount);
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
