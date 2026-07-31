using FreeX.Core.Model;

namespace FreeX.App.Presentation.ConditionalFormatting;

/// <summary>
/// Portable, framework-free evaluation of conditional-formatting rules. Given a rule definition
/// from the domain model plus the range statistics, it computes — for a single cell value — the
/// data-bar layout, color-scale fill, icon-set bucket, or highlight decision. This is a faithful
/// port of the engine math used by the desktop renderers, with native drawing types replaced
/// by plain numbers and <see cref="PresentationRgb"/>. Formula-typed thresholds are host-resolved;
/// callers pass them in pre-evaluated where the API accepts an override.
/// </summary>
public static class ConditionalFormatEvaluator
{
    // Excel's automatic negative data-bar fill color (solid red) — used whenever a data-bar rule
    // does not specify an explicit DataBarNegativeFillColor. Mirrors
    // ViewportConditionalFormatEvaluator.Thresholds.ExcelAutomaticNegativeDataBarColor (the grid
    // engine this evaluator is a portable port of) so PDF/print rendering matches the on-screen grid.
    private static readonly RgbColor ExcelAutomaticNegativeDataBarColor = new(0xFF, 0x00, 0x00);

    // ── Data bars ────────────────────────────────────────────────────────────

    /// <summary>
    /// Compute the data-bar layout for <paramref name="cellValue"/>, or <c>null</c> when the bar
    /// is empty / the thresholds cannot be resolved. Supports a negative axis: when the resolved
    /// range spans zero the axis is placed proportionally and negative values fill leftward.
    /// </summary>
    public static DataBarLayout? EvaluateDataBar(
        ConditionalFormat rule,
        double cellValue,
        ConditionalFormatStatistics stats,
        double? minOverride = null,
        double? maxOverride = null)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(stats);

        if (!double.IsFinite(cellValue))
            return null;

        if (!ResolveThreshold(rule.DataBarMinThresholdType, rule.DataBarMinThresholdValue, stats, minOverride, out var min) ||
            !ResolveThreshold(rule.DataBarMaxThresholdType, rule.DataBarMaxThresholdValue, stats, maxOverride, out var max))
        {
            return null;
        }

        // Excel's automatic (CfThresholdType.AutoMin/AutoMax) data-bar threshold always keeps a zero
        // baseline: the automatic minimum is min(0, actual minimum) and the automatic maximum is
        // max(0, actual maximum). Without this, an all-positive range (e.g. 10/20/30) would resolve
        // min=10, giving the smallest cell a zero-length bar instead of Excel's ~1/3-length bar. An
        // EXPLICIT Lowest/Highest Value endpoint (CfThresholdType.Min/Max) is deliberately excluded --
        // Excel does not zero-clamp an explicit endpoint, only Automatic -- as are the genuinely
        // explicit numeric/percent/percentile/formula thresholds (see
        // ViewportConditionalFormatEvaluator.Thresholds.cs, the engine this mirrors).
        if (rule.DataBarMinThresholdType == CfThresholdType.AutoMin)
            min = Math.Min(0d, min);
        if (rule.DataBarMaxThresholdType == CfThresholdType.AutoMax)
            max = Math.Max(0d, max);

        if (max <= min)
        {
            return null;
        }

        var minLength = Math.Clamp(rule.DataBarMinLength ?? 0, 0, 100) / 100d;
        var maxLength = Math.Clamp(rule.DataBarMaxLength ?? 100, 0, 100) / 100d;
        if (maxLength < minLength)
            (minLength, maxLength) = (maxLength, minLength);

        var fill = PresentationRgb.FromRgbColor(rule.DataBarColor);

