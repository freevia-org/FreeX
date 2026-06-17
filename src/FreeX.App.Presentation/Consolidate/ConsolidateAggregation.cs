using FreeX.Core.Commands;

namespace FreeX.App.Presentation.Consolidate;

/// <summary>
/// The numeric aggregation that backs a consolidation, mirroring the desktop hosts' Core consolidation
/// rules exactly: COUNT counts non-empty cells (numbers and labels alike); COUNTNUMBERS counts only the
/// numeric cells; every statistical function operates over just the numeric values and returns zero for an
/// empty set; the sample variance/standard deviation return zero when fewer than two numbers are present.
/// </summary>
public static class ConsolidateAggregation
{
    /// <summary>
    /// Aggregates the collected numeric <paramref name="values"/> for one output cell using
    /// <paramref name="function"/>. <paramref name="nonEmptyCount"/> is the number of contributing source
    /// cells that were not blank (used by <see cref="ConsolidateFunction.Count"/>).
    /// </summary>
    public static double Aggregate(IReadOnlyList<double> values, int nonEmptyCount, ConsolidateFunction function)
    {
        ArgumentNullException.ThrowIfNull(values);

        return function switch
        {
            ConsolidateFunction.Count => nonEmptyCount,
            ConsolidateFunction.Average => values.Count == 0 ? 0 : values.Average(),
            ConsolidateFunction.Max => values.Count == 0 ? 0 : values.Max(),
            ConsolidateFunction.Min => values.Count == 0 ? 0 : values.Min(),
            ConsolidateFunction.Product => values.Count == 0 ? 0 : values.Aggregate(1.0, (product, value) => product * value),
            ConsolidateFunction.CountNumbers => values.Count,
            ConsolidateFunction.StdDev => StandardDeviation(values, sample: true),
            ConsolidateFunction.StdDevp => StandardDeviation(values, sample: false),
            ConsolidateFunction.Var => Variance(values, sample: true),
            ConsolidateFunction.Varp => Variance(values, sample: false),
            _ => values.Sum()
        };
    }

    private static double StandardDeviation(IReadOnlyList<double> values, bool sample) =>
        Math.Sqrt(Variance(values, sample));

    private static double Variance(IReadOnlyList<double> values, bool sample)
    {
        var denominator = sample ? values.Count - 1 : values.Count;
        if (denominator <= 0)
            return 0;

        var average = values.Average();
        return values.Sum(value => Math.Pow(value - average, 2)) / denominator;
    }
}
