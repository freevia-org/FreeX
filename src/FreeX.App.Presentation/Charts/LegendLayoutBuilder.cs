using FreeX.Core.Model;

namespace FreeX.App.Presentation.Charts;

/// <summary>
/// Positions the chart legend and reserves its gutter from the plot rectangle. The legend is placed
/// on the side named by the chart model; each entry gets a color swatch box plus a label box sized
/// by the text measurer. When the legend overlays the plot (or is hidden) the full plot rectangle is
/// returned unchanged.
/// </summary>
internal static class LegendLayoutBuilder
{
    private const double SwatchSize = 12;
    private const double SwatchLabelGap = 4;
    private const double EntrySpacing = 6;
    private const double LegendPadding = 6;

    /// <summary>
    /// Builds the legend and outputs the <paramref name="plot"/> rectangle remaining for the series
    /// after the legend gutter is reserved.
    /// </summary>
    public static LegendLayout Build(ChartLayoutRequest request, out PlotRect plot)
    {
        var chart = request.Chart;
        plot = request.PlotArea;

        if (!chart.ShowLegend || chart.LegendPosition == ChartLegendPosition.None)
            return LegendLayout.None;

        var labels = CollectLabels(request);
        if (labels.Count == 0)
            return LegendLayout.None;

        // Measure each label to size the legend.
        var measured = new (int SeriesIndex, string Label, double Width, double Height)[labels.Count];
        var maxLabelWidth = 0.0;
        var maxLabelHeight = 0.0;
        var totalLabelWidth = 0.0;
        for (var i = 0; i < labels.Count; i++)
        {
            var size = request.TextMeasurer.Measure(labels[i].Label, null, chart.LegendFontSize, false, false);
            measured[i] = (labels[i].SeriesIndex, labels[i].Label, size.Width, size.Height);
            maxLabelWidth = Math.Max(maxLabelWidth, size.Width);
            maxLabelHeight = Math.Max(maxLabelHeight, size.Height);
            totalLabelWidth += size.Width;
        }

        var isVertical = chart.LegendPosition is ChartLegendPosition.Left or ChartLegendPosition.Right;
        var entryHeight = Math.Max(SwatchSize, maxLabelHeight);

        LayoutRect bounds;
        if (isVertical)
        {
            var width = LegendPadding * 2 + SwatchSize + SwatchLabelGap + maxLabelWidth;
            var height = LegendPadding * 2 + (measured.Length * entryHeight) + ((measured.Length - 1) * EntrySpacing);
            if (chart.LegendPosition == ChartLegendPosition.Right)
            {
                bounds = new LayoutRect(plot.Right - width, plot.Y, width, Math.Min(height, plot.Height));
                if (!chart.LegendOverlay)
                    plot = new PlotRect(plot.X, plot.Y, Math.Max(0, plot.Width - width), plot.Height);
            }
            else
            {
                bounds = new LayoutRect(plot.X, plot.Y, width, Math.Min(height, plot.Height));
                if (!chart.LegendOverlay)
                    plot = new PlotRect(plot.X + width, plot.Y, Math.Max(0, plot.Width - width), plot.Height);
            }
        }
        else
        {
            var entryWidths = measured.Sum(m => SwatchSize + SwatchLabelGap + m.Width);
            var width = LegendPadding * 2 + entryWidths + ((measured.Length - 1) * EntrySpacing);
            var height = LegendPadding * 2 + entryHeight;
            if (chart.LegendPosition == ChartLegendPosition.Top)
            {
                bounds = new LayoutRect(plot.X, plot.Y, Math.Min(width, plot.Width), height);
                if (!chart.LegendOverlay)
                    plot = new PlotRect(plot.X, plot.Y + height, plot.Width, Math.Max(0, plot.Height - height));
            }
            else
            {
                bounds = new LayoutRect(plot.X, plot.Bottom - height, Math.Min(width, plot.Width), height);
                if (!chart.LegendOverlay)
                    plot = new PlotRect(plot.X, plot.Y, plot.Width, Math.Max(0, plot.Height - height));
            }
        }

        var entries = BuildEntries(measured, bounds, isVertical, entryHeight);
        return new LegendLayout
        {
            Position = chart.LegendPosition,
            Bounds = bounds,
            Entries = entries,
        };
    }

    private static IReadOnlyList<LegendEntry> BuildEntries(
        (int SeriesIndex, string Label, double Width, double Height)[] measured,
        LayoutRect bounds,
        bool isVertical,
        double entryHeight)
    {
        var entries = new List<LegendEntry>(measured.Length);
        if (isVertical)
        {
            var y = bounds.Y + LegendPadding;
            foreach (var m in measured)
            {
                var swatch = new LayoutRect(bounds.X + LegendPadding, y + (entryHeight - SwatchSize) / 2, SwatchSize, SwatchSize);
                var label = new LayoutRect(swatch.Right + SwatchLabelGap, y + (entryHeight - m.Height) / 2, m.Width, m.Height);
                entries.Add(new LegendEntry(m.SeriesIndex, m.Label, swatch, label));
                y += entryHeight + EntrySpacing;
            }
        }
        else
        {
            var x = bounds.X + LegendPadding;
            var y = bounds.Y + LegendPadding;
            foreach (var m in measured)
            {
                var swatch = new LayoutRect(x, y + (entryHeight - SwatchSize) / 2, SwatchSize, SwatchSize);
                var label = new LayoutRect(swatch.Right + SwatchLabelGap, y + (entryHeight - m.Height) / 2, m.Width, m.Height);
                entries.Add(new LegendEntry(m.SeriesIndex, m.Label, swatch, label));
                x = label.Right + EntrySpacing;
            }
        }

        return entries;
    }

    private static IReadOnlyList<(int SeriesIndex, string Label)> CollectLabels(ChartLayoutRequest request)
    {
        var chart = request.Chart;
        // Pie/doughnut legends list categories, not series.
        if (chart.Type is ChartType.Pie or ChartType.ThreeDPie or ChartType.Doughnut)
        {
            var labels = new List<(int, string)>();
            for (var i = 0; i < request.Categories.Count; i++)
            {
                if (ChartRenderPolicyPlanner.IsPieLegendEntryDeleted(chart, i))
                    continue;
                labels.Add((i, request.Categories[i]));
            }
            return labels;
        }

        var entries = new List<(int, string)>(request.Series.Count);
        foreach (var series in request.Series)
        {
            if (ChartRenderPolicyPlanner.IsLegendEntryDeleted(chart, series.SeriesIndex))
                continue;
            entries.Add((series.SeriesIndex, series.Name ?? $"Series {series.SeriesIndex + 1}"));
        }
        return entries;
    }

}