        // Automatic axis: when the value range straddles zero (and the rule does not pin the axis
        // to an edge via "none"), the axis sits at the proportional zero position and negative
        // values fill to its left. "Middle" pins the axis at Excel's fixed 50% (cell-center)
        // position regardless of the min/max skew -- including an all-positive (or all-negative)
        // range that would otherwise never straddle zero, since the user explicitly asked for the
        // cell-center axis rather than the automatic zero-crossing one (see
        // ViewportConditionalFormatEvaluator.Thresholds.cs, the engine this mirrors). Otherwise
        // (axis "none", or "Automatic" outside a zero-straddling range) the engine's left-anchored
        // layout is reproduced exactly (axis at 0, bar from 0 to length).
        var axisAtNone = string.Equals(rule.DataBarAxisPosition, "none", StringComparison.OrdinalIgnoreCase);
        var axisAtMiddle = string.Equals(rule.DataBarAxisPosition, "middle", StringComparison.OrdinalIgnoreCase);
        // min < 0 <= max: matches the engine's negative-axis condition (see
        // ViewportConditionalFormatEvaluator.Thresholds.cs), which must also accept max == 0 so an
        // all-negative range (whose automatic maximum clamps to zero upstream) still resolves the
        // axis at the right edge with bars growing leftward in the negative fill color, rather than
        // falling through to the left-anchored positive-only path below.
        if (!axisAtNone && (axisAtMiddle || (min < 0 && max >= 0)))
        {
            // Division-by-zero guard: with axisAtMiddle forcing entry here, min/max need not
            // straddle zero any more (e.g. an all-positive range has min == 0 after the
            // automatic-minimum zero clamp above) -- the "Automatic" ternary branch below is only
            // ever reached when min < 0 <= max (see the outer condition), guaranteeing
            // max - min > 0 there.
            var axisFraction = axisAtMiddle ? 0.5d : (0 - min) / (max - min);
            var negativeFill = rule.DataBarNegativeFillColor.HasValue
                ? PresentationRgb.FromRgbColor(rule.DataBarNegativeFillColor.Value)
                : PresentationRgb.FromRgbColor(ExcelAutomaticNegativeDataBarColor);

            if (cellValue >= 0)
            {
                var t = max > 0d ? Math.Clamp((cellValue - 0) / (max - 0), 0d, 1d) : 0d;
                var length = (minLength + (maxLength - minLength) * t) * (1 - axisFraction);
                if (length <= 0)
                    return null;
                return new DataBarLayout(axisFraction, axisFraction + length, axisFraction, IsNegative: false, fill, rule.DataBarGradient, rule.DataBarBorder, rule.DataBarShowValue);
            }
            else
            {
                // min can be exactly 0 here too (axisAtMiddle forcing entry with an explicit,
                // non-auto-clamped non-negative min threshold while an actual cell value is still
                // negative); treat that as a zero-length negative segment from the axis instead of
                // dividing by zero.
                var t = min < 0d ? Math.Clamp((0 - cellValue) / (0 - min), 0d, 1d) : 0d;
                var length = (minLength + (maxLength - minLength) * t) * axisFraction;
                if (length <= 0)
                    return null;
                return new DataBarLayout(axisFraction - length, axisFraction, axisFraction, IsNegative: true, negativeFill, rule.DataBarGradient, rule.DataBarBorder, rule.DataBarShowValue);
            }
        }

        // Left-anchored (engine-equivalent) path.
        var fraction = Math.Clamp((cellValue - min) / (max - min), 0d, 1d);
        var barLength = minLength + (maxLength - minLength) * fraction;
        if (barLength <= 0)
            return null;

