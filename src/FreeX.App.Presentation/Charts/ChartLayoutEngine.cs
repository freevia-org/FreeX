using System.Globalization;
using FreeX.Core.Model;
using FreeX.App.Presentation.Text;

namespace FreeX.App.Presentation.Charts;

/// <summary>
/// Portable, UI-framework-free chart layout engine. Given a <see cref="ChartLayoutRequest"/> it
/// produces a fully positioned <see cref="ChartLayout"/> (axes, per-series geometry, legend,
/// data labels) in pixel space. The geometry mirrors the source (Windows) renderer's math so both
/// desktop hosts can draw identical charts from one engine.
///
/// Covered chart families: column (incl. stacked / percent-stacked), bar (incl. stacked /
/// percent-stacked), line, area, scatter, pie, doughnut, bubble, radar, and stock
/// (high-low-close / open-high-low-close). Column/line/area/scatter charts additionally support a
/// trendline overlay and a secondary value axis (combo charts). Remaining advanced families
/// (surface, waterfall, histogram, pareto, box-and-whisker, treemap, sunburst, funnel) are not laid
/// out here yet — see the project notes for follow-ups.
/// </summary>
public static class ChartLayoutEngine
{
    // The source renderer centers single-series columns at index ± this half-width and stacked
    // segments at ± 0.35; both are reproduced exactly here.
    private const double DefaultColumnHalfWidth = 0.4;
    private const double StackedColumnHalfWidth = 0.35;

    /// <summary>Returns true when this engine can lay out the given chart type.</summary>
    public static bool IsSupported(ChartType type) =>
        type is ChartType.Column
            or ChartType.StackedColumn
            or ChartType.PercentStackedColumn
            or ChartType.ThreeDColumn
            or ChartType.Bar
            or ChartType.StackedBar
            or ChartType.PercentStackedBar
            or ChartType.ThreeDBar
            or ChartType.Line
            or ChartType.ThreeDLine
            or ChartType.Area
            or ChartType.ThreeDArea
            or ChartType.Scatter
            or ChartType.Bubble
            or ChartType.Radar
            or ChartType.Stock
            or ChartType.Pie
            or ChartType.ThreeDPie
            or ChartType.Doughnut;

    /// <summary>
    /// Lays out <paramref name="request"/> into a <see cref="ChartLayout"/>. Throws
    /// <see cref="NotSupportedException"/> for chart types this engine does not cover (check with
    /// <see cref="IsSupported"/> first).
    /// </summary>
    public static ChartLayout Layout(ChartLayoutRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var chart = request.Chart;

        if (!IsSupported(chart.Type))
            throw new NotSupportedException($"Chart type '{chart.Type}' is not laid out by the portable engine.");

        return chart.Type switch
        {
            ChartType.Pie or ChartType.ThreeDPie or ChartType.Doughnut => LayoutPie(request),
            ChartType.Bar or ChartType.StackedBar or ChartType.PercentStackedBar or ChartType.ThreeDBar => LayoutBar(request),
            ChartType.Scatter => LayoutScatter(request),
            ChartType.Bubble => LayoutBubble(request),
            ChartType.Radar => LayoutRadar(request),
            ChartType.Stock => LayoutStock(request),
            _ => LayoutColumnLineArea(request),
        };
    }

    // ---- Pie / Doughnut -------------------------------------------------------------------

    private static ChartLayout LayoutPie(ChartLayoutRequest request)
    {
        var chart = request.Chart;
        var legend = LegendLayoutBuilder.Build(request, out var plot);
        var series = request.Series.Count > 0 ? request.Series[0] : null;

        var values = new List<(int Index, double Value, string Label)>();
        var total = 0.0;
        if (series is not null)
        {
            for (var i = 0; i < series.Values.Count; i++)
            {
                if (series.Values[i] is not { } v || v <= 0)
                    continue;
                var label = i < request.Categories.Count ? request.Categories[i] : "";
                values.Add((i, v, label));
                total += v;
            }
        }

        var center = plot.ToRect().Center;
        var outerRadius = Math.Max(0, Math.Min(plot.Width, plot.Height) / 2);
        var innerRadius = chart.Type == ChartType.Doughnut
            ? outerRadius * Math.Clamp(chart.DoughnutHoleSize, 0, 0.95)
            : 0;

        var slices = new List<SeriesSlice>(values.Count);
        var dataLabels = new List<DataLabelBox>(values.Count);
        // Angles measured clockwise from 12 o'clock, starting at the chart's first-slice angle.
        var angle = chart.FirstSliceAngle;
        for (var s = 0; s < values.Count; s++)
        {
            var (index, value, label) = values[s];
            var fraction = total > 0 ? value / total : 0;
            var sweep = fraction * 360.0;

            var sliceCenter = center;
            if (chart.ExplodedSliceIndex == index && chart.ExplodedSliceDistance > 0)
            {
                var mid = angle + (sweep / 2);
                var offset = outerRadius * chart.ExplodedSliceDistance;
                sliceCenter = PolarToPixel(center, mid, offset);
            }

            var arc = new LayoutArc(sliceCenter, outerRadius, innerRadius, angle, sweep);
            slices.Add(new SeriesSlice(index, value, fraction, label, arc));

            if (chart.ShowDataLabels)
            {
                var text = BuildPieLabel(chart, series?.Name ?? "", label, value, fraction);
                if (!string.IsNullOrEmpty(text))
                    dataLabels.Add(BuildPieDataLabel(request, arc, index, text));
            }

            angle += sweep;
        }

        var seriesLayout = new SeriesLayout
        {
            SeriesIndex = series?.SeriesIndex ?? 0,
            Name = series?.Name,
            Kind = SeriesGeometryKind.PieSlices,
            Slices = slices,
        };

        return new ChartLayout
        {
            Type = chart.Type,
            PlotArea = plot.ToRect(),
            Series = [seriesLayout],
            Legend = legend,
            DataLabels = dataLabels,
        };
    }

