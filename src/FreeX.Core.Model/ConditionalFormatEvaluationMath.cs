using System.Globalization;

namespace FreeX.Core.Model;

public readonly record struct ConditionalFormatEvaluationStatistics(
    int Count,
    double Min,
    double Max,
    double Average,
    double StdDev,
    IReadOnlyList<double>? SortedValues = null);

/// <summary>Pure conditional-format aggregate and threshold math shared by every renderer.</summary>
public static class ConditionalFormatEvaluationMath
{
    public sealed class StatisticsAccumulator
    {
        private readonly List<double>? _values;
        private double _sum;
        private double _sumSquares;
        private double _min = double.MaxValue;
        private double _max = double.MinValue;

        public StatisticsAccumulator(bool retainSortedValues = false)
        {
            if (retainSortedValues)
                _values = [];
        }

        public int Count { get; private set; }

        public bool Add(double value)
        {
            if (!double.IsFinite(value))
                return false;

            _sum += value;
            _sumSquares += value * value;
            _min = Math.Min(_min, value);
            _max = Math.Max(_max, value);
            _values?.Add(value);
            Count++;
            return true;
        }

        public ConditionalFormatEvaluationStatistics Build()
        {
            _values?.Sort();
            var average = Count > 0 ? _sum / Count : 0;
            var stdDev = Count > 1
                ? Math.Sqrt(Math.Max(0, (_sumSquares - Count * average * average) / (Count - 1)))
                : 0;
            return new ConditionalFormatEvaluationStatistics(
                Count,
                Count > 0 ? _min : 0,
                Count > 0 ? _max : 0,
                average,
                stdDev,
                _values);
        }
    }

    public static ConditionalFormatEvaluationStatistics CalculateStatistics(
        IEnumerable<double> values,
        bool retainSortedValues = true)
    {
        ArgumentNullException.ThrowIfNull(values);
        var accumulator = new StatisticsAccumulator(retainSortedValues);
        foreach (var value in values)
            accumulator.Add(value);
        return accumulator.Build();
    }

    public static bool TryResolveStaticThreshold(
        CfThresholdType type,
        string? text,
        ConditionalFormatEvaluationStatistics statistics,
        out double value)
    {
        value = 0;
        return type switch
        {
            CfThresholdType.Min or CfThresholdType.AutoMin => SetFinite(statistics.Min, out value),
            CfThresholdType.Max or CfThresholdType.AutoMax => SetFinite(statistics.Max, out value),
            CfThresholdType.Number => TryParseInvariant(text, out value),
            CfThresholdType.Percent => TryParseInvariant(text, out var percent) &&
                                       SetFinite(statistics.Min + (statistics.Max - statistics.Min) * percent / 100d, out value),
            CfThresholdType.Percentile => TryParseInvariant(text, out var percentile) &&
                                          TryResolvePercentile(statistics.SortedValues, percentile, out value),
            _ => false
        };
    }

    public static bool TryResolvePercentile(
        IReadOnlyList<double>? sortedValues,
        double percentile,
        out double value)
    {
        value = 0;
        if (sortedValues is null || sortedValues.Count == 0)
            return false;

        percentile = Math.Clamp(percentile, 0d, 100d);
        var position = (sortedValues.Count - 1) * percentile / 100d;
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        var weight = position - lower;
        value = sortedValues[lower] + (sortedValues[upper] - sortedValues[lower]) * weight;
        return true;
    }

    public static bool MatchesAboveAverage(
        double value,
        ConditionalFormatEvaluationStatistics statistics,
        bool aboveAverage,
        bool equalAverage,
        int? stdDevCount)
    {
        if (!double.IsFinite(value) || statistics.Count == 0)
            return false;

        var threshold = statistics.Average;
        if (stdDevCount is > 0)
            threshold += (aboveAverage ? 1 : -1) * stdDevCount.Value * statistics.StdDev;

        return aboveAverage
            ? equalAverage ? value >= threshold : value > threshold
            : equalAverage ? value <= threshold : value < threshold;
    }

    public static int GetIconSetCount(string? style) =>
        !string.IsNullOrWhiteSpace(style) && char.IsDigit(style[0])
            ? Math.Clamp(style[0] - '0', 3, 5)
            : 3;

    public static int GetIconSetThresholdStartIndex(int thresholdCount, int iconCount) =>
        thresholdCount >= iconCount ? 1 : 0;

    public static int ResolveIconBucket(
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

    public static int ResolveInterpolatedIconBucket(double value, double min, double max, int iconCount)
    {
        if (!double.IsFinite(value) || !double.IsFinite(min) || !double.IsFinite(max))
            return 0;
        if (max <= min)
            return iconCount - 1;

        var position = Math.Clamp((value - min) / (max - min), 0d, 1d);
        return Math.Clamp((int)Math.Floor(position * iconCount), 0, iconCount - 1);
    }

    public static bool TryParseInvariant(string? text, out double value) =>
        double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out value);

    private static bool SetFinite(double input, out double output)
    {
        output = input;
        return double.IsFinite(input);
    }
}