        return new DataBarLayout(0d, Math.Clamp(barLength, 0d, 1d), 0d, IsNegative: false, fill, rule.DataBarGradient, rule.DataBarBorder, rule.DataBarShowValue);
    }

    // ── Color scales ─────────────────────────────────────────────────────────

    /// <summary>
    /// Compute the interpolated color-scale fill for <paramref name="cellValue"/>, or <c>null</c>
    /// when the value is non-numeric or thresholds cannot be resolved. Two-color scales lerp
    /// min→max; three-color scales lerp min→mid then mid→max about the resolved mid point.
    /// </summary>
    public static ColorScaleResult? EvaluateColorScale(
        ConditionalFormat rule,
        double cellValue,
        ConditionalFormatStatistics stats,
        double? minOverride = null,
        double? midOverride = null,
        double? maxOverride = null)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(stats);

        if (!double.IsFinite(cellValue))
            return null;

        if (!ResolveThreshold(rule.MinThresholdType, rule.MinThresholdValue, stats, minOverride, out var min) ||
            !ResolveThreshold(rule.MaxThresholdType, rule.MaxThresholdValue, stats, maxOverride, out var max))
        {
            return null;
        }

        // A degenerate resolved midpoint (== min or == max, e.g. a skewed dataset where
        // percentile-50 lands exactly on the min) must still keep the 3-stop MidColor in the
        // gradient -- clamp into [min,max] instead of dropping mid to null, which used to collapse
        // the WHOLE range to a plain Min->Max lerp and erase MidColor everywhere, not just at the
        // degenerate point (see ViewportConditionalFormatEvaluator.Thresholds.cs, the engine this
        // mirrors).
        double? mid = null;
        if (rule.UseThreeColorScale &&
            ResolveThreshold(rule.MidThresholdType, rule.MidThresholdValue, stats, midOverride, out var resolvedMid))
        {
            mid = Math.Clamp(resolvedMid, min, max);
        }

        if (max <= min)
            return new ColorScaleResult(PresentationRgb.FromRgbColor(rule.MinColor));

        var color = mid.HasValue
            ? cellValue <= mid.Value
                ? mid.Value > min
                    ? Lerp(rule.MinColor, rule.MidColor, Math.Clamp((cellValue - min) / (mid.Value - min), 0d, 1d))
                    : PresentationRgb.FromRgbColor(rule.MidColor)
                : mid.Value < max
                    ? Lerp(rule.MidColor, rule.MaxColor, Math.Clamp((cellValue - mid.Value) / (max - mid.Value), 0d, 1d))
                    : PresentationRgb.FromRgbColor(rule.MidColor)
            : Lerp(rule.MinColor, rule.MaxColor, Math.Clamp((cellValue - min) / (max - min), 0d, 1d));

        return new ColorScaleResult(color);
    }

    // ── Icon sets ────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolve the icon-set bucket for <paramref name="cellValue"/>, or <c>null</c> when no icon
    /// applies. Uses explicit thresholds when present (≥ / &gt; per threshold), otherwise falls
    /// back to the engine's equal-width interpolated bucketing between min and max. Honors
    /// <see cref="ConditionalFormat.IconSetReverse"/>.
    /// </summary>
    public static IconSetResult? EvaluateIconSet(
        ConditionalFormat rule,
        double cellValue,
        ConditionalFormatStatistics stats,
        IReadOnlyList<double>? thresholdOverrides = null)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(stats);

        if (!double.IsFinite(cellValue))
            return null;

        var style = string.IsNullOrWhiteSpace(rule.IconSetStyle) ? "3TrafficLights1" : rule.IconSetStyle!;
        var iconCount = GetIconSetCount(style);

        var bucket = ResolveIconBucket(rule, cellValue, stats, iconCount, thresholdOverrides);
        if (!bucket.HasValue)
            return null;

        var index = bucket.Value;
        if (rule.IconSetReverse)
            index = iconCount - 1 - index;

        return new IconSetResult(style, index, iconCount, rule.IconSetShowValue);
    }

    private static int? ResolveIconBucket(
        ConditionalFormat rule,
        double cellValue,
        ConditionalFormatStatistics stats,
        int iconCount,
        IReadOnlyList<double>? thresholdOverrides)
    {
        var thresholdCount = iconCount - 1;

        // Explicit thresholds (resolved here, or supplied pre-resolved for formula thresholds).
        if (TryResolveIconThresholds(rule, stats, iconCount, thresholdOverrides, out var values, out var comparisons))
            return BucketFromThresholds(cellValue, values, comparisons, iconCount);

        // Fallback: equal-width interpolation between min and max.
        return InterpolatedBucket(cellValue, stats.Min, stats.Max, iconCount);
    }

    private static bool TryResolveIconThresholds(
        ConditionalFormat rule,
        ConditionalFormatStatistics stats,
        int iconCount,
        IReadOnlyList<double>? thresholdOverrides,
        out double[] values,
        out bool[] comparisons)
    {
        var thresholdCount = iconCount - 1;
        values = [];
        comparisons = [];

        if (thresholdOverrides is not null)
        {
            if (thresholdOverrides.Count < thresholdCount)
                return false;

            values = new double[thresholdCount];
            comparisons = new bool[thresholdCount];
            var startIndex = GetIconSetThresholdStartIndex(rule, iconCount);
            for (var i = 0; i < thresholdCount; i++)
            {
                values[i] = thresholdOverrides[i];
                var sourceIndex = startIndex + i;
                comparisons[i] = sourceIndex < rule.IconSetThresholds.Count
                    ? rule.IconSetThresholds[sourceIndex].GreaterThanOrEqual ?? true
                    : true;
                if (!double.IsFinite(values[i]))
                    return false;
            }

            return true;
        }

        if (rule.IconSetThresholds.Count < thresholdCount)
            return false;

        var thresholdStartIndex = GetIconSetThresholdStartIndex(rule, iconCount);
        if (rule.IconSetThresholds.Count - thresholdStartIndex < thresholdCount)
            return false;

        values = new double[thresholdCount];
        comparisons = new bool[thresholdCount];
        for (var i = 0; i < thresholdCount; i++)
        {
            var threshold = rule.IconSetThresholds[thresholdStartIndex + i];
            if (threshold.Type == CfThresholdType.Formula ||
                !stats.TryResolveThreshold(threshold.Type, threshold.Value, out values[i]))
            {
                return false;
            }

            comparisons[i] = threshold.GreaterThanOrEqual ?? true;
        }

        return true;
    }

    private static int BucketFromThresholds(
        double value,
        ReadOnlySpan<double> thresholdValues,
        ReadOnlySpan<bool> greaterThanOrEqual,
        int iconCount)
    {
        var index = 0;
        for (var i = 0; i < thresholdValues.Length; i++)
        {
            if (greaterThanOrEqual[i] ? value >= thresholdValues[i] : value > thresholdValues[i])
                index++;
        }

        return Math.Clamp(index, 0, iconCount - 1);
    }

    private static int InterpolatedBucket(double value, double min, double max, int iconCount)
    {
        if (!double.IsFinite(value) || !double.IsFinite(min) || !double.IsFinite(max))
            return 0;
        if (max <= min)
            return iconCount - 1;

        var t = Math.Clamp((value - min) / (max - min), 0d, 1d);
        return Math.Clamp((int)Math.Floor(t * iconCount), 0, iconCount - 1);
    }

    /// <summary>Icon count encoded in the style name (3, 4 or 5), defaulting to 3.</summary>
    public static int GetIconSetCount(string? style) =>
        !string.IsNullOrWhiteSpace(style) && char.IsDigit(style![0])
            ? Math.Clamp(style[0] - '0', 3, 5)
            : 3;

    private static int GetIconSetThresholdStartIndex(ConditionalFormat rule, int iconCount) =>
        rule.IconSetThresholds.Count >= iconCount ? 1 : 0;

    // ── Highlight / selection rules ──────────────────────────────────────────

    /// <summary>
    /// Evaluate a numeric CellValue comparison (Equal / NotEqual / GreaterThan / … / Between /
    /// NotBetween) against the rule's resolved thresholds.
    /// </summary>
    public static bool MatchesCellValueNumeric(ConditionalFormat rule, double cellValue)
    {
        ArgumentNullException.ThrowIfNull(rule);

        if (!ConditionalFormatStatistics.TryParseInvariant(rule.Value1, out var v1))
            return false;

        return rule.Operator switch
        {
            CfOperator.Equal => cellValue == v1,
            CfOperator.NotEqual => cellValue != v1,
            CfOperator.GreaterThan => cellValue > v1,
            CfOperator.GreaterThanOrEqual => cellValue >= v1,
            CfOperator.LessThan => cellValue < v1,
            CfOperator.LessThanOrEqual => cellValue <= v1,
            CfOperator.Between => ConditionalFormatStatistics.TryParseInvariant(rule.Value2, out var v2) && cellValue >= v1 && cellValue <= v2,
            CfOperator.NotBetween => ConditionalFormatStatistics.TryParseInvariant(rule.Value2, out var v2b) && !(cellValue >= v1 && cellValue <= v2b),
            _ => false
        };
    }

    /// <summary>True when the value is above (or, when <see cref="ConditionalFormat.AboveAverage"/>
    /// is false, below) the range average. Honors <see cref="ConditionalFormat.EqualAverage"/>
    /// (Excel's "Equal or Above/Below Average" variant, which turns the comparison into &gt;=/&lt;=)
    /// and <see cref="ConditionalFormat.StdDevCount"/> (Excel's "N standard deviations
    /// above/below average" variant, which shifts the threshold to <c>average ± N * stdDev</c>
    /// instead of the plain average). Mirrors
    /// <c>ViewportConditionalFormatEvaluator.MatchesAboveAverage</c>, the engine this is a portable
    /// port of, so printed/exported output matches the on-screen grid.</summary>
    public static bool MatchesAboveBelowAverage(ConditionalFormat rule, double cellValue, ConditionalFormatStatistics stats)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(stats);

        if (!double.IsFinite(cellValue) || stats.Count == 0)
            return false;

        var threshold = stats.Average;
        if (rule.StdDevCount is { } n && n > 0)
            threshold = rule.AboveAverage
                ? stats.Average + n * stats.StdDev
                : stats.Average - n * stats.StdDev;

        return rule.AboveAverage
            ? (rule.EqualAverage ? cellValue >= threshold : cellValue > threshold)
            : (rule.EqualAverage ? cellValue <= threshold : cellValue < threshold);
    }

    private static bool ResolveThreshold(
        CfThresholdType type,
        string? value,
        ConditionalFormatStatistics stats,
        double? formulaOverride,
        out double resolved)
    {
        if (type == CfThresholdType.Formula)
        {
            if (formulaOverride is { } v && double.IsFinite(v))
            {
                resolved = v;
                return true;
            }

            resolved = 0;
            return false;
        }

        return stats.TryResolveThreshold(type, value, out resolved);
    }

    private static PresentationRgb Lerp(RgbColor a, RgbColor b, double t)
    {
        var r = (byte)Math.Round(a.R + (b.R - a.R) * t);
        var g = (byte)Math.Round(a.G + (b.G - a.G) * t);
        var bl = (byte)Math.Round(a.B + (b.B - a.B) * t);
        return new PresentationRgb(r, g, bl);
    }
}