    private static DataLabelBox BuildPieDataLabel(ChartLayoutRequest request, LayoutArc arc, int index, string text)
    {
        var chart = request.Chart;
        // Inside-end / center labels sit at a fraction of the radius; outside-end sits just past it.
        var radiusFraction = chart.DataLabelPosition switch
        {
            ChartDataLabelPosition.Center => 0.5,
            ChartDataLabelPosition.InsideEnd => 0.8,
            ChartDataLabelPosition.OutsideEnd => 1.15,
            _ => 0.8,
        };
        var labelRadius = arc.OuterRadius * radiusFraction;
        var anchor = PolarToPixel(arc.Center, arc.MidAngleDegrees, labelRadius);
        var size = request.TextMeasurer.Measure(text, null, chart.DataLabelFontSize, false, false);
        var bounds = CenteredRect(anchor, size);
        return new DataLabelBox(0, index, text, anchor, bounds);
    }

    // ---- Column / Line / Area (shared category-X, value-Y layout) -------------------------

    private static ChartLayout LayoutColumnLineArea(ChartLayoutRequest request)
    {
        var chart = request.Chart;
        var legend = LegendLayoutBuilder.Build(request, out var plot);
        var categoryCount = ResolveCategoryCount(request);

        var isStacked = chart.Type is ChartType.StackedColumn or ChartType.PercentStackedColumn;
        var isPercent = chart.Type == ChartType.PercentStackedColumn;

        // Category axis: columns center categories over [-0.5, count-0.5]; line/area use [0, count-1].
        var isColumnFamily = chart.Type is ChartType.Column or ChartType.ThreeDColumn
            or ChartType.StackedColumn or ChartType.PercentStackedColumn;
        var (catMin, catMax) = isColumnFamily
            ? (-0.5, Math.Max(0.5, categoryCount - 0.5))
            : (0.0, (double)Math.Max(1, categoryCount - 1));
        var categoryScale = AxisScale.CreateIndexAxis(catMin, catMax, plot, AxisSide.Bottom);

        var (dataMin, dataMax) = isStacked
            ? StackedValueRange(request, categoryCount, isPercent)
            : PlainValueRange(request);

        var valueScale = AxisScale.CreateValueAxis(
            dataMin, dataMax, plot, AxisSide.Left,
            chart.YAxisMinimum, chart.YAxisMaximum, chart.YAxisMajorUnit);

        // Combo charts: a secondary value axis on the right carries the assigned series. Stacked
        // charts do not split across axes (matching the source renderer).
        var useSecondary = !isStacked && WantsSecondaryAxis(request, categoryCount);
        var secondaryScale = useSecondary
            ? AxisScale.CreateValueAxis(SecondaryValueRange(request).Min, SecondaryValueRange(request).Max, plot, AxisSide.Right)
            : null;

        var seriesLayouts = new List<SeriesLayout>(request.Series.Count);
        var dataLabels = new List<DataLabelBox>();
        var baselineY = valueScale.Transform(Clamp0(valueScale));

        if (isStacked)
        {
            LayoutStackedColumns(request, categoryCount, isPercent, categoryScale, valueScale, seriesLayouts);
        }
        else
        {
            foreach (var series in request.Series)
            {
                var onSecondary = useSecondary && UsesSecondaryAxis(chart, series.SeriesIndex);
                var yScale = onSecondary ? secondaryScale! : valueScale;
                var baseY = yScale.Transform(Clamp0(yScale));
                var laid = chart.Type switch
                {
                    ChartType.Column or ChartType.ThreeDColumn =>
                        LayoutColumnSeries(request, series, categoryScale, yScale, baseY, dataLabels),
                    ChartType.Area or ChartType.ThreeDArea =>
                        LayoutAreaSeries(request, series, categoryScale, yScale, baseY, dataLabels),
                    _ => LayoutLineSeries(request, series, categoryScale, yScale, dataLabels),
                };
                seriesLayouts.Add(laid with { UsesSecondaryAxis = onSecondary });
            }
        }

        AttachTrendline(request, seriesLayouts, x => categoryScale.Transform(x), valueScale, secondaryScale, useSecondary);

        return new ChartLayout
        {
            Type = chart.Type,
            PlotArea = plot.ToRect(),
            CategoryAxis = BuildCategoryAxisLayout(request, categoryScale, AxisSide.Bottom, valueScale.Transform(Clamp0(valueScale))),
            ValueAxis = BuildValueAxisLayout(chart, valueScale, AxisSide.Left, plot.Left, chart.YAxisNumberFormat),
            SecondaryValueAxis = secondaryScale is null
                ? null
                : BuildValueAxisLayout(chart, secondaryScale, AxisSide.Right, plot.Right, chart.YAxisNumberFormat),
            Series = seriesLayouts,
            Legend = legend,
            DataLabels = dataLabels,
        };
    }

    private static SeriesLayout LayoutColumnSeries(
        ChartLayoutRequest request,
        ChartSeriesData series,
        AxisScale categoryScale,
        AxisScale valueScale,
        double baselineY,
        List<DataLabelBox> dataLabels)
    {
        var chart = request.Chart;
        var bars = new List<SeriesBar>();
        var half = DefaultColumnHalfWidth;
        for (var i = 0; i < series.Values.Count; i++)
        {
            double v;
            if (series.Values[i] is { } actual)
                v = actual;
            else if (chart.BlankDisplayMode == ChartBlankDisplayMode.Zero)
                v = 0;
            else
                continue;

            // Mirrors RectangleBarItem(i - half, min(0,v), i + half, max(0,v)).
            var x0 = categoryScale.Transform(i - half);
            var x1 = categoryScale.Transform(i + half);
            var yLow = valueScale.Transform(Math.Min(0, v));
            var yHigh = valueScale.Transform(Math.Max(0, v));
            var rect = LayoutRect.FromCorners(x0, yLow, x1, yHigh);
            bars.Add(new SeriesBar(i, v, rect));

            AddCartesianDataLabel(request, dataLabels, series, i, v, new LayoutPoint(rect.Center.X, valueScale.Transform(v)));
        }

        return new SeriesLayout
        {
            SeriesIndex = series.SeriesIndex,
            Name = series.Name,
            Kind = SeriesGeometryKind.Columns,
            Bars = bars,
            AreaBaseline = baselineY,
        };
    }

