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
        var legend = BuildAutomatic(request, out plot);
        return ApplyManualLayouts(request, legend, ref plot);
    }

    private static LegendLayout ApplyManualLayouts(ChartLayoutRequest request, LegendLayout legend, ref PlotRect plot)
    {
        var chart = request.Chart;
        var w = request.ChartWidth;
        var h = request.ChartHeight;
        if (!(w > 0) || !(h > 0))
            return legend;

        // A dragged legend no longer owns a gutter: the plot reclaims the space (Excel keeps the plot
        // where the user left it, which is the automatic plot when it was not dragged too).
        if (legend.Position != ChartLegendPosition.None &&
            legend.Entries.Count > 0 &&
            ChartManualLayoutGeometry.TryResolveOrigin(chart.LegendLayout, w, h, new(legend.Bounds.Left, legend.Bounds.Top), out var origin))
        {
            var moved = ChartManualLayoutGeometry.ClampInside(
                new(origin.X, origin.Y, legend.Bounds.Width, legend.Bounds.Height), w, h);
            var dx = moved.Left - legend.Bounds.Left;
            var dy = moved.Top - legend.Bounds.Top;
            var entries = legend.Entries
                .Select(e => e with { SwatchRect = Shift(e.SwatchRect, dx, dy), LabelRect = Shift(e.LabelRect, dx, dy) })
                .ToList();
            legend = new LegendLayout { Position = legend.Position, Bounds = moved, Entries = entries };
            plot = request.PlotArea;
        }

        if (chart.PlotAreaLayout is { } plotLayout &&
            string.Equals(plotLayout.LayoutTarget, ChartManualLayoutGeometry.InnerTarget, StringComparison.OrdinalIgnoreCase) &&
            ChartManualLayoutGeometry.TryResolveRect(plotLayout, w, h, plot.ToRect(), out var rect) &&
            rect.Width > 1 && rect.Height > 1)
        {
            plot = new PlotRect(rect.Left, rect.Top, rect.Width, rect.Height);
        }

        return legend;
    }

    private static LayoutRect Shift(LayoutRect r, double dx, double dy) => new(r.X + dx, r.Y + dy, r.Width, r.Height);

    private static LegendLayout BuildAutomatic(ChartLayoutRequest request, out PlotRect plot)
    {
        var chart = request.Chart;
        plot = request.PlotArea;

        if (!chart.ShowLegend || chart.LegendPosition == ChartLegendPosition.None)
            return LegendLayout.None;

        var labels = CollectLabels(request);
        if (labels.Count == 0)
            return LegendLayout.None;

        // Measure each label to size the legend.
        var measured = new (int SeriesIndex, string Label, double Width, double Height, ChartModel? Trend)[labels.Count];
        var maxLabelWidth = 0.0;
        var maxLabelHeight = 0.0;
        var totalLabelWidth = 0.0;
        for (var i = 0; i < labels.Count; i++)
        {
            var size = request.TextMeasurer.Measure(labels[i].Label, null, chart.LegendFontSize, false, false);
            measured[i] = (labels[i].SeriesIndex, labels[i].Label, size.Width, size.Height, labels[i].Trend);
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

        // A host whose own renderer already settled the plot rectangle (WPF/OxyPlot) lays the series out
        // inside exactly that rectangle instead of letting the portable legend gutter shrink it again.
        if (!request.ReserveLegendGutter)
            plot = request.PlotArea;

        return new LegendLayout
        {
            Position = chart.LegendPosition,
            Bounds = bounds,
            Entries = entries,
        };
    }

    private static IReadOnlyList<LegendEntry> BuildEntries(
        (int SeriesIndex, string Label, double Width, double Height, ChartModel? Trend)[] measured,
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
                entries.Add(new LegendEntry(m.SeriesIndex, m.Label, swatch, label) { IsTrendline = m.Trend is not null, TrendlineStyle = m.Trend });
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
                entries.Add(new LegendEntry(m.SeriesIndex, m.Label, swatch, label) { IsTrendline = m.Trend is not null, TrendlineStyle = m.Trend });
                x = label.Right + EntrySpacing;
            }
        }

        return entries;
    }

    private static IReadOnlyList<(int SeriesIndex, string Label, ChartModel? Trend)> CollectLabels(ChartLayoutRequest request)
    {
        var chart = request.Chart;
        // Pie/doughnut legends list categories, not series.
        if (chart.Type is ChartType.Pie or ChartType.ThreeDPie or ChartType.Doughnut)
        {
            var labels = new List<(int, string, ChartModel?)>();
            for (var i = 0; i < request.Categories.Count; i++)
            {
                if (ChartRenderPolicyPlanner.IsPieLegendEntryDeleted(chart, i))
                    continue;
                labels.Add((i, request.Categories[i], null));
            }
            return labels;
        }

        var entries = new List<(int, string, ChartModel?)>(request.Series.Count);
        foreach (var series in request.Series)
        {
            if (ChartRenderPolicyPlanner.IsLegendEntryDeleted(chart, series.SeriesIndex))
                continue;
            entries.Add((series.SeriesIndex, series.Name ?? $"Series {series.SeriesIndex + 1}", null));
        }

        // Excel lists every trendline after the series ("Linear (Sales)", "Expon. (Sales)", ...).
        if (ChartTypeSupport.SupportsTrendlines(chart.Type))
        {
            foreach (var series in request.Series)
            {
                if (chart.ResolveTrendline(series.SeriesIndex) is not { } trend || CountPlottedValues(series) < 2)
                    continue;
                entries.Add((
                    series.SeriesIndex,
                    TrendlineAnnotationFormatter.GetLegendName(trend, series.Name ?? $"Series {series.SeriesIndex + 1}"),
                    trend));
            }
        }

        return entries;
    }

    private static int CountPlottedValues(ChartSeriesData series)
    {
        var count = 0;
        foreach (var value in series.Values)
        {
            if (value is { } v && !double.IsNaN(v))
                count++;
        }

        return count;
    }
}
