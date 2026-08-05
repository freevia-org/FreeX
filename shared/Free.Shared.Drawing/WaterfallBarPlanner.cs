namespace Free.Shared.Drawing;

public enum WaterfallBarKind
{
    Increase,
    Decrease,
    Total,
}

public enum WaterfallNullTotalsPolicy
{
    NoTotals,
    LastPointIsTotal,
}

public readonly record struct WaterfallBar(
    double Bottom,
    double Top,
    WaterfallBarKind Kind,
    double CumulativeAfter);

/// <summary>
/// Pure waterfall-chart geometry shared by the spreadsheet and presentation workareas. A total is
/// anchored at zero and does not consume its source value as an increment.
/// </summary>
public static class WaterfallBarPlanner
{
    public static IReadOnlyList<WaterfallBar> Compute(
        IReadOnlyList<double> values,
        IReadOnlyCollection<int>? totalIndices,
        WaterfallNullTotalsPolicy nullTotalsPolicy)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
            return [];

        var totals = ResolveTotals(values.Count, totalIndices, nullTotalsPolicy);
        var result = new List<WaterfallBar>(values.Count);
        var cumulative = 0d;

        for (var index = 0; index < values.Count; index++)
        {
            if (totals.Contains(index))
            {
                result.Add(new WaterfallBar(
                    Math.Min(0, cumulative),
                    Math.Max(0, cumulative),
                    WaterfallBarKind.Total,
                    cumulative));
                continue;
            }

            var next = cumulative + values[index];
            result.Add(new WaterfallBar(
                Math.Min(cumulative, next),
                Math.Max(cumulative, next),
                values[index] >= 0 ? WaterfallBarKind.Increase : WaterfallBarKind.Decrease,
                next));
            cumulative = next;
        }

        return result;
    }

    private static HashSet<int> ResolveTotals(
        int count,
        IReadOnlyCollection<int>? totalIndices,
        WaterfallNullTotalsPolicy nullTotalsPolicy)
    {
        if (totalIndices is not null)
            return totalIndices.Where(index => index >= 0 && index < count).ToHashSet();

        return nullTotalsPolicy switch
        {
            WaterfallNullTotalsPolicy.NoTotals => [],
            WaterfallNullTotalsPolicy.LastPointIsTotal => [count - 1],
            _ => throw new ArgumentOutOfRangeException(nameof(nullTotalsPolicy)),
        };
    }
}