    private static void LayoutStackedColumns(
        ChartLayoutRequest request,
        int categoryCount,
        bool isPercent,
        AxisScale categoryScale,
        AxisScale valueScale,
        List<SeriesLayout> seriesLayouts)
    {
        var (posTotals, negTotals) = StackedTotals(request, categoryCount);
        var posBases = new double[categoryCount];
        var negBases = new double[categoryCount];
        var half = StackedColumnHalfWidth;

        foreach (var series in request.Series)
        {
            var bars = new List<SeriesBar>();
            for (var i = 0; i < series.Values.Count && i < categoryCount; i++)
            {
                if (series.Values[i] is not { } raw)
                    continue;

                var v = isPercent ? NormalizePercent(raw, i, posTotals, negTotals) : raw;
                var start = v >= 0 ? posBases[i] : negBases[i];
                var end = start + v;

                var x0 = categoryScale.Transform(i - half);
                var x1 = categoryScale.Transform(i + half);
                var yStart = valueScale.Transform(start);
                var yEnd = valueScale.Transform(end);
                bars.Add(new SeriesBar(i, raw, LayoutRect.FromCorners(x0, yStart, x1, yEnd)));

                if (v >= 0) posBases[i] = end; else negBases[i] = end;
            }

            seriesLayouts.Add(new SeriesLayout
            {
                SeriesIndex = series.SeriesIndex,
                Name = series.Name,
                Kind = SeriesGeometryKind.Columns,
                Bars = bars,
            });
        }
    }

    private static SeriesLayout LayoutLineSeries(
        ChartLayoutRequest request,
        ChartSeriesData series,
        AxisScale categoryScale,
        AxisScale valueScale,
        List<DataLabelBox> dataLabels)
    {
        var chart = request.Chart;
        var points = new List<SeriesPoint>();
        for (var i = 0; i < series.Values.Count; i++)
        {
            double v;
            if (series.Values[i] is { } actual)
                v = actual;
            else if (chart.BlankDisplayMode == ChartBlankDisplayMode.Zero)
                v = 0;
            else
                continue; // Gap: skip the point so the line breaks.

            var pos = new LayoutPoint(categoryScale.Transform(i), valueScale.Transform(v));
            points.Add(new SeriesPoint(i, i, v, pos));
            AddCartesianDataLabel(request, dataLabels, series, i, v, pos);
        }

        return new SeriesLayout
        {
            SeriesIndex = series.SeriesIndex,
            Name = series.Name,
            Kind = SeriesGeometryKind.Line,
            Points = points,
        };
    }

    private static SeriesLayout LayoutAreaSeries(
        ChartLayoutRequest request,
        ChartSeriesData series,
        AxisScale categoryScale,
        AxisScale valueScale,
        double baselineY,
        List<DataLabelBox> dataLabels)
    {
        var line = LayoutLineSeries(request, series, categoryScale, valueScale, dataLabels);
        return new SeriesLayout
        {
            SeriesIndex = series.SeriesIndex,
            Name = series.Name,
            Kind = SeriesGeometryKind.Area,
            Points = line.Points,
            AreaBaseline = baselineY,
        };
    }

    // ---- Bar (category-Y, value-X) --------------------------------------------------------

    private static ChartLayout LayoutBar(ChartLayoutRequest request)
    {
        var chart = request.Chart;
        var legend = LegendLayoutBuilder.Build(request, out var plot);
        var categoryCount = ResolveCategoryCount(request);
        var isStacked = chart.Type is ChartType.StackedBar or ChartType.PercentStackedBar;
        var isPercent = chart.Type == ChartType.PercentStackedBar;

        // Category axis on the left; bars sit at integer indices.
        var categoryScale = AxisScale.CreateIndexAxis(-0.5, Math.Max(0.5, categoryCount - 0.5), plot, AxisSide.Left);

        var (dataMin, dataMax) = isStacked
            ? StackedValueRange(request, categoryCount, isPercent)
            : PlainValueRange(request);
        var valueScale = AxisScale.CreateValueAxis(
            dataMin, dataMax, plot, AxisSide.Bottom,
            chart.XAxisMinimum, chart.XAxisMaximum, chart.XAxisMajorUnit);

        var seriesLayouts = new List<SeriesLayout>(request.Series.Count);
        var dataLabels = new List<DataLabelBox>();
        var half = isStacked ? StackedColumnHalfWidth : DefaultColumnHalfWidth;
        var (posTotals, negTotals) = isStacked ? StackedTotals(request, categoryCount) : ([], []);
        var posBases = new double[categoryCount];
        var negBases = new double[categoryCount];
        var baselineX = valueScale.Transform(Clamp0(valueScale));

        foreach (var series in request.Series)
        {
            var bars = new List<SeriesBar>();
            for (var i = 0; i < series.Values.Count && i < categoryCount; i++)
            {
                double raw;
                if (series.Values[i] is { } actual)
                    raw = actual;
                else if (!isStacked && chart.BlankDisplayMode == ChartBlankDisplayMode.Zero)
                    raw = 0;
                else
                    continue;

                double x0;
                double x1;
                if (isStacked)
                {
                    var v = isPercent ? NormalizePercent(raw, i, posTotals, negTotals) : raw;
                    var start = v >= 0 ? posBases[i] : negBases[i];
                    var end = start + v;
                    x0 = valueScale.Transform(start);
                    x1 = valueScale.Transform(end);
                    if (v >= 0) posBases[i] = end; else negBases[i] = end;
                }
                else
                {
                    x0 = valueScale.Transform(Math.Min(0, raw));
                    x1 = valueScale.Transform(Math.Max(0, raw));
                }

                var y0 = categoryScale.Transform(i - half);
                var y1 = categoryScale.Transform(i + half);
                var rect = LayoutRect.FromCorners(x0, y0, x1, y1);
                bars.Add(new SeriesBar(i, raw, rect));

                if (!isStacked)
                    AddCartesianDataLabel(request, dataLabels, series, i, raw, new LayoutPoint(valueScale.Transform(raw), rect.Center.Y));
            }

            seriesLayouts.Add(new SeriesLayout
            {
                SeriesIndex = series.SeriesIndex,
                Name = series.Name,
                Kind = SeriesGeometryKind.Bars,
                Bars = bars,
                AreaBaseline = baselineX,
            });
        }

        return new ChartLayout
        {
            Type = chart.Type,
            PlotArea = plot.ToRect(),
            CategoryAxis = BuildCategoryAxisLayout(request, categoryScale, AxisSide.Left, baselineX),
            ValueAxis = BuildValueAxisLayout(chart, valueScale, AxisSide.Bottom, plot.Bottom, chart.XAxisNumberFormat),
            Series = seriesLayouts,
            Legend = legend,
            DataLabels = dataLabels,
        };
    }

