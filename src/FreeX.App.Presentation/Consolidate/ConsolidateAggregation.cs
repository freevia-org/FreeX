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
    public static double Aggregate(IReadOnlyList<double> values, int nonEmptyCount, ConsolidateFunction function) =>
        ConsolidationRules.Aggregate(values, nonEmptyCount, function);
}
