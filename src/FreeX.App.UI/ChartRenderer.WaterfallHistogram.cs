using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;

namespace FreeX.App.UI;

public static partial class ChartRenderer
{
    internal static PlotModel BuildWaterfallModel(
        ChartModel chart,
        PlotModel model,
        Dictionary<(uint Row, uint Col), DisplayCell> cellLookup,
        List<string> categories,
        uint dataStartRow, uint endRow, uint dataStartCol,
        WorkbookTheme theme)
    {
        // Collect values from the first data column
        var values = new List<double>();
        for (uint r = dataStartRow; r <= endRow; r++)
        {
            if (cellLookup.TryGetValue((r, dataStartCol), out var cell) &&
                TryGetChartNumericValue(cell, out var v))
                values.Add(v);
            else
                values.Add(0);
        }

        int n = values.Count;
        var bars = new RectangleBarSeries { FillColor = OxyColors.Transparent };
        var connectors = chart.ShowSeriesLines
            ? CreateSeriesLineConnectorSeries(chart, theme)
            : null;

        // Column geometry/classification (increase / decrease / total anchor) is decided by the pure,
        // unit-tested WaterfallBarPlanner; the renderer only draws the resulting bars and connectors.
        var plan = WaterfallBarPlanner.Compute(
            values,
            chart.WaterfallTotalPointIndices,
            WaterfallNullTotalsPolicy.LastPointIsTotal);
        for (int i = 0; i < plan.Count; i++)
        {
            var bar = plan[i];
            var color = ToOxyColor(ChartRenderPolicyPlanner.ResolveWaterfallBarColor(bar.Kind))
                ?? OxyColors.Transparent;

            bars.Items.Add(new RectangleBarItem(i - 0.35, bar.Bottom, i + 0.35, bar.Top) { Color = color });

            // Connect each column to the next at the running-cumulative level (no connector after the
            // final column). Totals are anchors but the running total still flows through them.
            if (i < plan.Count - 1)
                AddWaterfallConnector(connectors, i, bar.CumulativeAfter);
        }

        model.Series.Add(bars);
        if (connectors?.Points.Count > 0)
            model.Series.Add(connectors);

        var categoryAxis = new CategoryAxis
        {
            Position = AxisPosition.Bottom,
            Title = chart.XAxisTitle,
            IsTickCentered = true
        };
        foreach (var cat in categories)
            categoryAxis.Labels.Add(cat);
        if (categoryAxis.Labels.Count == 0)
            for (int i = 0; i < n; i++)
                categoryAxis.Labels.Add($"Point {i + 1}");
        model.Axes.Add(categoryAxis);
        model.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Title = chart.YAxisTitle });

        return model;
    }

    /// <summary>
    /// Builds a <see cref="LineSeries"/> styled from the chart's Series Lines formatting
    /// (<see cref="ChartModel.SeriesLineColor"/>/<see cref="ChartModel.SeriesLineThemeColor"/>/
    /// <see cref="ChartModel.SeriesLineThickness"/>/<see cref="ChartModel.SeriesLineDashStyle"/>).
    /// Shared by the Waterfall "connector" lines and the Stacked Column/Bar "series lines" feature
    /// (<see cref="ChartTypeSupport.SupportsSeriesLines"/>) -- both are the same Excel primitive
    /// (<c>&lt;c:serLines&gt;</c>) applied to different chart shapes.
    /// </summary>
    private static LineSeries CreateSeriesLineConnectorSeries(ChartModel chart, WorkbookTheme theme)
    {
        var color = chart.SeriesLineThemeColor?.Resolve(theme) ?? chart.SeriesLineColor;
        return new LineSeries
        {
            Color = ToOxyColor(color) ?? OxyColors.Gray,
            StrokeThickness = double.IsFinite(chart.SeriesLineThickness)
                ? Math.Clamp(chart.SeriesLineThickness, 0, 20)
                : 1,
            LineStyle = ToOxyLineStyle(chart.SeriesLineDashStyle),
            MarkerType = MarkerType.None
        };
    }

    /// <summary>
    /// Renders Excel's "Series Lines" for Stacked/100%-Stacked Column and Bar charts: a connector
    /// line tracing each series' segment boundary across adjacent categories, so the eye can follow
    /// one series' contribution across the whole stack (<c>&lt;c:serLines&gt;</c> under the chart's
    /// bar-chart plot element). Only called when <see cref="ChartModel.ShowSeriesLines"/> is set and
    /// <see cref="ChartTypeSupport.SupportsSeriesLines"/> allows the chart's type.
    /// </summary>
    /// <param name="isBar">True for horizontal Stacked/100%-Stacked Bar (value axis is X); false for
    /// vertical Stacked/100%-Stacked Column (value axis is Y).</param>
    internal static void AddStackedSeriesLines(PlotModel model, ChartModel chart, WorkbookTheme theme, bool isBar)
    {
        if (!chart.ShowSeriesLines)
            return;

        var rectangleSeries = model.Series.OfType<RectangleBarSeries>().ToList();
        if (rectangleSeries.Count == 0)
            return;

        // One connector line per stacked series, tracing that series' own segment boundary
        // (the edge furthest from the baseline) across categories -- matches Excel's rendering
        // of a single, uniformly-styled series line per series.
        foreach (var series in rectangleSeries)
        {
            if (series.Items.Count < 2)
                continue;

            var connector = CreateSeriesLineConnectorSeries(chart, theme);
            foreach (var item in series.Items)
            {
                // Bar: value axis is X (bottom), category axis is Y (left) -- the segment's
                // outer edge is X1, and the category slot centre is the midpoint of Y0/Y1.
                // Column: value axis is Y (left), category axis is X (bottom) -- mirrored.
                var point = isBar
                    ? new DataPoint(item.X1, (item.Y0 + item.Y1) / 2)
                    : new DataPoint((item.X0 + item.X1) / 2, item.Y1);
                connector.Points.Add(point);
            }

            model.Series.Add(connector);
        }
    }

    private static void AddWaterfallConnector(LineSeries? connectors, int index, double y)
    {
        if (connectors is null)
            return;

        connectors.Points.Add(new DataPoint(index + 0.35, y));
        connectors.Points.Add(new DataPoint(index + 0.65, y));
        connectors.Points.Add(DataPoint.Undefined);
    }

    internal static PlotModel BuildHistogramModel(
        ChartModel chart,
        PlotModel model,
        Dictionary<(uint Row, uint Col), DisplayCell> cellLookup,
        uint dataStartRow, uint endRow, uint dataStartCol,
        WorkbookTheme theme)
    {
        // Collect all numeric values from the first data column
        var rawValues = new List<double>();
        for (uint r = dataStartRow; r <= endRow; r++)
        {
            if (cellLookup.TryGetValue((r, dataStartCol), out var cell) &&
                TryGetChartNumericValue(cell, out var v))
                rawValues.Add(v);
        }

        if (rawValues.Count == 0) return model;

        // Binning (count/width/automatic + overflow/underflow) is decided by the pure, unit-tested
        // HistogramBinPlanner; the renderer just draws the resulting bins. Null settings => Automatic.
        var bins = HistogramBinPlanner.Compute(rawValues, chart.HistogramBinning ?? new HistogramBinningModel());
        if (bins.Count == 0) return model;

        var bars = new RectangleBarSeries
        {
            FillColor = ToOxyColor(ChartRenderPolicyPlanner.WaterfallTotalColor) ?? OxyColors.Transparent
        };
        for (int i = 0; i < bins.Count; i++)
            bars.Items.Add(new RectangleBarItem(i - 0.45, 0, i + 0.45, bins[i].Count));
        model.Series.Add(bars);

        var catAxis = new CategoryAxis { Position = AxisPosition.Bottom, Title = chart.XAxisTitle };
        foreach (var bin in bins)
            catAxis.Labels.Add(bin.Label);
        model.Axes.Add(catAxis);
        model.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Title = chart.YAxisTitle?.Length > 0 ? chart.YAxisTitle : "Frequency" });

        return model;
    }
}