    // ---- Scatter --------------------------------------------------------------------------

    private static ChartLayout LayoutScatter(ChartLayoutRequest request)
    {
        var chart = request.Chart;
        var legend = LegendLayoutBuilder.Build(request, out var plot);

        var (xMin, xMax) = ScatterRange(request, useX: true);
        var (yMin, yMax) = ScatterRange(request, useX: false);

        var xScale = AxisScale.CreateValueAxis(xMin, xMax, plot, AxisSide.Bottom,
            chart.XAxisMinimum, chart.XAxisMaximum, chart.XAxisMajorUnit);
        var yScale = AxisScale.CreateValueAxis(yMin, yMax, plot, AxisSide.Left,
            chart.YAxisMinimum, chart.YAxisMaximum, chart.YAxisMajorUnit);

        var seriesLayouts = new List<SeriesLayout>(request.Series.Count);
        var dataLabels = new List<DataLabelBox>();
        foreach (var series in request.Series)
        {
            var points = new List<SeriesPoint>();
            for (var i = 0; i < series.Values.Count; i++)
            {
                if (series.Values[i] is not { } y)
                    continue;
                var x = series.XValues is { } xs && i < xs.Count ? xs[i] : i;
                var pos = new LayoutPoint(xScale.Transform(x), yScale.Transform(y));
                points.Add(new SeriesPoint(i, x, y, pos));
                AddCartesianDataLabel(request, dataLabels, series, i, y, pos);
            }

            seriesLayouts.Add(new SeriesLayout
            {
                SeriesIndex = series.SeriesIndex,
                Name = series.Name,
                Kind = SeriesGeometryKind.ScatterPoints,
                Points = points,
            });
        }

        AttachScatterTrendline(request, seriesLayouts, xScale, yScale);

        return new ChartLayout
        {
            Type = chart.Type,
            PlotArea = plot.ToRect(),
            CategoryAxis = BuildValueAxisLayout(chart, xScale, AxisSide.Bottom, plot.Bottom, chart.XAxisNumberFormat),
            ValueAxis = BuildValueAxisLayout(chart, yScale, AxisSide.Left, plot.Left, chart.YAxisNumberFormat),
            Series = seriesLayouts,
            Legend = legend,
            DataLabels = dataLabels,
        };
    }

    // ---- Bubble ---------------------------------------------------------------------------

    // The largest pixel radius a bubble is drawn at when its size equals the series maximum, before
    // the chart's BubbleScale is applied. Mirrors the source renderer's default bubble sizing.
    private const double MaxBubbleRadius = 20.0;
    private const double MinBubbleRadius = 1.0;

    private static ChartLayout LayoutBubble(ChartLayoutRequest request)
    {
        var chart = request.Chart;
        var legend = LegendLayoutBuilder.Build(request, out var plot);

        var (xMin, xMax) = ScatterRange(request, useX: true);
        var (yMin, yMax) = ScatterRange(request, useX: false);

        var xScale = AxisScale.CreateValueAxis(xMin, xMax, plot, AxisSide.Bottom,
            chart.XAxisMinimum, chart.XAxisMaximum, chart.XAxisMajorUnit);
        var yScale = AxisScale.CreateValueAxis(yMin, yMax, plot, AxisSide.Left,
            chart.YAxisMinimum, chart.YAxisMaximum, chart.YAxisMajorUnit);

        var maxSize = MaxBubbleSize(request);
        var scale = Math.Max(0, chart.BubbleScale) / 100.0;

        var seriesLayouts = new List<SeriesLayout>(request.Series.Count);
        var dataLabels = new List<DataLabelBox>();
        foreach (var series in request.Series)
        {
            var bubbles = new List<SeriesBubble>();
            for (var i = 0; i < series.Values.Count; i++)
            {
                if (series.Values[i] is not { } y)
                    continue;
                var x = series.XValues is { } xs && i < xs.Count ? xs[i] : i;
                var rawSize = series.SizeValues is { } sizes && i < sizes.Count && sizes[i] is { } s ? s : 1;
                if (rawSize < 0 && !chart.ShowNegativeBubbles)
                    continue;

                var center = new LayoutPoint(xScale.Transform(x), yScale.Transform(y));
                var radius = BubbleRadius(Math.Abs(rawSize), maxSize, chart.BubbleSizeRepresents) * scale;
                bubbles.Add(new SeriesBubble(i, x, y, rawSize, center, radius));
                AddCartesianDataLabel(request, dataLabels, series, i, y, center);
            }

            seriesLayouts.Add(new SeriesLayout
            {
                SeriesIndex = series.SeriesIndex,
                Name = series.Name,
                Kind = SeriesGeometryKind.Bubbles,
                Bubbles = bubbles,
            });
        }

        AttachScatterTrendline(request, seriesLayouts, xScale, yScale);

        return new ChartLayout
        {
            Type = chart.Type,
            PlotArea = plot.ToRect(),
            CategoryAxis = BuildValueAxisLayout(chart, xScale, AxisSide.Bottom, plot.Bottom, chart.XAxisNumberFormat),
            ValueAxis = BuildValueAxisLayout(chart, yScale, AxisSide.Left, plot.Left, chart.YAxisNumberFormat),
            Series = seriesLayouts,
            Legend = legend,
            DataLabels = dataLabels,
        };
    }

