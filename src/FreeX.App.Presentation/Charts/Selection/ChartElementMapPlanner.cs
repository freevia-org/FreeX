using Free.Shared.Drawing;
using FreeX.App.Presentation.Text;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Charts.Selection;

/// <summary>
/// Optional geometry a host's own renderer already decided (the WPF host lays charts out with OxyPlot,
/// so the title/legend/axis gutters are authoritative there). Every member left null is derived from
/// the portable <see cref="ChartLayout"/> using the same constants the Avalonia renderer draws with.
/// </summary>
public sealed class ChartElementFrame
{
    public LayoutRect? TitleBounds { get; init; }

    public LayoutRect? LegendBounds { get; init; }

    /// <summary>The plot rectangle plus the axis label/title gutters around it.</summary>
    public LayoutRect? PlotAndAxisBounds { get; init; }
}

/// <summary>
/// Computes the <see cref="ChartElementMap"/> (selectable element rectangles) from a chart's portable
/// <see cref="ChartLayout"/>. Pure geometry: no UI types, deterministic given a text measurer.
/// </summary>
public static class ChartElementMapPlanner
{
    // Mirror AvaloniaChartRenderer's drawing constants so hit regions line up with what is painted.
    private const double TickLength = 4;
    private const double DefaultAxisLabelFontSize = 10;
    private const double DefaultAxisTitleFontSize = 11;
    private const double ChartTitleTopMargin = 4;
    private const double DefaultChartTitleFontSize = 16;
    private const double AxisLinePadding = 4;
    private const double GridlineTolerance = 3;
    private const double SeriesLineTolerance = 5;
    private const double ErrorBarTolerance = 3;
    private const double MarkerHitRadius = 6;
    private const double TitlePadding = 2;

    public static ChartElementMap Plan(
        ChartModel chart,
        ChartLayout? layout,
        double width,
        double height,
        ITextMeasurer measurer,
        ChartElementFrame? frame = null,
        LayoutRect? plotOverride = null)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(measurer);

        var regions = new List<ChartElementRegion>();
        var plot = layout?.PlotArea ?? plotOverride ?? frame?.PlotAndAxisBounds ?? new LayoutRect(0, 0, width, height);

        AddDataLabels(regions, layout);
        AddChartTitle(regions, chart, plot, width, measurer, frame);
        AddLegend(regions, layout, frame);

        if (layout is not null)
        {
            AddAxisTitles(regions, chart, layout, plot, measurer, frame);
            AddAxes(regions, chart, layout, plot, measurer, frame);
            AddOverlays(regions, layout);
            AddSeries(regions, layout);
            AddGridlines(regions, chart, layout);
            regions.Add(Region(ChartElementKind.PlotArea, new RectHitShape(plot)));
        }
        else
        {
            regions.Add(Region(ChartElementKind.PlotArea, new RectHitShape(plot)));
        }

        var nested = new List<ChartElementRegion>();
        var legendLabels = new Dictionary<int, string?>();
        AddNestedLegendEntries(nested, legendLabels, layout, frame);
        AddNestedDataPoints(nested, layout);

