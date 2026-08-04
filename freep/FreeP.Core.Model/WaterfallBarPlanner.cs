namespace FreeP.Core.Model;

/// <summary>Whether a waterfall point is an increase, decrease, or anchored total.</summary>
public enum WaterfallBarKind
{
    Increase,
    Decrease,
    Total,
}

/// <summary>Pure waterfall geometry shared by rendering and functional editing.</summary>
public readonly record struct WaterfallBar(
    double Bottom,
    double Top,
    WaterfallBarKind Kind,
    double CumulativeAfter);

/// <summary>
/// Computes PowerPoint-style waterfall columns. A total is drawn from zero to the
/// accumulated value and does not consume its source cell as an increment.
/// </summary>
public static class WaterfallBarPlanner
{
    public static IReadOnlyList<WaterfallBar> Compute(
        IReadOnlyList<double> values,
        IReadOnlyCollection<int>? totalIndices)
    {
        if (values is null || values.Count == 0)
            return [];

        var totals = totalIndices is null
            ? []
            : totalIndices.Where(index => index >= 0 && index < values.Count).ToHashSet();
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
}