    private static double MaxBubbleSize(ChartLayoutRequest request)
    {
        var max = 0.0;
        foreach (var series in request.Series)
        {
            if (series.SizeValues is not { } sizes)
                continue;
            for (var i = 0; i < sizes.Count; i++)
            {
                if (sizes[i] is { } s)
                    max = Math.Max(max, Math.Abs(s));
            }
        }

        return max;
    }

    private static double BubbleRadius(double size, double maxSize, ChartBubbleSizeRepresents represents)
    {
        if (maxSize <= 0)
            return MinBubbleRadius;

        // Area representation keeps the bubble area proportional to the size value, so the radius
        // scales with the square root of the size fraction; Width scales the radius linearly.
        var fraction = Math.Clamp(size / maxSize, 0, 1);
        var radiusFraction = represents == ChartBubbleSizeRepresents.Width ? fraction : Math.Sqrt(fraction);
        return Math.Max(MinBubbleRadius, MaxBubbleRadius * radiusFraction);
    }

    // ---- Radar ----------------------------------------------------------------------------

    private static ChartLayout LayoutRadar(ChartLayoutRequest request)
    {
        var chart = request.Chart;
        var legend = LegendLayoutBuilder.Build(request, out var plot);
        var categoryCount = ResolveCategoryCount(request);
        var spokeCount = Math.Max(1, categoryCount);

        // Radius axis runs from zero (center) out to the data maximum. Mirrors the source renderer's
        // MagnitudeAxis (minimum pinned at zero).
        var (_, dataMax) = PlainValueRange(request);
        var valueMax = chart.YAxisMaximum ?? Math.Max(dataMax, 0);
        if (valueMax <= 0)
            valueMax = 1;

        var center = plot.ToRect().Center;
        var outerRadius = Math.Max(0, Math.Min(plot.Width, plot.Height) / 2);

        // Angle per category, clockwise from 12 o'clock (the source renderer starts the angle axis at
        // the top and steps a full turn across the categories).
        var spokes = new List<RadarSpoke>(spokeCount);
        for (var i = 0; i < spokeCount; i++)
        {
            var angle = i * 360.0 / spokeCount;
            spokes.Add(new RadarSpoke(i, angle, PolarToPixel(center, angle, outerRadius)));
        }

        var seriesLayouts = new List<SeriesLayout>(request.Series.Count);
        foreach (var series in request.Series)
        {
            var points = new List<SeriesPoint>();
            for (var i = 0; i < series.Values.Count && i < spokeCount; i++)
            {
                if (series.Values[i] is not { } v)
                    continue;
                var angle = i * 360.0 / spokeCount;
                var radius = outerRadius * Math.Clamp(v / valueMax, 0, 1);
                points.Add(new SeriesPoint(i, i, v, PolarToPixel(center, angle, radius)));
            }

            // Close the polyline back to the first vertex (matching the source renderer's loop).
            if (points.Count > 1)
                points.Add(points[0] with { PointIndex = points.Count });

            seriesLayouts.Add(new SeriesLayout
            {
                SeriesIndex = series.SeriesIndex,
                Name = series.Name,
                Kind = SeriesGeometryKind.RadarPolyline,
                Points = points,
            });
        }

        return new ChartLayout
        {
            Type = chart.Type,
            PlotArea = plot.ToRect(),
            Radar = new RadarLayout { Center = center, OuterRadius = outerRadius, Spokes = spokes },
            Series = seriesLayouts,
            Legend = legend,
        };
    }

    // ---- Stock ----------------------------------------------------------------------------

    private static ChartLayout LayoutStock(ChartLayoutRequest request)
    {
        var chart = request.Chart;
        var legend = LegendLayoutBuilder.Build(request, out var plot);
        var categoryCount = ResolveCategoryCount(request);

        var categoryScale = AxisScale.CreateIndexAxis(-0.5, Math.Max(0.5, categoryCount - 0.5), plot, AxisSide.Bottom);
        var (dataMin, dataMax) = StockValueRange(request);
        var valueScale = AxisScale.CreateValueAxis(dataMin, dataMax, plot, AxisSide.Left,
            chart.YAxisMinimum, chart.YAxisMaximum, chart.YAxisMajorUnit);

        var seriesLayouts = new List<SeriesLayout>(request.Series.Count);
        foreach (var series in request.Series)
        {
            var elements = new List<StockElement>();
            var highs = series.HighValues;
            var lows = series.LowValues;
            var opens = series.OpenValues;
            var hasOpen = opens is not null;
            if (highs is null || lows is null)
            {
                seriesLayouts.Add(new SeriesLayout
                {
                    SeriesIndex = series.SeriesIndex,
                    Name = series.Name,
                    Kind = SeriesGeometryKind.StockBars,
                    StockElements = elements,
                });
                continue;
            }

            for (var i = 0; i < series.Values.Count && i < categoryCount; i++)
            {
                if (i >= highs.Count || i >= lows.Count
                    || highs[i] is not { } high || lows[i] is not { } low || series.Values[i] is not { } close)
                    continue;

                var open = hasOpen && i < opens!.Count && opens[i] is { } o ? o : close;
                var x = categoryScale.Transform(i);
                elements.Add(new StockElement(
                    i, x,
                    valueScale.Transform(high), valueScale.Transform(low),
                    valueScale.Transform(open), valueScale.Transform(close),
                    open, high, low, close, hasOpen));
            }

            seriesLayouts.Add(new SeriesLayout
            {
                SeriesIndex = series.SeriesIndex,
                Name = series.Name,
                Kind = SeriesGeometryKind.StockBars,
                StockElements = elements,
            });
        }

        return new ChartLayout
        {
            Type = chart.Type,
            PlotArea = plot.ToRect(),
            CategoryAxis = BuildCategoryAxisLayout(request, categoryScale, AxisSide.Bottom, plot.Bottom),
            ValueAxis = BuildValueAxisLayout(chart, valueScale, AxisSide.Left, plot.Left, chart.YAxisNumberFormat),
            Series = seriesLayouts,
            Legend = legend,
        };
    }

