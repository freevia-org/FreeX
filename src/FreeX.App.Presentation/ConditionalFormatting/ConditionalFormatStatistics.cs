using FreeX.Core.Model;

namespace FreeX.App.Presentation.ConditionalFormatting;

/// <summary>
/// Pre-computed numeric aggregates over the values in a conditional-format range. This is the
/// portable equivalent of the aggregate cache the engine builds before evaluating each cell:
/// it captures the minimum, maximum, average and a sorted copy of the numeric values (needed
/// for percentile thresholds). Build it once per rule range, then evaluate each cell against it.
/// </summary>
public sealed class ConditionalFormatStatistics
{
    private ConditionalFormatStatistics(
        int count,
        double min,
        double max,
        double average,
        IReadOnlyList<double> sortedValues)
    {
        Count = count;
        Min = min;
        Max = max;
        Average = average;
        SortedValues = sortedValues;
    }

    /// <summary>Number of numeric values in the range.</summary>
    public int Count { get; }

    /// <summary>Smallest numeric value, or 0 when the range has no numeric values.</summary>
    public double Min { get; }

    /// <summary>Largest numeric value, or 0 when the range has no numeric values.</summary>
    public double Max { get; }

    /// <summary>Arithmetic mean of the numeric values, or 0 when the range has no numeric values.</summary>
    public double Average { get; }

    /// <summary>Numeric values sorted ascending (for percentile thresholds).</summary>
    public IReadOnlyList<double> SortedValues { get; }

    /// <summary>
    /// Build statistics from a sequence of numeric values. Non-finite values (NaN / infinity)
    /// are ignored, matching the engine which only accumulates finite cell numbers.
    /// </summary>
    public static ConditionalFormatStatistics FromValues(IEnumerable<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        double sum = 0;
        double min = double.MaxValue;
        double max = double.MinValue;
        var count = 0;
        var sorted = new List<double>();
        foreach (var v in values)
        {
            if (!double.IsFinite(v))
                continue;

            sum += v;
            if (v < min) min = v;
            if (v > max) max = v;
            sorted.Add(v);
            count++;
        }

        sorted.Sort();
        return new ConditionalFormatStatistics(
            count,
            count > 0 ? min : 0,
            count > 0 ? max : 0,
            count > 0 ? sum / count : 0,
            sorted);
    }

    /// <summary>
    /// Resolve a Min/Max/Number/Percent/Percentile threshold to an absolute numeric value.
    /// Mirrors the engine's static-threshold resolution. Formula thresholds are host-resolved
    /// and must be supplied pre-evaluated via the dedicated evaluator overloads; this method
    /// returns <c>false</c> for <see cref="CfThresholdType.Formula"/>.
    /// </summary>
    public bool TryResolveThreshold(CfThresholdType type, string? value, out double resolved)
    {
        resolved = 0;
        switch (type)
        {
            case CfThresholdType.Min:
                return SetFinite(Min, out resolved);
            case CfThresholdType.Max:
                return SetFinite(Max, out resolved);
            // AutoMin/AutoMax (data-bar-only "Automatic") resolve to the same actual min/max as
            // Min/Max -- the zero-baseline clamp that distinguishes Automatic is applied separately
            // by the data-bar caller (ConditionalFormatEvaluator.EvaluateDataBar), keyed off the
            // threshold TYPE, not here.
            case CfThresholdType.AutoMin:
                return SetFinite(Min, out resolved);
            case CfThresholdType.AutoMax:
                return SetFinite(Max, out resolved);
            case CfThresholdType.Number:
                return TryParseInvariant(value, out resolved);
            case CfThresholdType.Percent:
                return TryParseInvariant(value, out var percent) &&
                       SetFinite(Min + (Max - Min) * (percent / 100d), out resolved);
            case CfThresholdType.Percentile:
                return TryParseInvariant(value, out var percentile) &&
                       TryResolvePercentile(SortedValues, percentile, out resolved);
            default:
                return false;
        }
    }

    /// <summary>
    /// Linear-interpolated percentile over a sorted list, identical to the engine's percentile
    /// math: position = (n − 1) · p / 100, then interpolate between the bracketing values.
    /// </summary>
    public static bool TryResolvePercentile(IReadOnlyList<double> sortedValues, double percentile, out double value)
    {
        ArgumentNullException.ThrowIfNull(sortedValues);

        value = 0;
        if (sortedValues.Count == 0)
            return false;

        percentile = Math.Clamp(percentile, 0d, 100d);
        if (sortedValues.Count == 1)
        {
            value = sortedValues[0];
            return true;
        }

        var position = (sortedValues.Count - 1) * percentile / 100d;
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper)
        {
            value = sortedValues[lower];
            return true;
        }

        var weight = position - lower;
        value = sortedValues[lower] + (sortedValues[upper] - sortedValues[lower]) * weight;
        return true;
    }

    internal static bool TryParseInvariant(string? text, out double result)
    {
        if (text is null)
        {
            result = 0;
            return false;
        }

        return double.TryParse(
            text,
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture,
            out result);
    }

    private static bool SetFinite(double input, out double output)
    {
        output = input;
        return double.IsFinite(input);
    }
}
