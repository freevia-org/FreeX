using Freexcel.Core.Model;

namespace Freexcel.Core.Calc;

public sealed partial class ViewportService
{
    private static ConditionalFormatIcon? EvaluateConditionalIcon(
        Sheet sheet,
        CellAddress addr,
        ScalarValue value,
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
            var iconIndex = ResolveIconSetIndex(rule, cellValue, cache.Min, cache.Max, iconCount);
            if (rule.IconSetReverse)
                iconIndex = iconCount - 1 - iconIndex;

            return new ConditionalFormatIcon(style, iconIndex, iconCount, rule.IconSetShowValue);
        }

        return null;
    }

    private static int ResolveIconSetIndex(ConditionalFormat rule, double value, double min, double max, int iconCount)
    {
        if (TryResolveIconSetThresholds(rule, min, max, iconCount, out var thresholds))
        {
            var index = 0;
            foreach (var threshold in thresholds)
            {
                if (value >= threshold)
                    index++;
            }

            return Math.Clamp(index, 0, iconCount - 1);
        }

        return ResolveInterpolatedIconSetIndex(value, min, max, iconCount);
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
        double min,
        double max,
        int iconCount,
        out double[] thresholds)
    {
        thresholds = [];
        if (rule.IconSetThresholds.Count < iconCount - 1)
            return false;

        var resolved = new List<double>(iconCount - 1);
        foreach (var threshold in rule.IconSetThresholds.Take(iconCount - 1))
        {
            switch (threshold.Type)
            {
                case CfThresholdType.Number:
                    if (!TryParseDouble(threshold.Value, out var number))
                        return false;
                    resolved.Add(number);
                    break;
                case CfThresholdType.Percent:
                    if (!TryParseDouble(threshold.Value, out var percent))
                        return false;
                    resolved.Add(min + (max - min) * (percent / 100d));
                    break;
                default:
                    return false;
            }
        }

        thresholds = resolved.ToArray();
        return thresholds.Length == iconCount - 1;
    }

    private static int GetIconSetCount(string style) =>
        style.Length > 0 && char.IsDigit(style[0])
            ? Math.Clamp(style[0] - '0', 3, 5)
            : 3;
}