    private static (double Min, double Max) StockValueRange(ChartLayoutRequest request)
    {
        var min = double.PositiveInfinity;
        var max = double.NegativeInfinity;
        foreach (var series in request.Series)
        {
            Extend(series.Values);
            Extend(series.HighValues);
            Extend(series.LowValues);
            Extend(series.OpenValues);
        }

        if (double.IsInfinity(min) || double.IsInfinity(max))
            return (0, 1);
        return (min, max);

        void Extend(IReadOnlyList<double?>? values)
        {
            if (values is null)
                return;
            foreach (var value in values)
            {
                if (value is not { } v || double.IsNaN(v))
                    continue;
                min = Math.Min(min, v);
                max = Math.Max(max, v);
            }
        }
    }

    // ---- Axis layout builders -------------------------------------------------------------

    private static AxisLayout BuildValueAxisLayout(
        ChartModel chart,
        AxisScale scale,
        AxisSide side,
        double linePosition,
        ChartDataLabelNumberFormat numberFormat)
    {
        var ticks = new List<AxisTick>();
        foreach (var value in scale.GetMajorTickValues())
        {
            var label = FormatAxisValue(numberFormat, value);
            ticks.Add(new AxisTick(value, scale.Transform(value), label));
        }

        return new AxisLayout
        {
            Side = side,
            Title = side is AxisSide.Bottom or AxisSide.Top ? chart.XAxisTitle : chart.YAxisTitle,
            LinePosition = linePosition,
            Ticks = ticks,
            Scale = scale,
        };
    }

    private static AxisLayout BuildCategoryAxisLayout(
        ChartLayoutRequest request,
        AxisScale scale,
        AxisSide side,
        double linePosition)
    {
        var ticks = new List<AxisTick>();
        for (var i = 0; i < request.Categories.Count; i++)
        {
            ticks.Add(new AxisTick(i, scale.Transform(i), request.Categories[i]));
        }

        return new AxisLayout
        {
            Side = side,
            Title = side is AxisSide.Bottom or AxisSide.Top ? request.Chart.XAxisTitle : request.Chart.YAxisTitle,
            LinePosition = linePosition,
            Ticks = ticks,
            Scale = scale,
        };
    }

    // ---- Value-range helpers --------------------------------------------------------------

    private static (double Min, double Max) PlainValueRange(ChartLayoutRequest request)
    {
        var min = double.PositiveInfinity;
        var max = double.NegativeInfinity;
        foreach (var series in request.Series)
        {
            foreach (var value in series.Values)
            {
                if (value is not { } v || double.IsNaN(v))
                    continue;
                min = Math.Min(min, v);
                max = Math.Max(max, v);
            }
        }

        if (double.IsInfinity(min) || double.IsInfinity(max))
            return (0, 1);
        return (min, max);
    }

    private static (double Min, double Max) StackedValueRange(ChartLayoutRequest request, int categoryCount, bool isPercent)
    {
        if (isPercent)
        {
            var (posTotals, negTotals) = StackedTotals(request, categoryCount);
            var hasPos = posTotals.Any(t => t > 0);
            var hasNeg = negTotals.Any(t => t > 0);
            return (hasNeg ? -100 : 0, hasPos || !hasNeg ? 100 : 0);
        }

        var (pos, neg) = StackedTotals(request, categoryCount);
        var max = pos.Length > 0 ? pos.Max() : 0;
        var min = neg.Length > 0 ? -neg.Max() : 0;
        return (min, max);
    }

    private static (double[] Positive, double[] Negative) StackedTotals(ChartLayoutRequest request, int categoryCount)
    {
        var pos = new double[categoryCount];
        var neg = new double[categoryCount];
        foreach (var series in request.Series)
        {
            for (var i = 0; i < series.Values.Count && i < categoryCount; i++)
            {
                if (series.Values[i] is not { } v)
                    continue;
                if (v >= 0) pos[i] += v; else neg[i] += Math.Abs(v);
            }
        }

        return (pos, neg);
    }

    private static double NormalizePercent(double value, int index, double[] posTotals, double[] negTotals)
    {
        var total = value >= 0 ? posTotals[index] : negTotals[index];
        return total == 0 ? 0 : value / total * 100;
    }

    private static (double Min, double Max) ScatterRange(ChartLayoutRequest request, bool useX)
    {
        var min = double.PositiveInfinity;
        var max = double.NegativeInfinity;
        foreach (var series in request.Series)
        {
            for (var i = 0; i < series.Values.Count; i++)
            {
                double value;
                if (useX)
                    value = series.XValues is { } xs && i < xs.Count ? xs[i] : i;
                else if (series.Values[i] is { } y)
                    value = y;
                else
                    continue;

                if (double.IsNaN(value))
                    continue;
                min = Math.Min(min, value);
                max = Math.Max(max, value);
            }
        }

        if (double.IsInfinity(min) || double.IsInfinity(max))
            return (0, 1);
        return (min, max);
    }

