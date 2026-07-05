using FreeX.Core.Formula;
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
/// percent-stacked), line, area, scatter, pie, doughnut, bubble, radar, stock
/// (high-low-close / open-high-low-close), waterfall, histogram, pareto, box-and-whisker,
/// treemap, sunburst, funnel, and surface (rendered as a 2D heatmap grid by the shell renderers).
/// Column/line/area/scatter charts additionally support a trendline overlay and a secondary value
/// axis (combo charts).
/// </summary>
public static class ChartLayoutEngine
{
    // The source renderer centers both clustered and stacked columns/bars at index ± 0.35 by
    // default (WPF's ColumnBarHalfWidth is a single shared function used by both paths); both are
    // reproduced exactly here.
    private const double DefaultColumnHalfWidth = 0.35;
    private const double StackedColumnHalfWidth = 0.35;

    /// <summary>
    /// Returns the half-width of the full category slot for a clustered (non-stacked) column or bar
    /// chart, mirroring WPF ColumnBarHalfWidth. When <see cref="ChartModel.BarGapWidth"/> is set,
    /// the half-width is computed so that the gap between adjacent bars equals the requested
    /// percentage of the bar width (gapWidth=0 ⇒ no gap; gapWidth=150 ⇒ Excel default).
    /// </summary>
    private static double ClusteredBarHalfWidth(ChartModel chart) =>
        chart.BarGapWidth is int gapWidth
            ? Math.Clamp(0.5 * 100.0 / (100.0 + gapWidth), 0.05, 0.5)
            : DefaultColumnHalfWidth;

    /// <summary>
    /// Returns the half-width of a stacked column/bar segment, mirroring WPF's ColumnBarHalfWidth
    /// (the same shared default of 0.35 as the clustered path, applying the exact same
    /// <see cref="ChartModel.BarGapWidth"/> formula when the user sets a gap width). Stacked
    /// types are included in <see cref="ChartTypeSupport.SupportsBarGapWidth"/>, so a stacked chart's
    /// Gap Width setting must narrow/widen the stack just like a clustered chart's.
    /// </summary>
    private static double StackedBarHalfWidth(ChartModel chart) =>
        chart.BarGapWidth is int gapWidth
            ? Math.Clamp(0.5 * 100.0 / (100.0 + gapWidth), 0.05, 0.5)
            : StackedColumnHalfWidth;