        return new ChartElementMap(width, height, regions)
        {
            SeriesNames = CollectSeriesNames(layout),
            NestedRegions = nested,
            LegendEntryLabels = legendLabels,
        };
    }

    private static Dictionary<int, string?> CollectSeriesNames(ChartLayout? layout)
    {
        var names = new Dictionary<int, string?>();
        if (layout is null)
            return names;

        foreach (var series in layout.Series)
            names.TryAdd(series.SeriesIndex, series.Name);
        return names;
    }

    /// <summary>True when the category axis runs vertically (the horizontal bar family).</summary>
    public static bool IsBarFamily(ChartType type) =>
        type is ChartType.Bar or ChartType.StackedBar or ChartType.PercentStackedBar or ChartType.ThreeDBar;

    private static ChartElementRegion Region(ChartElementKind kind, params ChartHitShape[] shapes) =>
        new(new ChartElementRef(kind), shapes);

    // ---- Title / legend -------------------------------------------------------------------

    private static void AddChartTitle(
        List<ChartElementRegion> regions,
        ChartModel chart,
        LayoutRect plot,
        double width,
        ITextMeasurer measurer,
        ChartElementFrame? frame)
    {
        if (string.IsNullOrWhiteSpace(chart.Title))
            return;

        LayoutRect rect;
        if (frame?.TitleBounds is { } given)
        {
            rect = given;
        }
        else
        {
            var fontSize = chart.ChartTitleFontSize > 0 ? chart.ChartTitleFontSize : DefaultChartTitleFontSize;
            var size = measurer.Measure(chart.Title, null, fontSize, true, false);
            var naturalHeight = size.Height > 0 ? size.Height : fontSize + 4;
            var fitted = ChartTitleFit.ResolveFittingFontSize(
                fontSize, naturalHeight, Math.Max(0, plot.Top - ChartTitleTopMargin));
            if (fitted < fontSize)
                size = measurer.Measure(chart.Title, null, fitted, true, false);

            var w = size.Width > 0 ? size.Width : width * 0.8;
            var h = size.Height > 0 ? size.Height : fitted + 4;
            rect = new LayoutRect((width - w) / 2, ChartTitleTopMargin, w, h);
        }

        regions.Add(Region(ChartElementKind.ChartTitle, new RectHitShape(rect, TitlePadding)));
    }

    private static void AddLegend(List<ChartElementRegion> regions, ChartLayout? layout, ChartElementFrame? frame)
    {
        if (frame?.LegendBounds is { } given)
        {
            regions.Add(Region(ChartElementKind.Legend, new RectHitShape(given)));
            return;
        }

        var legend = layout?.Legend;
        if (legend is null || legend.Position == ChartLegendPosition.None || legend.Entries.Count == 0)
            return;

        regions.Add(Region(ChartElementKind.Legend, new RectHitShape(legend.Bounds)));
    }

    // ---- Axes -----------------------------------------------------------------------------

    private static void AddAxisTitles(
        List<ChartElementRegion> regions,
        ChartModel chart,
        ChartLayout layout,
        LayoutRect plot,
        ITextMeasurer measurer,
        ChartElementFrame? frame)
    {
        AddAxisTitle(regions, chart, layout.ValueAxis, ChartElementKind.ValueAxisTitle, plot, measurer, frame);
        AddAxisTitle(regions, chart, layout.CategoryAxis, ChartElementKind.CategoryAxisTitle, plot, measurer, frame);
        if (layout.SecondaryValueAxis is { } secondary &&
            !string.IsNullOrWhiteSpace(chart.SecondaryAxisTitle))
        {
            AddAxisTitle(regions, chart, secondary, ChartElementKind.SecondaryAxisTitle, plot, measurer, frame);
        }
    }

    private static void AddAxisTitle(
        List<ChartElementRegion> regions,
        ChartModel chart,
        AxisLayout? axis,
        ChartElementKind kind,
        LayoutRect plot,
        ITextMeasurer measurer,
        ChartElementFrame? frame)
    {
        if (axis is null || string.IsNullOrWhiteSpace(axis.Title))
            return;

        var fontSize = chart.AxisTitleFontSize > 0 ? chart.AxisTitleFontSize : DefaultAxisTitleFontSize;
        var size = measurer.Measure(axis.Title, null, fontSize, true, false);
        var textW = size.Width > 0 ? size.Width : 60;
        var textH = size.Height > 0 ? size.Height : fontSize + 4;
        var horizontal = axis.Side is AxisSide.Bottom or AxisSide.Top;
        var outer = frame?.PlotAndAxisBounds;

        LayoutRect rect;
        if (horizontal)
        {
            var centerX = (plot.Left + plot.Right) / 2;
            double top;
            if (outer is { } o)
                top = axis.Side == AxisSide.Bottom ? o.Bottom - textH - TitlePadding : o.Top + TitlePadding;
            else
                top = axis.LinePosition + TickLength + 14 + 2;
            rect = new LayoutRect(centerX - (textW / 2), top, textW, textH);
        }
        else
        {
            var centerY = (plot.Top + plot.Bottom) / 2;
            double left;
            if (outer is { } o)
                left = axis.Side == AxisSide.Left ? o.Left + TitlePadding : o.Right - textH - TitlePadding;
            else
                left = axis.Side == AxisSide.Right
                    ? axis.LinePosition + TickLength + 18 - textH
                    : axis.LinePosition - TickLength - 18 - textH;
            rect = new LayoutRect(left, centerY - (textW / 2), textH, textW);
        }

        regions.Add(Region(kind, new RectHitShape(rect, TitlePadding)));
    }

    private static void AddAxes(
        List<ChartElementRegion> regions,
        ChartModel chart,
        ChartLayout layout,
        LayoutRect plot,
        ITextMeasurer measurer,
        ChartElementFrame? frame)
    {
        // Secondary first: it occupies the opposite edge and never overlaps the primary bands.
        if (layout.SecondaryValueAxis is { } secondary)
            AddAxis(regions, chart, secondary, ChartElementKind.SecondaryValueAxis, plot, measurer, frame);
        if (layout.ValueAxis is { } value)
            AddAxis(regions, chart, value, ChartElementKind.ValueAxis, plot, measurer, frame);
        if (layout.CategoryAxis is { } category)
            AddAxis(regions, chart, category, ChartElementKind.CategoryAxis, plot, measurer, frame);
    }

    private static void AddAxis(
        List<ChartElementRegion> regions,
        ChartModel chart,
        AxisLayout axis,
        ChartElementKind kind,
        LayoutRect plot,
        ITextMeasurer measurer,
        ChartElementFrame? frame)
    {
        var horizontal = axis.Side is AxisSide.Bottom or AxisSide.Top;
        var labelsShown = horizontal ? chart.ShowXAxisLabels : chart.ShowYAxisLabels;
        var configuredFontSize = horizontal ? chart.XAxisLabelFontSize : chart.YAxisLabelFontSize;
        var fontSize = configuredFontSize > 0 ? configuredFontSize : DefaultAxisLabelFontSize;

        var labelExtent = 0.0;
        if (labelsShown)
        {
            foreach (var tick in axis.Ticks)
            {
                if (string.IsNullOrEmpty(tick.Label))
                    continue;
                var size = measurer.Measure(tick.Label, null, fontSize, false, false);
                labelExtent = Math.Max(labelExtent, horizontal ? Math.Max(size.Height, fontSize + 2) : size.Width);
            }
        }

        var band = TickLength + 3 + labelExtent;
        var line = axis.LinePosition;
        var outer = frame?.PlotAndAxisBounds;
        LayoutRect rect;
        var outerLeft = outer?.Left ?? line - band;
        var outerRight = outer?.Right ?? line + band;
        var outerTop = outer?.Top ?? line - band;
        var outerBottom = outer?.Bottom ?? line + band;
        switch (axis.Side)
        {
            case AxisSide.Left:
                rect = LayoutRect.FromCorners(outerLeft, plot.Top - 6, line + AxisLinePadding, plot.Bottom + 6);
                break;
            case AxisSide.Right:
                rect = LayoutRect.FromCorners(line - AxisLinePadding, plot.Top - 6, outerRight, plot.Bottom + 6);
                break;
            case AxisSide.Top:
                rect = LayoutRect.FromCorners(plot.Left - 6, outerTop, plot.Right + 6, line + AxisLinePadding);
                break;
            default:
                rect = LayoutRect.FromCorners(plot.Left - 6, line - AxisLinePadding, plot.Right + 6, outerBottom);
                break;
        }

        regions.Add(Region(kind, new RectHitShape(rect)));
    }

    // ---- Gridlines ------------------------------------------------------------------------

    private static void AddGridlines(List<ChartElementRegion> regions, ChartModel chart, ChartLayout layout)
    {
        AddGridlines(regions, chart, layout.CategoryAxis, ChartElementKind.CategoryGridlines, layout.PlotArea);
        AddGridlines(regions, chart, layout.ValueAxis, ChartElementKind.ValueGridlines, layout.PlotArea);
    }

    private static void AddGridlines(
        List<ChartElementRegion> regions,
        ChartModel chart,
        AxisLayout? axis,
        ChartElementKind kind,
        LayoutRect plot)
    {
        if (axis is null || plot.Width <= 0 || plot.Height <= 0)
            return;

        var horizontal = axis.Side is AxisSide.Bottom or AxisSide.Top;
        if (!(horizontal ? chart.ShowXAxisMajorGridlines : chart.ShowYAxisMajorGridlines))
            return;

        var segments = new List<(LayoutPoint, LayoutPoint)>(axis.Ticks.Count);
        foreach (var tick in axis.Ticks)
        {
            if (!double.IsFinite(tick.Position))
                continue;
            segments.Add(horizontal
                ? (new LayoutPoint(tick.Position, plot.Top), new LayoutPoint(tick.Position, plot.Bottom))
                : (new LayoutPoint(plot.Left, tick.Position), new LayoutPoint(plot.Right, tick.Position)));
        }

        if (segments.Count > 0)
            regions.Add(Region(kind, new SegmentsHitShape(segments, GridlineTolerance)));
    }

    // ---- Series / data labels -------------------------------------------------------------

    private static void AddDataLabels(List<ChartElementRegion> regions, ChartLayout? layout)
    {
        if (layout is null || layout.DataLabels.Count == 0)
            return;

        foreach (var group in layout.DataLabels.GroupBy(l => l.SeriesIndex))
        {
            var shapes = group.Select(l => (ChartHitShape)new RectHitShape(l.Bounds)).ToList();
            regions.Add(new ChartElementRegion(ChartElementRef.ForDataLabels(group.Key), shapes));
        }
    }

    /// <summary>
    /// Trendlines and error bars are drawn over their series, so they win the hit test (Excel lets a
    /// thin trendline or whisker be picked even where it crosses a bar or marker).
    /// </summary>
    private static void AddOverlays(List<ChartElementRegion> regions, ChartLayout layout)
    {
        for (var i = layout.Series.Count - 1; i >= 0; i--)
        {
            var series = layout.Series[i];

            if (series.ErrorBars is { Whiskers.Count: > 0 } errorBars)
            {
                var segments = new List<(LayoutPoint, LayoutPoint)>();
                foreach (var w in errorBars.Whiskers)
                {
                    if (w.HasPlus)
                    {
                        segments.Add((w.Center, w.PlusEnd));
                        if (errorBars.EndCaps)
                            segments.Add((w.PlusCapStart, w.PlusCapEnd));
                    }

                    if (w.HasMinus)
                    {
                        segments.Add((w.Center, w.MinusEnd));
                        if (errorBars.EndCaps)
                            segments.Add((w.MinusCapStart, w.MinusCapEnd));
                    }
                }

                segments.RemoveAll(sg => !IsFinite(sg.Item1) || !IsFinite(sg.Item2));
                if (segments.Count > 0)
                {
                    regions.Add(new ChartElementRegion(
                        ChartElementRef.ForErrorBars(series.SeriesIndex),
                        [new SegmentsHitShape(segments, ErrorBarTolerance)]));
                }
            }

            if (series.Trendline is { Points.Count: >= 2 } trendline)
            {
                regions.Add(new ChartElementRegion(
                    ChartElementRef.ForTrendline(series.SeriesIndex),
                    [SegmentsHitShape.FromPolyline(trendline.Points, SeriesLineTolerance)]));
            }
        }
    }

    private static bool IsFinite(LayoutPoint p) => double.IsFinite(p.X) && double.IsFinite(p.Y);

    // ---- Nested (second-click) elements ---------------------------------------------------

    private static void AddNestedLegendEntries(
        List<ChartElementRegion> nested,
        Dictionary<int, string?> labels,
        ChartLayout? layout,
        ChartElementFrame? frame)
    {
        var legend = layout?.Legend;
        if (legend is null || legend.Position == ChartLegendPosition.None || legend.Entries.Count == 0)
            return;

        var rects = new List<LayoutRect>(legend.Entries.Count);
        if (frame?.LegendBounds is { } host)
        {
            // The host's own legend (OxyPlot) decided where the legend sits, so the portable per-entry
            // rectangles are meaningless: divide the host legend among the entries instead.
            var count = legend.Entries.Count;
            var vertical = legend.Position is ChartLegendPosition.Left or ChartLegendPosition.Right or ChartLegendPosition.TopRight;
            if (vertical)
            {
                var step = host.Height / count;
                for (var i = 0; i < count; i++)
                    rects.Add(new LayoutRect(host.Left, host.Top + (i * step), host.Width, step));
            }
            else
            {
                var weights = legend.Entries
                    .Select(e => Math.Max(1.0, e.LabelRect.Width + e.SwatchRect.Width))
                    .ToList();
                var total = weights.Sum();
                var x = host.Left;
                foreach (var weight in weights)
                {
                    var w = host.Width * weight / total;
                    rects.Add(new LayoutRect(x, host.Top, w, host.Height));
                    x += w;
                }
            }
        }
        else
        {
            foreach (var entry in legend.Entries)
            {
                var left = Math.Min(entry.SwatchRect.Left, entry.LabelRect.Left);
                var top = Math.Min(entry.SwatchRect.Top, entry.LabelRect.Top);
                var right = Math.Max(entry.SwatchRect.Right, entry.LabelRect.Right);
                var bottom = Math.Max(entry.SwatchRect.Bottom, entry.LabelRect.Bottom);
                rects.Add(LayoutRect.FromCorners(left, top, right, bottom));
            }
        }

        for (var i = 0; i < legend.Entries.Count; i++)
        {
            var entry = legend.Entries[i];
            labels[entry.SeriesIndex] = entry.Label;
            nested.Add(new ChartElementRegion(
                ChartElementRef.ForLegendEntry(entry.SeriesIndex),
                [new RectHitShape(rects[i], 1)]));
        }
    }

    private static void AddNestedDataPoints(List<ChartElementRegion> nested, ChartLayout? layout)
    {
        if (layout is null)
            return;

        foreach (var series in layout.Series)
        {
            var byPoint = new SortedDictionary<int, List<ChartHitShape>>();

            void Add(int pointIndex, ChartHitShape shape)
            {
                if (!byPoint.TryGetValue(pointIndex, out var list))
                    byPoint[pointIndex] = list = [];
                list.Add(shape);
            }

            foreach (var bar in series.Bars)
                Add(bar.PointIndex, new RectHitShape(bar.Rect, 1));
            foreach (var slice in series.Slices)
                Add(slice.PointIndex, new ArcHitShape(slice.Arc));
            foreach (var bubble in series.Bubbles)
                Add(bubble.PointIndex, new CirclesHitShape([(bubble.Center, Math.Max(bubble.Radius, 3.0))]));
            foreach (var stock in series.StockElements)
            {
                Add(stock.PointIndex, new RectHitShape(
                    LayoutRect.FromCorners(stock.X, Math.Min(stock.HighY, stock.LowY), stock.X, Math.Max(stock.HighY, stock.LowY)),
                    MarkerHitRadius));
            }

            if (series.Bars.Count == 0 && series.Slices.Count == 0 && series.Bubbles.Count == 0 && series.StockElements.Count == 0)
            {
                foreach (var point in series.Points)
                {
                    if (IsFinite(point.Position))
                        Add(point.PointIndex, new CirclesHitShape([(point.Position, MarkerHitRadius)]));
                }
            }

            foreach (var (pointIndex, shapes) in byPoint)
                nested.Add(new ChartElementRegion(ChartElementRef.ForDataPoint(series.SeriesIndex, pointIndex), shapes));
        }
    }

    private static void AddSeries(List<ChartElementRegion> regions, ChartLayout layout)
    {
        // Later series paint on top, so they win the hit test.
        for (var i = layout.Series.Count - 1; i >= 0; i--)
        {
            var series = layout.Series[i];
            var shapes = BuildSeriesShapes(series);
            if (shapes.Count > 0)
                regions.Add(new ChartElementRegion(ChartElementRef.ForSeries(series.SeriesIndex), shapes));
        }
    }

    private static List<ChartHitShape> BuildSeriesShapes(SeriesLayout series)
    {
        var shapes = new List<ChartHitShape>();

        foreach (var bar in series.Bars)
            shapes.Add(new RectHitShape(bar.Rect, 1));

        foreach (var slice in series.Slices)
            shapes.Add(new ArcHitShape(slice.Arc));

        foreach (var cell in series.SurfaceCells)
            shapes.Add(new RectHitShape(cell.Rect));

        if (series.Bubbles.Count > 0)
        {
            shapes.Add(new CirclesHitShape(series.Bubbles
                .Select(b => (b.Center, Math.Max(b.Radius, 3.0)))
                .ToList()));
        }

        foreach (var stock in series.StockElements)
        {
            shapes.Add(new RectHitShape(
                LayoutRect.FromCorners(stock.X, Math.Min(stock.HighY, stock.LowY), stock.X, Math.Max(stock.HighY, stock.LowY)),
                MarkerHitRadius));
        }

        if (series.Points.Count > 0)
        {
            var points = series.Points.Select(p => p.Position).ToList();
            switch (series.Kind)
            {
                case SeriesGeometryKind.Area when series.Points.Count >= 2:
                    shapes.Add(BuildAreaPolygon(series, points));
                    break;
                case SeriesGeometryKind.ScatterPoints:
                    shapes.Add(new CirclesHitShape(points.Select(p => (p, MarkerHitRadius)).ToList()));
                    break;
                default:
                    shapes.Add(SegmentsHitShape.FromPolyline(
                        points, SeriesLineTolerance, closed: series.Kind == SeriesGeometryKind.RadarPolyline));
                    shapes.Add(new CirclesHitShape(points
                        .Where(p => double.IsFinite(p.X) && double.IsFinite(p.Y))
                        .Select(p => (p, MarkerHitRadius))
                        .ToList()));
                    break;
            }
        }

        return shapes;
    }

    private static PolygonHitShape BuildAreaPolygon(SeriesLayout series, List<LayoutPoint> top)
    {
        var polygon = new List<LayoutPoint>(top.Count * 2);
        polygon.AddRange(top.Where(p => double.IsFinite(p.X) && double.IsFinite(p.Y)));

        if (series.BaselinePoints.Count > 0)
        {
            for (var i = series.BaselinePoints.Count - 1; i >= 0; i--)
            {
                var p = series.BaselinePoints[i].Position;
                if (double.IsFinite(p.X) && double.IsFinite(p.Y))
                    polygon.Add(p);
            }
        }
        else
        {
            for (var i = top.Count - 1; i >= 0; i--)
            {
                if (double.IsFinite(top[i].X))
                    polygon.Add(new LayoutPoint(top[i].X, series.AreaBaseline));
            }
        }

        return new PolygonHitShape(polygon);
    }
}
