using System;
using System.Collections.Generic;

namespace FreeX.Core.Model;

/// <summary>Whether a waterfall column is a positive step, a negative step, or a total/anchor column.</summary>
public enum WaterfallBarKind
{
    Increase,
    Decrease,
    Total,
}

/// <summary>
/// A single waterfall column. <see cref="Bottom"/>/<see cref="Top"/> are the value-axis extents
/// (already ordered low→high), <see cref="Kind"/> drives the column colour, and
/// <see cref="CumulativeAfter"/> is the running cumulative once this column is applied — the level a
/// connector line to the next column is drawn at.
/// </summary>
public readonly record struct WaterfallBar(double Bottom, double Top, WaterfallBarKind Kind, double CumulativeAfter);

/// <summary>
/// Pure, deterministic waterfall column geometry. Step columns stack on the running cumulative; a
/// point marked as a total is an anchor column drawn from zero to the current running cumulative
/// (its own cell value is ignored) and does not advance the running total, so later steps continue
/// from it — matching Excel's "Set as Total" behaviour. WPF-free and unit-tested; the renderer draws
/// the result.
/// </summary>
public static class WaterfallBarPlanner
{
    /// <param name="totalIndices">
    /// 0-based indices to treat as total/anchor columns. <c>null</c> falls back to treating the last
    /// point as the total (legacy default); an empty collection means no totals.
    /// </param>
    public static IReadOnlyList<WaterfallBar> Compute(
        IReadOnlyList<double> values,
        IReadOnlyCollection<int>? totalIndices)
    {
        if (values is null || values.Count == 0)
            return [];

        var totals = ResolveTotals(values.Count, totalIndices);

        var bars = new List<WaterfallBar>(values.Count);
        var running = 0d;
        for (var i = 0; i < values.Count; i++)
        {
            if (totals.Contains(i))
            {
                // Anchor column: 0..running, ignoring this point's own value; running is unchanged.
                bars.Add(new WaterfallBar(
                    Math.Min(0, running), Math.Max(0, running), WaterfallBarKind.Total, running));
                continue;
            }

            var top = running + values[i];
            bars.Add(new WaterfallBar(
                Math.Min(running, top),
                Math.Max(running, top),
                values[i] >= 0 ? WaterfallBarKind.Increase : WaterfallBarKind.Decrease,
                top));
            running = top;
        }

        return bars;
    }

    private static HashSet<int> ResolveTotals(int count, IReadOnlyCollection<int>? totalIndices)
    {
        if (totalIndices is null)
            return count > 0 ? [count - 1] : [];

        return [.. totalIndices];
    }
}
