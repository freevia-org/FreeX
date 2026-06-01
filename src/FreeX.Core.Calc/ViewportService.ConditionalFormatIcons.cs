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
        foreach (var rule in cfContext.IconRulesByPriority)
        {
            if (!rule.AppliesTo.Contains(addr))
                continue;
            if (!TryGetDouble(value, out var cellValue) || !cfContext.Aggregates.TryGetValue(rule, out var cache))
                return null;

            var style = string.IsNullOrWhiteSpace(rule.IconSetStyle) ? "3TrafficLights1" : rule.IconSetStyle!;
            var iconCount = GetIconSetCount(style);
            var bucketIndex = ResolveIconSetIndex(rule, cellValue, cache, sheet, workbook, addr, iconCount, cfContext);

            if (rule.IconOverrides.Count == iconCount)
            {
                var ovr = rule.IconOverrides[bucketIndex];
                return new ConditionalFormatIcon(ovr.IconSet, ovr.IconId, iconCount, rule.IconSetShowValue);
            }

            if (rule.IconSetReverse)
                bucketIndex = iconCount - 1 - bucketIndex;

            return new ConditionalFormatIcon(style, bucketIndex, iconCount, rule.IconSetShowValue);
        }

        return null;
    }

    private static int ResolveIconSetIndex(
        ConditionalFormat rule,
        double value,
        CfAggregateCache cache,
        Sheet sheet,
        Workbook workbook,
        CellAddress addr,
        int iconCount,
        CfEvaluationContext cfContext)
    {
        if (TryResolveIconSetThresholds(rule, cache, sheet, workbook, addr, iconCount, cfContext, out var thresholds))
        {
            var index = 0;
            foreach (var threshold in thresholds)
            {
                if (threshold.GreaterThanOrEqual ? value >= threshold.Value : value > threshold.Value)
                    index++;
            }

            return Math.Clamp(index, 0, iconCount - 1);
        }

        return ResolveInterpolatedIconSetIndex(value, cache.Min, cache.Max, iconCount);
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
        out (double Value, bool GreaterThanOrEqual)[] thresholds)
    {
        thresholds = [];
        if (rule.IconSetThresholds.Count < iconCount - 1)
            return false;

        var resolved = new List<(double Value, bool GreaterThanOrEqual)>(iconCount - 1);
        for (var i = 0; i < iconCount - 1; i++)
        {
            var threshold = rule.IconSetThresholds[i];
            if (!ViewportConditionalFormatEvaluator.TryResolveThreshold(
                    threshold.Type,
                    threshold.Value,
                    cache,
                    sheet,
                    workbook,
                    addr,
                    ViewportConditionalFormatEvaluator.GetThresholdFormula(
                        cfContext,
                        rule,
                        CfThresholdFormulaSlot.IconSet,
                        i),
                    out var value))
                return false;

            resolved.Add((value, threshold.GreaterThanOrEqual ?? true));
        }

        thresholds = resolved.ToArray();
        return thresholds.Length == iconCount - 1;
    }

    private static int GetIconSetCount(string style) =>
        style.Length > 0 && char.IsDigit(style[0])
            ? Math.Clamp(style[0] - '0', 3, 5)
            : 3;
}