    /// <summary>
    /// Returns the left/right offsets (relative to the category centre) for the bar of the
    /// <paramref name="clusterOrdinal"/>-th clustered series, given the full category half-width,
    /// the total clustered-series count, and Excel's Series Overlap percentage (-100..100, Format
    /// Data Series' "Overlap" slider; <see cref="ChartModel.BarOverlap"/>). Mirrors WPF
    /// ClusteredBarOffsets exactly: with one series the bar fills the whole slot regardless of
    /// overlap. With N series each bar has a fixed width <c>unitWidth</c> chosen so the whole
    /// cluster of N bars — spaced <c>unitWidth * (1 - overlap/100)</c> apart center-to-center —
    /// exactly fills <c>[-halfWidth, halfWidth]</c>: overlap=0 reproduces the previous disjoint
    /// side-by-side tiling, overlap=100 collapses every bar onto the same full-width position
    /// (Excel's fully-overlapping look), and overlap=-100 spreads the bars out with equal gaps.
    /// </summary>
    private static (double Left, double Right) ClusteredBarOffsets(
        double halfWidth,
        int clusterOrdinal,
        int clusterCount,
        int overlapPercent = 0)
    {
        if (clusterCount <= 1)
            return (-halfWidth, halfWidth);

        var overlap = Math.Clamp(overlapPercent, -100, 100) / 100.0;
        var denominator = clusterCount - (overlap * (clusterCount - 1));
        var unitWidth = Math.Abs(denominator) < 1e-9 ? 2.0 * halfWidth : 2.0 * halfWidth / denominator;
        var step = unitWidth * (1 - overlap);
        var left = -halfWidth + clusterOrdinal * step;
        return (left, left + unitWidth);
    }

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
            or ChartType.Doughnut
            or ChartType.Funnel
            or ChartType.Waterfall
            or ChartType.Histogram
            or ChartType.Pareto
            or ChartType.BoxAndWhisker
            or ChartType.Treemap
            or ChartType.Sunburst
            or ChartType.Surface
            or ChartType.ThreeDSurface;

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
            ChartType.Funnel => LayoutFunnel(request),
            ChartType.Waterfall => LayoutWaterfall(request),
            ChartType.Histogram => LayoutHistogram(request),
            ChartType.Pareto => LayoutPareto(request),
            ChartType.BoxAndWhisker => LayoutBoxAndWhisker(request),
            ChartType.Treemap => LayoutTreemap(request),
            ChartType.Sunburst => LayoutSunburst(request),
            ChartType.Surface or ChartType.ThreeDSurface => LayoutSurface(request),
            _ => LayoutColumnLineArea(request),
        };
    }

    /// <summary>
    /// Builds the value axis that runs along the bottom (X, for Bar/Scatter/Bubble) using a
    /// logarithmic scale when the chart requests it (<see cref="ChartModel.XAxisLogScale"/>) and the
    /// chart type supports a log X axis (<see cref="ChartTypeSupport.SupportsXAxisLogScale"/>);
    /// otherwise falls back to the normal linear axis.
    /// </summary>
    private static AxisScale CreateXValueAxis(ChartModel chart, double dataMin, double dataMax, PlotRect plot, AxisSide side)
    {
        if (chart.XAxisLogScale && ChartTypeSupport.SupportsXAxisLogScale(chart.Type))
        {
            return AxisScale.CreateLogValueAxis(dataMin, dataMax, plot, side,
                chart.XAxisMinimum, chart.XAxisMaximum, chart.XAxisLogBase);
        }

        return AxisScale.CreateValueAxis(dataMin, dataMax, plot, side,
            chart.XAxisMinimum, chart.XAxisMaximum, chart.XAxisMajorUnit);
    }

    /// <summary>
    /// Builds the value axis that runs along the left (Y, for Column/Line/Area/Scatter/Bubble) using
    /// a logarithmic scale when the chart requests it (<see cref="ChartModel.YAxisLogScale"/>) and the
    /// chart type supports a log Y axis (<see cref="ChartTypeSupport.SupportsYAxisLogScale"/>);
    /// otherwise falls back to the normal linear axis.
    /// </summary>
    private static AxisScale CreateYValueAxis(ChartModel chart, double dataMin, double dataMax, PlotRect plot, AxisSide side)
    {
        if (chart.YAxisLogScale && ChartTypeSupport.SupportsYAxisLogScale(chart.Type))
        {
            return AxisScale.CreateLogValueAxis(dataMin, dataMax, plot, side,
                chart.YAxisMinimum, chart.YAxisMaximum, chart.YAxisLogBase);
        }

        return AxisScale.CreateValueAxis(dataMin, dataMax, plot, side,
            chart.YAxisMinimum, chart.YAxisMaximum, chart.YAxisMajorUnit);
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
                var text = ChartDataLabelTextPlanner.FormatPieDataLabel(chart, series?.Name ?? "", label, value, fraction);
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

        var valueScale = CreateYValueAxis(chart, dataMin, dataMax, plot, AxisSide.Left);

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
            LayoutStackedColumns(request, categoryCount, isPercent, categoryScale, valueScale, seriesLayouts, dataLabels);
        }
        else
        {
            // For clustered (non-stacked) Column/ThreeDColumn charts each series gets a disjoint
            // sub-slot within the category so the bars sit side by side rather than overdrawing
            // each other (mirroring WPF ClusteredBarOffsets). The cluster count is the number of
            // series that will be laid out as columns; the ordinal increments for each such series.
            // Series promoted to a combo line/scatter overlay (ComboLineSeriesIndexes /
            // ComboScatterSeriesIndexes) are drawn as a line/scatter instead and excluded from both
            // the clustered slot count and the clustered ordinal, mirroring the source renderer's
            // CountClusteredBarSeries.
            var isClusteredColumn = chart.Type is ChartType.Column or ChartType.ThreeDColumn;
            var clusteredColumnCount = isClusteredColumn
                ? request.Series.Count(s => !IsComboLineSeries(chart, s.SeriesIndex) && !IsComboScatterSeries(chart, s.SeriesIndex))
                : 0;
            var clusteredColumnOrdinal = 0;

            foreach (var series in request.Series)
            {
                var onSecondary = useSecondary && UsesSecondaryAxis(chart, series.SeriesIndex);
                var yScale = onSecondary ? secondaryScale! : valueScale;
                var baseY = yScale.Transform(Clamp0(yScale));
                SeriesLayout laid;
                if (IsComboScatterSeries(chart, series.SeriesIndex))
                {
                    laid = LayoutComboScatterSeries(request, series, categoryScale, yScale, dataLabels);
                }
                else if (IsComboLineSeries(chart, series.SeriesIndex))
                {
                    laid = LayoutLineSeries(request, series, categoryScale, yScale, dataLabels);
                }
                else if (isClusteredColumn)
                {
                    laid = LayoutColumnSeries(request, series, categoryScale, yScale, baseY, dataLabels,
                        clusteredColumnOrdinal, clusteredColumnCount);
                    clusteredColumnOrdinal++;
                }
                else
                {
                    laid = chart.Type switch
                    {
                        ChartType.Area or ChartType.ThreeDArea =>
                            LayoutAreaSeries(request, series, categoryScale, yScale, baseY, dataLabels),
                        _ => LayoutLineSeries(request, series, categoryScale, yScale, dataLabels),
                    };
                }
                seriesLayouts.Add(laid with { UsesSecondaryAxis = onSecondary });
            }
        }

        AttachTrendline(request, seriesLayouts, x => categoryScale.Transform(x), valueScale, secondaryScale, useSecondary);

        return new ChartLayout
        {
            Type = chart.Type,
            PlotArea = plot.ToRect(),
            CategoryAxis = BuildCategoryAxisLayout(request, categoryScale, AxisSide.Bottom, valueScale.Transform(Clamp0(valueScale)), chart.XAxisLabelAngle),
            ValueAxis = BuildValueAxisLayout(chart, valueScale, AxisSide.Left, plot.Left, chart.YAxisNumberFormat, chart.YAxisNumberFormatCode, chart.YAxisLabelAngle),
            SecondaryValueAxis = secondaryScale is null
                ? null
                : BuildValueAxisLayout(chart, secondaryScale, AxisSide.Right, plot.Right, chart.YAxisNumberFormat, chart.YAxisNumberFormatCode, chart.YAxisLabelAngle),
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
        List<DataLabelBox> dataLabels,
        int clusterOrdinal = 0,
        int clusterCount = 1)
    {
        var chart = request.Chart;
        var bars = new List<SeriesBar>();
        // Compute the disjoint sub-slot for this series within the category slot.
        // With one series (clusterCount=1) the bar fills the full slot (no change).
        // With N series each occupies a 1/N sub-slot positioned at ordinal*subWidth,
        // mirroring WPF ClusteredBarOffsets so multi-series bars sit side by side.
        var halfWidth = ClusteredBarHalfWidth(chart);
        var (clusterLeft, clusterRight) = ClusteredBarOffsets(halfWidth, clusterOrdinal, clusterCount, chart.BarOverlap ?? 0);
        for (var i = 0; i < series.Values.Count; i++)
        {
            double v;
            if (series.Values[i] is { } actual)
                v = actual;
            else if (chart.BlankDisplayMode == ChartBlankDisplayMode.Zero)
                v = 0;
            else
                continue;

            // Mirrors WPF RectangleBarItem(i + clusterLeft, min(0,v), i + clusterRight, max(0,v)).
            var x0 = categoryScale.Transform(i + clusterLeft);
            var x1 = categoryScale.Transform(i + clusterRight);
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
        List<SeriesLayout> seriesLayouts,
        List<DataLabelBox> dataLabels)
    {
        var chart = request.Chart;
        var (posTotals, negTotals) = StackedTotals(request, categoryCount);
        var posBases = new double[categoryCount];
        var negBases = new double[categoryCount];
        var half = StackedBarHalfWidth(chart);

        foreach (var series in request.Series)
        {
            // A series promoted to a combo line overlay is drawn as a line over the stack instead
            // of a stacked segment, and does not participate in the running stack totals (mirrors
            // WPF BuildStackedColumnModel, which `continue`s before touching positiveBases/negativeBases).
            if (IsComboLineSeries(chart, series.SeriesIndex))
            {
                seriesLayouts.Add(LayoutLineSeries(request, series, categoryScale, valueScale, dataLabels));
                continue;
            }

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

    // Combo scatter overlay: same category-index x positions as the line overlay, but rendered as
    // unconnected markers (ScatterPoints), mirroring the source renderer's IsComboScatterSeries path
    // (a ScatterSeries with circle markers, no connecting line).
    private static SeriesLayout LayoutComboScatterSeries(
        ChartLayoutRequest request,
        ChartSeriesData series,
        AxisScale categoryScale,
        AxisScale valueScale,
        List<DataLabelBox> dataLabels)
    {
        var line = LayoutLineSeries(request, series, categoryScale, valueScale, dataLabels);
        return new SeriesLayout
        {
            SeriesIndex = series.SeriesIndex,
            Name = series.Name,
            Kind = SeriesGeometryKind.ScatterPoints,
            Points = line.Points,
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
        var valueScale = CreateXValueAxis(chart, dataMin, dataMax, plot, AxisSide.Bottom);

        var seriesLayouts = new List<SeriesLayout>(request.Series.Count);
        var dataLabels = new List<DataLabelBox>();
        var (posTotals, negTotals) = isStacked ? StackedTotals(request, categoryCount) : ([], []);
        var posBases = new double[categoryCount];
        var negBases = new double[categoryCount];
        var baselineX = valueScale.Transform(Clamp0(valueScale));

        // For clustered (non-stacked) Bar/ThreeDBar each series gets a disjoint sub-slot within
        // the category so the bars sit side by side rather than overdrawing each other, mirroring
        // WPF ClusteredBarOffsets. Stacked bars keep the full slot (half=StackedBarHalfWidth, which
        // also honors the chart's Gap Width setting like the clustered path does).
        var clusteredBarCount = isStacked ? 0 : request.Series.Count;
        var clusteredBarOrdinal = 0;
        var stackedHalfWidth = isStacked ? StackedBarHalfWidth(chart) : 0;

        foreach (var series in request.Series)
        {
            var bars = new List<SeriesBar>();

            // Determine the y-slot offsets (category axis) for this series.
            double ySlotLeft, ySlotRight;
            if (isStacked)
            {
                ySlotLeft  = -stackedHalfWidth;
                ySlotRight =  stackedHalfWidth;
            }
            else
            {
                var barHalfWidth = ClusteredBarHalfWidth(chart);
                (ySlotLeft, ySlotRight) = ClusteredBarOffsets(barHalfWidth, clusteredBarOrdinal, clusteredBarCount, chart.BarOverlap ?? 0);
                clusteredBarOrdinal++;
            }

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

                var y0 = categoryScale.Transform(i + ySlotLeft);
                var y1 = categoryScale.Transform(i + ySlotRight);
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

        // F7: the WPF renderer honors ShowLinearTrendline for horizontal Bar charts too
        // (swapTrendlineAxes: true); attach the trendline here so every renderer draws it for
        // Bar/StackedBar/PercentStackedBar/ThreeDBar.
        AttachBarTrendline(request, seriesLayouts, categoryScale, valueScale);

        return new ChartLayout
        {
            Type = chart.Type,
            PlotArea = plot.ToRect(),
            CategoryAxis = BuildCategoryAxisLayout(request, categoryScale, AxisSide.Left, baselineX, chart.YAxisLabelAngle),
            ValueAxis = BuildValueAxisLayout(chart, valueScale, AxisSide.Bottom, plot.Bottom, chart.XAxisNumberFormat, chart.XAxisNumberFormatCode, chart.XAxisLabelAngle),
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

        var xScale = CreateXValueAxis(chart, xMin, xMax, plot, AxisSide.Bottom);
        var yScale = CreateYValueAxis(chart, yMin, yMax, plot, AxisSide.Left);

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
            CategoryAxis = BuildValueAxisLayout(chart, xScale, AxisSide.Bottom, plot.Bottom, chart.XAxisNumberFormat, chart.XAxisNumberFormatCode, chart.XAxisLabelAngle),
            ValueAxis = BuildValueAxisLayout(chart, yScale, AxisSide.Left, plot.Left, chart.YAxisNumberFormat, chart.YAxisNumberFormatCode, chart.YAxisLabelAngle),
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

        var xScale = CreateXValueAxis(chart, xMin, xMax, plot, AxisSide.Bottom);
        var yScale = CreateYValueAxis(chart, yMin, yMax, plot, AxisSide.Left);

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
            CategoryAxis = BuildValueAxisLayout(chart, xScale, AxisSide.Bottom, plot.Bottom, chart.XAxisNumberFormat, chart.XAxisNumberFormatCode, chart.XAxisLabelAngle),
            ValueAxis = BuildValueAxisLayout(chart, yScale, AxisSide.Left, plot.Left, chart.YAxisNumberFormat, chart.YAxisNumberFormatCode, chart.YAxisLabelAngle),
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
            CategoryAxis = BuildCategoryAxisLayout(request, categoryScale, AxisSide.Bottom, plot.Bottom, chart.XAxisLabelAngle),
            ValueAxis = BuildValueAxisLayout(chart, valueScale, AxisSide.Left, plot.Left, chart.YAxisNumberFormat, chart.YAxisNumberFormatCode, chart.YAxisLabelAngle),
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
        ChartDataLabelNumberFormat numberFormat,
        string? numberFormatCode = null,
        double labelAngle = 0)
    {
        var ticks = new List<AxisTick>();
        foreach (var value in scale.GetMajorTickValues())
        {
            var label = !string.IsNullOrEmpty(numberFormatCode)
                ? NumberFormatter.Format(new NumberValue(value), numberFormatCode)
                : ChartDataLabelTextPlanner.FormatAxisValue(numberFormat, value);
            ticks.Add(new AxisTick(value, scale.Transform(value), label));
        }

        return new AxisLayout
        {
            Side = side,
            Title = side is AxisSide.Bottom or AxisSide.Top ? chart.XAxisTitle : chart.YAxisTitle,
            LinePosition = linePosition,
            Ticks = ticks,
            Scale = scale,
            LabelAngle = labelAngle,
        };
    }

    private static AxisLayout BuildCategoryAxisLayout(
        ChartLayoutRequest request,
        AxisScale scale,
        AxisSide side,
        double linePosition,
        double labelAngle = 0)
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
            LabelAngle = labelAngle,
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

    // Mirrors the source renderer's IsComboLineSeries: membership in ComboLineSeriesIndexes is
    // authoritative (populated from the chart XML's <c:lineChart> plot group), so a real Excel combo
    // chart (e.g. bar-plus-line) draws the designated series as a line overlay instead of a
    // column/area, even at series index 0 (Excel commonly draws the line series first over bar
    // helper series). An empty list means "no combo lines" so a plain chart is unaffected.
    private static bool IsComboLineSeries(ChartModel chart, int seriesIndex)
    {
        if (!ChartTypeSupport.SupportsComboLineOverlay(chart.Type) || !chart.UseComboLineForSecondarySeries || seriesIndex < 0)
            return false;

        return chart.ComboLineSeriesIndexes.Contains(seriesIndex);
    }

    // Mirrors the source renderer's IsComboScatterSeries: a series in ComboScatterSeriesIndexes is
    // drawn as a scatter overlay (markers only, no connecting line) instead of column/area.
    private static bool IsComboScatterSeries(ChartModel chart, int seriesIndex)
    {
        if (!ChartTypeSupport.SupportsComboLineOverlay(chart.Type) || seriesIndex < 0)
            return false;

        return chart.ComboScatterSeriesIndexes.Contains(seriesIndex);
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
            Trendline = BuildTrendlineLayout(chart, sourcePoints, trend, pixelPoints,
                point => new LayoutPoint(xToPixel(point.X), yScale.Transform(point.Y))),
        };
    }

    // Trendline overlay for horizontal Bar/StackedBar/PercentStackedBar/ThreeDBar charts: the category
    // axis is vertical (Y) and the value axis is horizontal (X) — the mirror image of the
    // column/line/area layout. Source points are (category index, value) exactly as in
    // <see cref="AttachTrendline"/>, but pixel mapping swaps which scale drives which screen axis
    // (mirroring the source renderer's swapTrendlineAxes: true for ChartType.Bar).
    private static void AttachBarTrendline(
        ChartLayoutRequest request,
        List<SeriesLayout> seriesLayouts,
        AxisScale categoryScale,
        AxisScale valueScale)
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

        // TrendPoint.X is the category index (→ categoryScale, vertical); TrendPoint.Y is the value
        // (→ valueScale, horizontal).
        var pixelPoints = new List<LayoutPoint>(trend.Count);
        foreach (var point in trend)
            pixelPoints.Add(new LayoutPoint(valueScale.Transform(point.Y), categoryScale.Transform(point.X)));

        seriesLayouts[0] = seriesLayouts[0] with
        {
            Trendline = BuildTrendlineLayout(chart, sourcePoints, trend, pixelPoints,
                point => new LayoutPoint(valueScale.Transform(point.Y), categoryScale.Transform(point.X)),
                swapAnnotationAxes: true),
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
            Trendline = BuildTrendlineLayout(chart, sourcePoints, trend, pixelPoints,
                point => new LayoutPoint(xScale.Transform(point.X), yScale.Transform(point.Y))),
        };
    }

    // Builds the TrendlineLayout including the optional equation/R-squared annotation (F18): the
    // annotation anchor mirrors the source renderer's TextAnnotation placement. The source renderer
    // (AddTrendlineIfRequested) swaps each source point to (Y, X) before taking (Min(X), Max(Y))
    // whenever swapTrendlineAxes is set (Bar charts); for the non-swapped families it takes
    // (Min(X), Max(Y)) of the source points directly. swapAnnotationAxes reproduces that exactly:
    // when true, the anchor is (min value, max index) — the swapped corner — instead of
    // (min index, max value), matching WPF's swapTrendlineAxes: true path for ChartType.Bar.
    private static TrendlineLayout BuildTrendlineLayout(
        ChartModel chart,
        IReadOnlyList<TrendPoint> sourcePoints,
        IReadOnlyList<TrendPoint> trend,
        IReadOnlyList<LayoutPoint> pixelPoints,
        Func<TrendPoint, LayoutPoint> toPixel,
        bool swapAnnotationAxes = false)
    {
        var annotationLines = TrendlineAnnotationFormatter.BuildAnnotationLines(chart, sourcePoints, trend);
        var anchor = default(LayoutPoint);
        if (annotationLines.Count > 0)
        {
            if (swapAnnotationAxes)
            {
                // Mirror WPF's displaySourcePoints = points.Select(p => (p.Y, p.X)) swap: anchor at
                // (min value, max index) rather than (min index, max value).
                var minValue = sourcePoints[0].Y;
                var maxIndex = sourcePoints[0].X;
                foreach (var point in sourcePoints)
                {
                    minValue = Math.Min(minValue, point.Y);
                    maxIndex = Math.Max(maxIndex, point.X);
                }

                anchor = toPixel(new TrendPoint(maxIndex, minValue));
            }
            else
            {
                var minX = sourcePoints[0].X;
                var maxY = sourcePoints[0].Y;
                foreach (var point in sourcePoints)
                {
                    minX = Math.Min(minX, point.X);
                    maxY = Math.Max(maxY, point.Y);
                }

                anchor = toPixel(new TrendPoint(minX, maxY));
            }
        }

        return new TrendlineLayout
        {
            Fit = ToFitKind(chart.TrendlineType),
            Points = pixelPoints,
            AnnotationLines = annotationLines,
            AnnotationAnchor = anchor,
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
        var text = ChartDataLabelTextPlanner.FormatDataLabel(chart, series.Name ?? "", category, value);
        if (string.IsNullOrEmpty(text))
            return;

        var size = request.TextMeasurer.Measure(text, null, chart.DataLabelFontSize, false, false);
        dataLabels.Add(new DataLabelBox(series.SeriesIndex, pointIndex, text, anchor, CenteredRect(anchor, size)));
    }

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

    // ---- Funnel ---------------------------------------------------------------------------

    // Funnel: horizontal bars centered on a vertical axis, widths proportional to value, stacked
    // top-to-bottom (first item widest, last narrowest). Produces Bars geometry (horizontal).
    // Math mirrors WPF BuildFunnelModel: halfWidth = value/maxVal * 0.45, placed at index i.
    // Each bar is colored from the palette (per-point, cycling).
    private static ChartLayout LayoutFunnel(ChartLayoutRequest request)
    {
        var chart = request.Chart;
        var legend = LegendLayoutBuilder.Build(request, out var plot);

        var series = request.Series.Count > 0 ? request.Series[0] : null;
        var allValues = new List<(int Index, double Value, string Label)>();
        var maxVal = 0.0;
        if (series is not null)
        {
            for (var i = 0; i < series.Values.Count; i++)
            {
                if (series.Values[i] is not { } v)
                    continue;
                var absVal = Math.Abs(v);
                var label = i < request.Categories.Count ? request.Categories[i] : $"Stage {i + 1}";
                allValues.Add((i, absVal, label));
                if (absVal > maxVal)
                    maxVal = absVal;
            }
        }

        if (maxVal <= 0)
            maxVal = 1;

        var n = allValues.Count;

        // Funnel axis: category-Y runs 0..n-1 (top-to-bottom), value-X is symmetric around 0.5
        // mirroring WPF's 0..1 X range. We map to pixel coords directly rather than going through
        // AxisScale because funnel axes are hidden — we just need the plot-rect extents.
        var plotW = plot.Width;
        var plotH = plot.Height;
        var barHeight = n > 0 ? plotH / n : plotH;
        var half = 0.45; // max half-width fraction of plot width

        var bars = new List<SeriesBar>(n);
        var palette = BuildFunnelPalette();
        for (var i = 0; i < n; i++)
        {
            var (index, value, _) = allValues[i];
            var barHalfW = value / maxVal * half * plotW;
            var cx = plot.Left + plotW / 2;
            var yTop = plot.Top + i * barHeight;
            var yBot = yTop + barHeight * 0.9;
            var rect = LayoutRect.FromCorners(cx - barHalfW, yTop, cx + barHalfW, yBot);
            var color = palette[i % palette.Length];
            bars.Add(new SeriesBar(index, value, rect, color));
        }

        var seriesLayout = new SeriesLayout
        {
            SeriesIndex = series?.SeriesIndex ?? 0,
            Name = series?.Name,
            Kind = SeriesGeometryKind.Bars,
            Bars = bars,
        };

        return new ChartLayout
        {
            Type = chart.Type,
            PlotArea = plot.ToRect(),
            Series = [seriesLayout],
            Legend = legend,
        };
    }

    // Funnel uses a fixed palette of 6 Office-style accent colors (no theme dependency in the engine
    // itself; the renderer overrides per-bar via FillColorOverride from the SeriesBar).
    private static readonly CellColor[] FunnelPaletteColors =
    [
        new CellColor(0x15, 0x60, 0x82), // Accent1-ish
        new CellColor(0xFF, 0x69, 0x1E), // Accent2-ish
        new CellColor(0xA9, 0xD1, 0x8E), // Accent3-ish
        new CellColor(0xFF, 0xC0, 0x00), // Accent4-ish
        new CellColor(0x5B, 0x9B, 0xD5), // Accent5-ish
        new CellColor(0x70, 0xAD, 0x47), // Accent6-ish
    ];

    private static CellColor[] BuildFunnelPalette() => FunnelPaletteColors;

    // ---- Waterfall ------------------------------------------------------------------------

    // Waterfall colors (match WPF: green for increase, red for decrease, blue for total).
    private static readonly CellColor WaterfallPositiveColor = new CellColor(0x54, 0x82, 0x35);
    private static readonly CellColor WaterfallNegativeColor = new CellColor(0xC0, 0x00, 0x00);
    private static readonly CellColor WaterfallTotalColor    = new CellColor(0x44, 0x72, 0xC4);

    // Waterfall: vertical floating bars — each bar starts at the running total of prior values.
    // Increase/decrease/total bars are colored differently. Connectors are emitted as WaterfallConnectors.
    // Math: uses the existing WaterfallBarPlanner.Compute() from Core.Model (same as WPF renderer).
    private static ChartLayout LayoutWaterfall(ChartLayoutRequest request)
    {
        var chart = request.Chart;
        var legend = LegendLayoutBuilder.Build(request, out var plot);
        var categoryCount = ResolveCategoryCount(request);

        // Collect values from the first series (waterfall is single-series).
        var series = request.Series.Count > 0 ? request.Series[0] : null;
        var rawValues = new List<double>(categoryCount);
        if (series is not null)
        {
            foreach (var v in series.Values)
                rawValues.Add(v ?? 0.0);
        }

        // Waterfall geometry is computed by the shared WaterfallBarPlanner (ported from WPF).
        var plan = WaterfallBarPlanner.Compute(rawValues, chart.WaterfallTotalPointIndices);

        // Compute the full value range so we can build a proper value axis.
        var yMin = double.PositiveInfinity;
        var yMax = double.NegativeInfinity;
        foreach (var bar in plan)
        {
            yMin = Math.Min(yMin, bar.Bottom);
            yMax = Math.Max(yMax, bar.Top);
        }
        if (double.IsInfinity(yMin) || double.IsInfinity(yMax))
        {
            yMin = 0;
            yMax = 1;
        }

        var categoryScale = AxisScale.CreateIndexAxis(-0.5, Math.Max(0.5, plan.Count - 0.5), plot, AxisSide.Bottom);
        var valueScale = AxisScale.CreateValueAxis(
            yMin, yMax, plot, AxisSide.Left,
            chart.YAxisMinimum, chart.YAxisMaximum, chart.YAxisMajorUnit);

        const double WaterfallHalfWidth = 0.35;
        var bars = new List<SeriesBar>(plan.Count);
        var connectors = new List<(LayoutPoint Left, LayoutPoint Right)>(plan.Count);

        for (var i = 0; i < plan.Count; i++)
        {
            var bar = plan[i];
            var color = bar.Kind switch
            {
                WaterfallBarKind.Total    => WaterfallTotalColor,
                WaterfallBarKind.Increase => WaterfallPositiveColor,
                _                         => WaterfallNegativeColor,
            };

            var x0 = categoryScale.Transform(i - WaterfallHalfWidth);
            var x1 = categoryScale.Transform(i + WaterfallHalfWidth);
            var yLow = valueScale.Transform(bar.Bottom);
            var yHigh = valueScale.Transform(bar.Top);
            var rect = LayoutRect.FromCorners(x0, yHigh, x1, yLow);
            bars.Add(new SeriesBar(i, rawValues.Count > i ? rawValues[i] : 0, rect, color));

            // Connector line: horizontal segment at the cumulative-after level, connecting right
            // edge of bar i to the left edge of bar i+1. Mirrors WPF AddWaterfallConnector.
            if (i < plan.Count - 1)
            {
                var cy = valueScale.Transform(bar.CumulativeAfter);
                var leftPt  = new LayoutPoint(x1, cy);
                var rightPt = new LayoutPoint(categoryScale.Transform(i + 1 - WaterfallHalfWidth), cy);
                connectors.Add((leftPt, rightPt));
            }
        }

        // Category labels
        var catLabels = new List<string>(plan.Count);
        for (var i = 0; i < plan.Count; i++)
            catLabels.Add(i < request.Categories.Count ? request.Categories[i] : $"Point {i + 1}");

        var catAxis = BuildWaterfallCategoryAxis(request, categoryScale, catLabels);
        var valAxis = BuildValueAxisLayout(chart, valueScale, AxisSide.Left, plot.Left, chart.YAxisNumberFormat, chart.YAxisNumberFormatCode, chart.YAxisLabelAngle);

        var seriesLayout = new SeriesLayout
        {
            SeriesIndex = series?.SeriesIndex ?? 0,
            Name = series?.Name,
            Kind = SeriesGeometryKind.Columns,
            Bars = bars,
            WaterfallConnectors = connectors,
        };

        return new ChartLayout
        {
            Type = chart.Type,
            PlotArea = plot.ToRect(),
            CategoryAxis = catAxis,
            ValueAxis = valAxis,
            Series = [seriesLayout],
            Legend = legend,
        };
    }

    private static AxisLayout BuildWaterfallCategoryAxis(
        ChartLayoutRequest request,
        AxisScale scale,
        List<string> labels)
    {
        var ticks = new List<AxisTick>(labels.Count);
        for (var i = 0; i < labels.Count; i++)
            ticks.Add(new AxisTick(i, scale.Transform(i), labels[i]));

        return new AxisLayout
        {
            Side = AxisSide.Bottom,
            Title = request.Chart.XAxisTitle,
            LinePosition = scale.Transform(0),
            Ticks = ticks,
            Scale = scale,
        };
    }

    // ---- Histogram ------------------------------------------------------------------------

    // Histogram: bin the single data series into equal-width buckets (using HistogramBinPlanner,
    // the same binning logic the WPF renderer uses), then emit one SeriesBar per bin. Category
    // labels are the bin-range strings produced by the planner. Math is fully ported from WPF.
    private static ChartLayout LayoutHistogram(ChartLayoutRequest request)
    {
        var chart = request.Chart;
        var legend = LegendLayoutBuilder.Build(request, out var plot);

        // Collect all numeric values from the first series.
        var series = request.Series.Count > 0 ? request.Series[0] : null;
        var rawValues = new List<double>();
        if (series is not null)
        {
            foreach (var v in series.Values)
                if (v is { } val)
                    rawValues.Add(val);
        }

        if (rawValues.Count == 0)
        {
            return new ChartLayout
            {
                Type = chart.Type,
                PlotArea = plot.ToRect(),
                Series = [],
                Legend = legend,
            };
        }

        // Binning: uses HistogramBinPlanner from Core.Model (same as WPF renderer).
        var bins = HistogramBinPlanner.Compute(rawValues, chart.HistogramBinning ?? new HistogramBinningModel());
        if (bins.Count == 0)
        {
            return new ChartLayout
            {
                Type = chart.Type,
                PlotArea = plot.ToRect(),
                Series = [],
                Legend = legend,
            };
        }

        var maxCount = bins.Max(b => b.Count);
        var categoryScale = AxisScale.CreateIndexAxis(-0.5, Math.Max(0.5, bins.Count - 0.5), plot, AxisSide.Bottom);
        var valueScale = AxisScale.CreateValueAxis(
            0, Math.Max(1, maxCount), plot, AxisSide.Left,
            chart.YAxisMinimum, chart.YAxisMaximum, chart.YAxisMajorUnit);

        const double HistogramHalfWidth = 0.45;
        var bars = new List<SeriesBar>(bins.Count);
        var baselineY = valueScale.Transform(0);
        for (var i = 0; i < bins.Count; i++)
        {
            var bin = bins[i];
            var x0 = categoryScale.Transform(i - HistogramHalfWidth);
            var x1 = categoryScale.Transform(i + HistogramHalfWidth);
            var yHigh = valueScale.Transform(bin.Count);
            var rect = LayoutRect.FromCorners(x0, yHigh, x1, baselineY);
            bars.Add(new SeriesBar(i, bin.Count, rect));
        }

        // Category axis ticks use bin labels (e.g. "0–10", "10–20").
        var catTicks = new List<AxisTick>(bins.Count);
        for (var i = 0; i < bins.Count; i++)
            catTicks.Add(new AxisTick(i, categoryScale.Transform(i), bins[i].Label));

        var catAxis = new AxisLayout
        {
            Side = AxisSide.Bottom,
            Title = chart.XAxisTitle,
            LinePosition = baselineY,
            Ticks = catTicks,
            Scale = categoryScale,
        };

        var freqTitle = !string.IsNullOrEmpty(chart.YAxisTitle) ? chart.YAxisTitle : "Frequency";
        var valAxis = new AxisLayout
        {
            Side = AxisSide.Left,
            Title = freqTitle,
            LinePosition = plot.Left,
            Ticks = BuildValueAxisLayout(chart, valueScale, AxisSide.Left, plot.Left, chart.YAxisNumberFormat, chart.YAxisNumberFormatCode, chart.YAxisLabelAngle).Ticks,
            Scale = valueScale,
        };

        var seriesLayout = new SeriesLayout
        {
            SeriesIndex = series?.SeriesIndex ?? 0,
            Name = series?.Name,
            Kind = SeriesGeometryKind.Columns,
            Bars = bars,
            AreaBaseline = baselineY,
        };

        return new ChartLayout
        {
            Type = chart.Type,
            PlotArea = plot.ToRect(),
            CategoryAxis = catAxis,
            ValueAxis = valAxis,
            Series = [seriesLayout],
            Legend = legend,
        };
    }

    // ---- Pareto ---------------------------------------------------------------------------

    // Pareto: histogram-style bars sorted descending by value + a cumulative-% line on a secondary
    // axis (0–100%). Bars are sorted by value (largest first), the cumulative line plots the
    // running percentage at each bar's right edge. Math matches WPF BuildParetoModel.
    private static ChartLayout LayoutPareto(ChartLayoutRequest request)
    {
        var chart = request.Chart;
        var legend = LegendLayoutBuilder.Build(request, out var plot);

        var series = request.Series.Count > 0 ? request.Series[0] : null;

        // Collect values + labels.
        var items = new List<(string Label, double Value)>();
        var total = 0.0;
        if (series is not null)
        {
            for (var i = 0; i < series.Values.Count; i++)
            {
                if (series.Values[i] is not { } v)
                    continue;
                var label = i < request.Categories.Count ? request.Categories[i] : $"Item {i + 1}";
                items.Add((label, v));
                total += v;
            }
        }

        // Sort descending by value (Pareto order).
        items.Sort((a, b) => b.Value.CompareTo(a.Value));
        var n = items.Count;

        if (n == 0)
        {
            return new ChartLayout
            {
                Type = chart.Type,
                PlotArea = plot.ToRect(),
                Series = [],
                Legend = legend,
            };
        }

        var maxValue = items.Count > 0 ? items[0].Value : 1;
        var categoryScale = AxisScale.CreateIndexAxis(-0.5, Math.Max(0.5, n - 0.5), plot, AxisSide.Bottom);
        var valueScale = AxisScale.CreateValueAxis(
            0, Math.Max(1, maxValue), plot, AxisSide.Left,
            chart.YAxisMinimum, chart.YAxisMaximum, chart.YAxisMajorUnit);

        // Secondary axis: 0–100% for the cumulative line.
        var pctScale = AxisScale.CreateValueAxis(0, 100, plot, AxisSide.Right);

        const double ParetoHalfWidth = 0.40;
        var bars = new List<SeriesBar>(n);
        var linePoints = new List<SeriesPoint>(n);
        var baselineY = valueScale.Transform(0);
        var runningSum = 0.0;

        for (var i = 0; i < n; i++)
        {
            var (_, value) = items[i];

            // Bar geometry.
            var x0 = categoryScale.Transform(i - ParetoHalfWidth);
            var x1 = categoryScale.Transform(i + ParetoHalfWidth);
            var yHigh = valueScale.Transform(value);
            var rect = LayoutRect.FromCorners(x0, yHigh, x1, baselineY);
            bars.Add(new SeriesBar(i, value, rect));

            // Cumulative % line point at the right edge of each bar (bar center x).
            runningSum += value;
            var pct = total > 0 ? 100.0 * runningSum / total : 0;
            var px = categoryScale.Transform(i);
            var py = pctScale.Transform(pct);
            linePoints.Add(new SeriesPoint(i, i, pct, new LayoutPoint(px, py)));
        }

        // Category axis ticks from the sorted labels.
        var catTicks = new List<AxisTick>(n);
        for (var i = 0; i < n; i++)
            catTicks.Add(new AxisTick(i, categoryScale.Transform(i), items[i].Label));

        var catAxis = new AxisLayout
        {
            Side = AxisSide.Bottom,
            Title = chart.XAxisTitle,
            LinePosition = baselineY,
            Ticks = catTicks,
            Scale = categoryScale,
        };

        var leftTitle = !string.IsNullOrEmpty(chart.YAxisTitle) ? chart.YAxisTitle : "Count";
        var valAxis = new AxisLayout
        {
            Side = AxisSide.Left,
            Title = leftTitle,
            LinePosition = plot.Left,
            Ticks = BuildValueAxisLayout(chart, valueScale, AxisSide.Left, plot.Left, chart.YAxisNumberFormat, chart.YAxisNumberFormatCode, chart.YAxisLabelAngle).Ticks,
            Scale = valueScale,
        };

        // Right axis: 0–100% with 20% major ticks.
        var pctTicks = new List<AxisTick>();
        for (var pct = 0; pct <= 100; pct += 20)
            pctTicks.Add(new AxisTick(pct, pctScale.Transform(pct), $"{pct}%"));

        var pctAxis = new AxisLayout
        {
            Side = AxisSide.Right,
            Title = "%",
            LinePosition = plot.Right,
            Ticks = pctTicks,
            Scale = pctScale,
        };

        // Bars series (index 0 of the layout) + cumulative-% line (index 1, uses secondary axis).
        var barSeries = new SeriesLayout
        {
            SeriesIndex = series?.SeriesIndex ?? 0,
            Name = series?.Name,
            Kind = SeriesGeometryKind.Columns,
            Bars = bars,
            AreaBaseline = baselineY,
        };

        // The line series gets SeriesIndex = -1 (a sentinel that does not appear in ChartModel.SeriesFormats),
        // flagged as secondary so the renderer knows it maps to the right axis.
        var lineSeries = new SeriesLayout
        {
            SeriesIndex = -1,
            Name = "%",
            Kind = SeriesGeometryKind.Line,
            Points = linePoints,
            UsesSecondaryAxis = true,
        };

        return new ChartLayout
        {
            Type = chart.Type,
            PlotArea = plot.ToRect(),
            CategoryAxis = catAxis,
            ValueAxis = valAxis,
            SecondaryValueAxis = pctAxis,
            Series = [barSeries, lineSeries],
            Legend = legend,
        };
    }

    // ---- Box-and-Whisker -----------------------------------------------------------------

    // Box-and-Whisker: one box per series column. Each column of values is sorted; Q1/median/Q3
    // are computed via linear interpolation (mirrors WPF BoxPercentile). IQR-based fences
    // (1.5×IQR) determine whisker extents. Box: Q1→Q3 rect. Median: horizontal line.
    // Whiskers: vertical lines from box edges to fence extremes. Outliers: small circles.
    // Uses Columns geometry kind (box rect as SeriesBar) + SeriesPoints for median/whiskers.
    private static ChartLayout LayoutBoxAndWhisker(ChartLayoutRequest request)
    {
        var chart = request.Chart;
        var legend = LegendLayoutBuilder.Build(request, out var plot);

        // Collect one set of values per series — each series is one box.
        var boxes = new List<BoxWhiskerStat>(request.Series.Count);
        for (var si = 0; si < request.Series.Count; si++)
        {
            var series = request.Series[si];
            var vals = new List<double>();
            foreach (var v in series.Values)
                if (v is { } val)
                    vals.Add(val);
            if (vals.Count == 0)
                continue;
            vals.Sort();
            var q1     = BoxPercentile(vals, 25);
            var median = BoxPercentile(vals, 50);
            var q3     = BoxPercentile(vals, 75);
            var iqr    = q3 - q1;
            var lowerFence = q1 - 1.5 * iqr;
            var upperFence = q3 + 1.5 * iqr;
            // Lower whisker: smallest value >= lowerFence.
            var lowerWhisker = vals[0];
            foreach (var val in vals)
            {
                if (val >= lowerFence) { lowerWhisker = val; break; }
            }
            // Upper whisker: largest value <= upperFence.
            var upperWhisker = vals[^1];
            for (var j = vals.Count - 1; j >= 0; j--)
            {
                if (vals[j] <= upperFence) { upperWhisker = vals[j]; break; }
            }
            // Outliers: values outside the fences.
            var outliers = new List<double>();
            foreach (var val in vals)
                if (val < lowerFence || val > upperFence)
                    outliers.Add(val);

            var label = si < request.Categories.Count ? request.Categories[si]
                      : (series.Name ?? $"S{si + 1}");
            boxes.Add(new BoxWhiskerStat(si, label, lowerWhisker, q1, median, q3, upperWhisker, outliers));
        }

        if (boxes.Count == 0)
        {
            return new ChartLayout
            {
                Type = chart.Type,
                PlotArea = plot.ToRect(),
                Series = [],
                Legend = legend,
            };
        }

        // Value range from all box extents.
        var yMin = double.PositiveInfinity;
        var yMax = double.NegativeInfinity;
        foreach (var b in boxes)
        {
            yMin = Math.Min(yMin, b.LowerWhisker);
            yMax = Math.Max(yMax, b.UpperWhisker);
            foreach (var o in b.Outliers) { yMin = Math.Min(yMin, o); yMax = Math.Max(yMax, o); }
        }
        if (double.IsInfinity(yMin) || double.IsInfinity(yMax)) { yMin = 0; yMax = 1; }

        var n = boxes.Count;
        var categoryScale = AxisScale.CreateIndexAxis(-0.5, Math.Max(0.5, n - 0.5), plot, AxisSide.Bottom);
        var valueScale    = AxisScale.CreateValueAxis(yMin, yMax, plot, AxisSide.Left,
            chart.YAxisMinimum, chart.YAxisMaximum, chart.YAxisMajorUnit);

        const double BoxHalfWidth = 0.30;
        // We emit multiple SeriesLayout: index 0 = boxes (Columns), index 1 = whisker/median lines (Line).
        var barsList   = new List<SeriesBar>();
        var linePoints = new List<SeriesPoint>(); // pairs: (lowerWhisker,top) then (UpperWhisker,bottom) then median
        var outlierPts = new List<SeriesPoint>();

        for (var i = 0; i < n; i++)
        {
            var b = boxes[i];
            // Box rect: Q1 → Q3.
            var cx   = categoryScale.Transform(i);
            var x0   = categoryScale.Transform(i - BoxHalfWidth);
            var x1   = categoryScale.Transform(i + BoxHalfWidth);
            var yQ1  = valueScale.Transform(b.Q1);
            var yQ3  = valueScale.Transform(b.Q3);
            barsList.Add(new SeriesBar(i, b.Q3 - b.Q1, LayoutRect.FromCorners(x0, yQ3, x1, yQ1)));

            // Median line: two sentinel points with same Y but different X used as a horizontal segment.
            var yMed = valueScale.Transform(b.Median);
            linePoints.Add(new SeriesPoint(i * 6 + 0, i - BoxHalfWidth, b.Median, new LayoutPoint(x0, yMed)));
            linePoints.Add(new SeriesPoint(i * 6 + 1, i + BoxHalfWidth, b.Median, new LayoutPoint(x1, yMed)));

            // Whisker lines: vertical center line from lowerWhisker to Q1, and Q3 to upperWhisker.
            // Encode as two-point line segments interleaved (renderer draws them as separate strokes).
            var yLow = valueScale.Transform(b.LowerWhisker);
            var yHigh = valueScale.Transform(b.UpperWhisker);
            // Lower whisker vertical: lowerWhisker → Q1
            linePoints.Add(new SeriesPoint(i * 6 + 2, i, b.LowerWhisker, new LayoutPoint(cx, yLow)));
            linePoints.Add(new SeriesPoint(i * 6 + 3, i, b.Q1,           new LayoutPoint(cx, yQ1)));
            // Upper whisker vertical: Q3 → upperWhisker
            linePoints.Add(new SeriesPoint(i * 6 + 4, i, b.Q3,           new LayoutPoint(cx, yQ3)));
            linePoints.Add(new SeriesPoint(i * 6 + 5, i, b.UpperWhisker, new LayoutPoint(cx, yHigh)));

            // Outlier points.
            foreach (var o in b.Outliers)
                outlierPts.Add(new SeriesPoint(i, i, o, new LayoutPoint(cx, valueScale.Transform(o))));
        }

        // Category ticks.
        var catTicks = new List<AxisTick>(n);
        for (var i = 0; i < n; i++)
            catTicks.Add(new AxisTick(i, categoryScale.Transform(i), boxes[i].Label));

        var catAxis = new AxisLayout
        {
            Side = AxisSide.Bottom,
            Title = chart.XAxisTitle,
            LinePosition = valueScale.Transform(Math.Max(0, yMin)),
            Ticks = catTicks,
            Scale = categoryScale,
        };
        var valAxis = BuildValueAxisLayout(chart, valueScale, AxisSide.Left, plot.Left, chart.YAxisNumberFormat, chart.YAxisNumberFormatCode, chart.YAxisLabelAngle);

        // Boxes as Columns series (filled Q1-Q3 rect), whiskers/median as Line, outliers as ScatterPoints.
        var boxSeries = new SeriesLayout
        {
            SeriesIndex = 0,
            Name = "Box",
            Kind = SeriesGeometryKind.Columns,
            Bars = barsList,
        };
        // Whisker/median lines stored as SeriesPoints — the renderer handles BoxAndWhisker specially.
        var whiskerSeries = new SeriesLayout
        {
            SeriesIndex = -2, // sentinel: box whisker overlay
            Name = "Whiskers",
            Kind = SeriesGeometryKind.BoxWhiskers,
            Points = linePoints,
        };
        var outlierSeries = new SeriesLayout
        {
            SeriesIndex = -3,
            Name = "Outliers",
            Kind = SeriesGeometryKind.ScatterPoints,
            Points = outlierPts,
        };

        return new ChartLayout
        {
            Type = chart.Type,
            PlotArea = plot.ToRect(),
            CategoryAxis = catAxis,
            ValueAxis = valAxis,
            Series = outlierPts.Count > 0
                ? [boxSeries, whiskerSeries, outlierSeries]
                : [boxSeries, whiskerSeries],
            Legend = legend,
        };
    }

    // Mirrors WPF BoxPercentile: linear interpolation between sorted values at position pct/100.
    private static double BoxPercentile(List<double> sorted, double pct)
    {
        if (sorted.Count == 1) return sorted[0];
        var pos = pct / 100.0 * (sorted.Count - 1);
        var lo = (int)pos;
        var hi = lo + 1;
        if (hi >= sorted.Count) return sorted[^1];
        return sorted[lo] + (pos - lo) * (sorted[hi] - sorted[lo]);
    }

    private readonly record struct BoxWhiskerStat(
        int SeriesIndex,
        string Label,
        double LowerWhisker,
        double Q1,
        double Median,
        double Q3,
        double UpperWhisker,
        List<double> Outliers);

    // ---- Treemap -------------------------------------------------------------------------

    // Treemap: tiles the plot rect into horizontal strips, each strip width proportional to its
    // value's share of the total. Single-row tiling (slice/dice) mirrors WPF BuildTreemapModel
    // which uses a single horizontal pass (width = value/total across the full plot height).
    // Each tile is a SeriesBar (rect) with a per-bar palette color.
    private static ChartLayout LayoutTreemap(ChartLayoutRequest request)
    {
        var chart = request.Chart;
        var legend = LegendLayoutBuilder.Build(request, out var plot);

        // Collect positive values from the first series (treemap is single-series, per-category tiles).
        var series = request.Series.Count > 0 ? request.Series[0] : null;
        var items = new List<(int Index, double Value, string Label)>();
        var total = 0.0;
        if (series is not null)
        {
            for (var i = 0; i < series.Values.Count; i++)
            {
                if (series.Values[i] is not { } v || v <= 0)
                    continue;
                var label = i < request.Categories.Count ? request.Categories[i] : $"Item {i + 1}";
                items.Add((i, v, label));
                total += v;
            }
        }

        if (items.Count == 0 || total <= 0)
        {
            return new ChartLayout
            {
                Type = chart.Type,
                PlotArea = plot.ToRect(),
                Series = [],
                Legend = legend,
            };
        }

        var palette = BuildTreemapPalette();
        var bars = new List<SeriesBar>(items.Count);
        var plotLeft = plot.Left;
        var plotTop  = plot.Top;
        var plotW    = plot.Width;
        var plotH    = plot.Height;
        var curX     = plotLeft;

        for (var i = 0; i < items.Count; i++)
        {
            var (index, value, _) = items[i];
            // Last tile gets remainder to avoid floating-point gap.
            double tileW;
            if (i == items.Count - 1)
                tileW = plotLeft + plotW - curX;
            else
                tileW = value / total * plotW;

            var rect = new LayoutRect(curX, plotTop, Math.Max(1, tileW), plotH);
            var color = palette[i % palette.Length];
            bars.Add(new SeriesBar(index, value, rect, color));
            curX += tileW;
        }

        // Category labels become data labels centered in each tile.
        var dataLabels = new List<DataLabelBox>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            var (_, _, label) = items[i];
            var bar = bars[i];
            var anchor = bar.Rect.Center;
            var size = request.TextMeasurer.Measure(label, null, 10, false, false);
            dataLabels.Add(new DataLabelBox(0, bars[i].PointIndex, label, anchor, CenteredRect(anchor, size)));
        }

        var seriesLayout = new SeriesLayout
        {
            SeriesIndex = series?.SeriesIndex ?? 0,
            Name = series?.Name,
            Kind = SeriesGeometryKind.TreemapTiles,
            Bars = bars,
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

    private static readonly CellColor[] TreemapPaletteColors =
    [
        new CellColor(0x44, 0x72, 0xC4), // blue
        new CellColor(0xED, 0x7D, 0x31), // orange
        new CellColor(0xA9, 0xD1, 0x8E), // green
        new CellColor(0xFF, 0xC0, 0x00), // yellow
        new CellColor(0x5B, 0x9B, 0xD5), // light blue
        new CellColor(0x70, 0xAD, 0x47), // dark green
    ];

    private static CellColor[] BuildTreemapPalette() => TreemapPaletteColors;

    // ---- Surface / heatmap ---------------------------------------------------------------

    // Surface: 2D heatmap grid. Rows = series, columns = categories. Each cell is a rect
    // colored by its z-value mapped through a blue (min) → yellow (max) gradient, matching
    // the shell renderer GetSurfaceCellColor logic (ChartRenderer.Surface.cs R:68→255 G:114→192 B:196→0).
    private static ChartLayout LayoutSurface(ChartLayoutRequest request)
    {
        var chart = request.Chart;
        var legend = LegendLayoutBuilder.Build(request, out var plot);

        var seriesCount   = request.Series.Count;
        var categoryCount = request.Categories.Count;
        if (seriesCount == 0 || categoryCount == 0)
        {
            return new ChartLayout
            {
                Type = chart.Type,
                PlotArea = plot.ToRect(),
                Series = [],
                Legend = legend,
            };
        }

        // Collect all values to find min/max for the gradient.
        var rawCells = new List<(int SeriesIdx, int CatIdx, double Value)>(seriesCount * categoryCount);
        var minValue = 0.0;
        var maxValue = 0.0;

        for (var si = 0; si < seriesCount; si++)
        {
            var s = request.Series[si];
            for (var ci = 0; ci < s.Values.Count; ci++)
            {
                if (s.Values[ci] is not { } v)
                    continue;
                if (rawCells.Count == 0)
                {
                    minValue = v;
                    maxValue = v;
                }
                else
                {
                    if (v < minValue) minValue = v;
                    if (v > maxValue) maxValue = v;
                }
                rawCells.Add((si, ci, v));
            }
        }

        // Build pixel-space grid: columns = categories, rows = series.
        var plotRect = plot.ToRect();
        var cellW = plotRect.Width  / categoryCount;
        var cellH = plotRect.Height / seriesCount;

        var surfaceCells = new List<SurfaceCell>(rawCells.Count);
        foreach (var (si, ci, value) in rawCells)
        {
            var left = plotRect.Left + ci * cellW;
            var top  = plotRect.Top  + si * cellH;
            var rect = new LayoutRect(left, top, cellW, cellH);
            var fill = GetSurfaceCellColor(value, minValue, maxValue);
            surfaceCells.Add(new SurfaceCell(si, ci, value, rect, fill));
        }

        // Axis ticks: category axis (X) and series/y axis.
        var catTicks = new List<AxisTick>(categoryCount);
        for (var ci = 0; ci < categoryCount; ci++)
        {
            var label = ci < request.Categories.Count ? request.Categories[ci] : $"{ci + 1}";
            var x = plotRect.Left + (ci + 0.5) * cellW;
            catTicks.Add(new AxisTick(ci, x, label));
        }

        var serTicks = new List<AxisTick>(seriesCount);
        for (var si = 0; si < seriesCount; si++)
        {
            var label = request.Series[si].Name ?? $"S{si + 1}";
            var y = plotRect.Top + (si + 0.5) * cellH;
            serTicks.Add(new AxisTick(si, y, label));
        }

        var categoryAxis = new AxisLayout
        {
            Side = AxisSide.Bottom,
            LinePosition = plotRect.Bottom,
            Ticks = catTicks,
            Scale = AxisScale.CreateIndexAxis(0, categoryCount, plot, AxisSide.Bottom),
        };
        var seriesAxis = new AxisLayout
        {
            Side = AxisSide.Left,
            LinePosition = plotRect.Left,
            Ticks = serTicks,
            Scale = AxisScale.CreateIndexAxis(0, seriesCount, plot, AxisSide.Left),
        };

        var seriesLayout = new SeriesLayout
        {
            SeriesIndex = 0,
            Kind = SeriesGeometryKind.SurfaceCells,
            SurfaceCells = surfaceCells,
        };

        return new ChartLayout
        {
            Type = chart.Type,
            PlotArea = plotRect,
            CategoryAxis = categoryAxis,
            ValueAxis = seriesAxis,
            Series = [seriesLayout],
            Legend = legend,
        };
    }

    /// <summary>
    /// Maps a z-value to a cell fill color using a blue→yellow gradient that mirrors the desktop
    /// shell renderer's heatmap color scale (R: 68→255, G: 114→192, B: 196→0).
    /// </summary>
    public static CellColor GetSurfaceCellColor(double value, double minValue, double maxValue)
    {
        var t = maxValue <= minValue
            ? 0.5
            : Math.Clamp((value - minValue) / (maxValue - minValue), 0.0, 1.0);
        var r = (byte)Math.Round(68  + (255 - 68)  * t);
        var g = (byte)Math.Round(114 + (192 - 114) * t);
        var b = (byte)Math.Round(196 + (0   - 196) * t);
        return new CellColor(r, g, b);
    }

    // ---- Sunburst ------------------------------------------------------------------------

    // Sunburst: doughnut-ring approximation (matching WPF BuildSunburstModel: PieSeries with
    // InnerDiameter=0.35). Slices are proportional to value; each slice is a LayoutArc with
    // inner radius set to 35% of the outer radius. Uses PieSlices geometry kind.
    private static ChartLayout LayoutSunburst(ChartLayoutRequest request)
    {
        var chart = request.Chart;
        var legend = LegendLayoutBuilder.Build(request, out var plot);

        var series = request.Series.Count > 0 ? request.Series[0] : null;
        var values = new List<(int Index, double Value, string Label)>();
        var total  = 0.0;
        if (series is not null)
        {
            for (var i = 0; i < series.Values.Count; i++)
            {
                if (series.Values[i] is not { } v || v <= 0)
                    continue;
                var label = i < request.Categories.Count ? request.Categories[i] : $"Item {i + 1}";
                values.Add((i, v, label));
                total += v;
            }
        }

        var center      = plot.ToRect().Center;
        var outerRadius = Math.Max(0, Math.Min(plot.Width, plot.Height) / 2.0);
        // WPF uses InnerDiameter=0.35, so innerRadius = outerRadius * 0.35.
        var innerRadius = outerRadius * 0.35;

        var slices     = new List<SeriesSlice>(values.Count);
        var dataLabels = new List<DataLabelBox>();
        var angle      = 0.0; // start at 12 o'clock, clockwise

        for (var s = 0; s < values.Count; s++)
        {
            var (index, value, label) = values[s];
            var fraction = total > 0 ? value / total : 0;
            var sweep    = fraction * 360.0;
            var arc      = new LayoutArc(center, outerRadius, innerRadius, angle, sweep);
            slices.Add(new SeriesSlice(index, value, fraction, label, arc));

            if (chart.ShowDataLabels && !string.IsNullOrEmpty(label))
            {
                var labelRadius = (outerRadius + innerRadius) / 2.0;
                var anchor = PolarToPixel(center, angle + sweep / 2, labelRadius);
                var size   = request.TextMeasurer.Measure(label, null, chart.DataLabelFontSize, false, false);
                dataLabels.Add(new DataLabelBox(0, index, label, anchor, CenteredRect(anchor, size)));
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
}