    // ---- Secondary axis (combo) -----------------------------------------------------------

    private static bool WantsSecondaryAxis(ChartLayoutRequest request, int categoryCount)
    {
        var chart = request.Chart;
        if (!chart.ShowSecondaryAxis || !ChartTypeSupport.SupportsSecondaryAxis(chart.Type) || request.Series.Count < 2)
            return false;

        // At least one non-first series must actually map to the secondary axis.
        foreach (var series in request.Series)
        {
            if (UsesSecondaryAxis(chart, series.SeriesIndex))
                return true;
        }

        return false;
    }

    // Mirrors the source renderer: a series uses the secondary axis when the chart enables it, the
    // series is not the first, and either no explicit assignment list is given (all but the first
    // go secondary) or the list contains this series index.
    private static bool UsesSecondaryAxis(ChartModel chart, int seriesIndex)
    {
        if (!chart.ShowSecondaryAxis || seriesIndex <= 0)
            return false;

        return chart.SecondaryAxisSeriesIndexes.Count == 0
            || chart.SecondaryAxisSeriesIndexes.Contains(seriesIndex);
    }

    private static (double Min, double Max) SecondaryValueRange(ChartLayoutRequest request)
    {
        var chart = request.Chart;
        var min = double.PositiveInfinity;
        var max = double.NegativeInfinity;
        foreach (var series in request.Series)
        {
            if (!UsesSecondaryAxis(chart, series.SeriesIndex))
                continue;
            foreach (var value in series.Values)
            {
                if (value is not { } v || double.IsNaN(v))
                    continue;
                min = Math.Min(min, v);
                max = Math.Max(max, v);
            }
        }

        if (double.IsInfinity(min) || double.IsInfinity(max))
            return (0, 1);
        return (min, max);
    }

    // ---- Trendline overlay ----------------------------------------------------------------

    // Computes the trendline overlay for the first plotted series (matching the source renderer,
    // which fits the trendline to the first series' points) and attaches it to that series' layout.
    private static void AttachTrendline(
        ChartLayoutRequest request,
        List<SeriesLayout> seriesLayouts,
        Func<double, double> xToPixel,
        AxisScale primaryScale,
        AxisScale? secondaryScale,
        bool useSecondary)
    {
        var chart = request.Chart;
        if (!chart.ShowLinearTrendline || !ChartTypeSupport.SupportsTrendlines(chart.Type))
            return;
        if (request.Series.Count == 0 || seriesLayouts.Count == 0)
            return;

        var first = request.Series[0];
        var sourcePoints = new List<TrendPoint>(first.Values.Count);
        for (var i = 0; i < first.Values.Count; i++)
        {
            if (first.Values[i] is { } v && !double.IsNaN(v))
                sourcePoints.Add(new TrendPoint(i, v));
        }

        if (sourcePoints.Count < 2)
            return;

        var trend = TrendlineCalculator.Calculate(chart.TrendlineType, sourcePoints, chart.TrendlinePeriod, chart.TrendlineOrder);
        if (trend.Count < 2)
            return;

        var onSecondary = useSecondary && UsesSecondaryAxis(chart, first.SeriesIndex) && secondaryScale is not null;
        var yScale = onSecondary ? secondaryScale! : primaryScale;
        var pixelPoints = new List<LayoutPoint>(trend.Count);
        foreach (var point in trend)
            pixelPoints.Add(new LayoutPoint(xToPixel(point.X), yScale.Transform(point.Y)));

        seriesLayouts[0] = seriesLayouts[0] with
        {
            Trendline = new TrendlineLayout { Fit = ToFitKind(chart.TrendlineType), Points = pixelPoints },
        };
    }

    // Trendline overlay for scatter/bubble: x comes from the explicit x value, mapped through the
    // value-axis x scale rather than the category-index scale.
    private static void AttachScatterTrendline(
        ChartLayoutRequest request,
        List<SeriesLayout> seriesLayouts,
        AxisScale xScale,
        AxisScale yScale)
    {
        var chart = request.Chart;
        if (!chart.ShowLinearTrendline || !ChartTypeSupport.SupportsTrendlines(chart.Type))
            return;
        if (request.Series.Count == 0 || seriesLayouts.Count == 0)
            return;

        var first = request.Series[0];
        var sourcePoints = new List<TrendPoint>(first.Values.Count);
        for (var i = 0; i < first.Values.Count; i++)
        {
            if (first.Values[i] is not { } y || double.IsNaN(y))
                continue;
            var x = first.XValues is { } xs && i < xs.Count ? xs[i] : i;
            sourcePoints.Add(new TrendPoint(x, y));
        }

        if (sourcePoints.Count < 2)
            return;

        var trend = TrendlineCalculator.Calculate(chart.TrendlineType, sourcePoints, chart.TrendlinePeriod, chart.TrendlineOrder);
        if (trend.Count < 2)
            return;

        var pixelPoints = new List<LayoutPoint>(trend.Count);
        foreach (var point in trend)
            pixelPoints.Add(new LayoutPoint(xScale.Transform(point.X), yScale.Transform(point.Y)));

        seriesLayouts[0] = seriesLayouts[0] with
        {
            Trendline = new TrendlineLayout { Fit = ToFitKind(chart.TrendlineType), Points = pixelPoints },
        };
    }

    private static TrendlineFitKind ToFitKind(ChartTrendlineType type) =>
        type switch
        {
            ChartTrendlineType.Exponential => TrendlineFitKind.Exponential,
            ChartTrendlineType.Logarithmic => TrendlineFitKind.Logarithmic,
            ChartTrendlineType.Power => TrendlineFitKind.Power,
            ChartTrendlineType.MovingAverage => TrendlineFitKind.MovingAverage,
            ChartTrendlineType.Polynomial => TrendlineFitKind.Polynomial,
            _ => TrendlineFitKind.Linear,
        };

    // ---- Data-label helpers ---------------------------------------------------------------

    private static void AddCartesianDataLabel(
        ChartLayoutRequest request,
        List<DataLabelBox> dataLabels,
        ChartSeriesData series,
        int pointIndex,
        double value,
        LayoutPoint anchor)
    {
        var chart = request.Chart;
        if (!chart.ShowDataLabels)
            return;

        var category = pointIndex < request.Categories.Count ? request.Categories[pointIndex] : "";
        var text = BuildCartesianLabel(chart, series.Name ?? "", category, value);
        if (string.IsNullOrEmpty(text))
            return;

        var size = request.TextMeasurer.Measure(text, null, chart.DataLabelFontSize, false, false);
        dataLabels.Add(new DataLabelBox(series.SeriesIndex, pointIndex, text, anchor, CenteredRect(anchor, size)));
    }

    private static string BuildCartesianLabel(ChartModel chart, string seriesName, string categoryName, double value)
    {
        var hasSeries = chart.ShowDataLabelSeriesName && !string.IsNullOrWhiteSpace(seriesName);
        var hasCategory = chart.ShowDataLabelCategoryName && !string.IsNullOrWhiteSpace(categoryName);
        var hasValue = chart.ShowDataLabelValue || (!hasSeries && !hasCategory);
        var valueText = hasValue ? FormatLabelValue(chart, value) : "";
        var sep = SeparatorText(chart.DataLabelSeparator);

        return (hasSeries, hasCategory, hasValue) switch
        {
            (true, true, true) => $"{seriesName}{sep}{categoryName}{sep}{valueText}",
            (true, true, false) => $"{seriesName}{sep}{categoryName}",
            (true, false, true) => $"{seriesName}{sep}{valueText}",
            (true, false, false) => seriesName,
            (false, true, true) => $"{categoryName}{sep}{valueText}",
            (false, true, false) => categoryName,
            _ => valueText,
        };
    }

    private static string BuildPieLabel(ChartModel chart, string seriesName, string categoryName, double value, double fraction)
    {
        var sep = SeparatorText(chart.DataLabelSeparator);
        var hasSeries = chart.ShowDataLabelSeriesName && !string.IsNullOrWhiteSpace(seriesName);
        var hasCategory = chart.ShowDataLabelCategoryName && !string.IsNullOrWhiteSpace(categoryName);
        var parts = new List<string>(3);
        if (hasSeries) parts.Add(seriesName);
        if (hasCategory) parts.Add(categoryName);
        if (chart.ShowDataLabelPercentage)
            parts.Add(fraction.ToString("0%", CultureInfo.InvariantCulture));
        else if (chart.ShowDataLabelValue || parts.Count == 0)
            parts.Add(FormatLabelValue(chart, value));

        return string.Join(sep, parts);
    }

    private static string FormatLabelValue(ChartModel chart, double value) =>
        chart.ShowDataLabelPercentage && IsPercentageCapable(chart.Type)
            ? value.ToString("0%", CultureInfo.InvariantCulture)
            : chart.DataLabelNumberFormat switch
            {
                ChartDataLabelNumberFormat.Number => value.ToString("0.00", CultureInfo.InvariantCulture),
                ChartDataLabelNumberFormat.Currency => value.ToString("$#,##0.00", CultureInfo.InvariantCulture),
                ChartDataLabelNumberFormat.Percent => value.ToString("0%", CultureInfo.InvariantCulture),
                _ => value.ToString("0.###", CultureInfo.InvariantCulture),
            };

    private static bool IsPercentageCapable(ChartType type) =>
        type is ChartType.Pie or ChartType.ThreeDPie or ChartType.Doughnut
            or ChartType.PercentStackedColumn or ChartType.PercentStackedBar;

    internal static string FormatAxisValue(ChartDataLabelNumberFormat format, double value) =>
        format switch
        {
            ChartDataLabelNumberFormat.Number => value.ToString("0.00", CultureInfo.InvariantCulture),
            ChartDataLabelNumberFormat.Currency => value.ToString("$#,##0.00", CultureInfo.InvariantCulture),
            ChartDataLabelNumberFormat.Percent => value.ToString("0%", CultureInfo.InvariantCulture),
            _ => value.ToString("0.###", CultureInfo.InvariantCulture),
        };

    private static string SeparatorText(ChartDataLabelSeparator separator) =>
        separator switch
        {
            ChartDataLabelSeparator.Semicolon => "; ",
            ChartDataLabelSeparator.NewLine => "\n",
            ChartDataLabelSeparator.Space => " ",
            _ => ", ",
        };

    // ---- Shared geometry helpers ----------------------------------------------------------

    private static int ResolveCategoryCount(ChartLayoutRequest request)
    {
        if (request.Categories.Count > 0)
            return request.Categories.Count;
        var max = 0;
        foreach (var series in request.Series)
            max = Math.Max(max, series.Values.Count);
        return max;
    }

    private static double Clamp0(AxisScale scale) => Math.Clamp(0, scale.Minimum, scale.Maximum);

    /// <summary>Converts a polar angle (degrees, clockwise from 12 o'clock) + radius to a pixel point.</summary>
    internal static LayoutPoint PolarToPixel(LayoutPoint center, double angleDegrees, double radius)
    {
        // 0° points straight up; clockwise positive. Screen Y grows downward.
        var radians = (Math.PI / 180.0) * angleDegrees;
        var x = center.X + (radius * Math.Sin(radians));
        var y = center.Y - (radius * Math.Cos(radians));
        return new LayoutPoint(x, y);
    }

    private static LayoutRect CenteredRect(LayoutPoint center, TextSize size) =>
        new(center.X - (size.Width / 2), center.Y - (size.Height / 2), size.Width, size.Height);
}
