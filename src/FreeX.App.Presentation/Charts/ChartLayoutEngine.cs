using System.Globalization;
using System.Xml.Linq;

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
    /// Resolves the effective Series Overlap percentage for a clustered/stacked column or bar
    /// chart, mirroring Excel's own native default: when the chart XML has no explicit
    /// <c>&lt;c:overlap&gt;</c> (<see cref="ChartModel.BarOverlap"/> is null), real Excel still
    /// draws clustered AND stacked/100%-stacked 2-D bar/column charts with overlap=-27 (a small gap
    /// between bars in the same cluster) — see <c>XlsxChartPartReader.Bar.cs</c>'s
    /// NormalizeExcelNativeDefaultBarOverlap, which maps a written -27 back to null on read so a
    /// default chart round-trips cleanly. Falling back to a literal 0 here (edge-to-edge bars)
    /// would silently diverge from Excel's rendering for the overwhelming majority of real-world
    /// clustered charts, so the null case must resolve to -27 for the same chart-type family the
    /// writer/reader normalize, and to 0 for 3-D bar/column (which Excel does not apply the -27
    /// default to).
    /// </summary>
    private static int EffectiveBarOverlap(ChartModel chart) =>
        chart.BarOverlap ?? (chart.Type is ChartType.Column
            or ChartType.Bar
            or ChartType.StackedColumn
            or ChartType.PercentStackedColumn
            or ChartType.StackedBar
            or ChartType.PercentStackedBar
                ? -27
                : 0);

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
            or ChartType.StackedArea
            or ChartType.PercentStackedArea
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
    private static AxisScale CreateXValueAxis(ChartModel chart, double dataMin, double dataMax, PlotRect plot, AxisSide side, bool includeZeroBaseline = true)
    {
        if (chart.XAxisLogScale && ChartTypeSupport.SupportsXAxisLogScale(chart.Type))
        {
            return AxisScale.CreateLogValueAxis(dataMin, dataMax, plot, side,
                chart.XAxisMinimum, chart.XAxisMaximum, chart.XAxisLogBase, chart.XAxisReverseOrder);
        }

        return AxisScale.CreateValueAxis(dataMin, dataMax, plot, side,
            chart.XAxisMinimum, chart.XAxisMaximum, chart.XAxisMajorUnit, reverseOrder: chart.XAxisReverseOrder,
            includeZeroBaseline: includeZeroBaseline);
    }

    /// <summary>
    /// Builds the value axis that runs along the left (Y, for Column/Line/Area/Scatter/Bubble) using
    /// a logarithmic scale when the chart requests it (<see cref="ChartModel.YAxisLogScale"/>) and the
    /// chart type supports a log Y axis (<see cref="ChartTypeSupport.SupportsYAxisLogScale"/>);
    /// otherwise falls back to the normal linear axis.
    /// </summary>
    private static AxisScale CreateYValueAxis(ChartModel chart, double dataMin, double dataMax, PlotRect plot, AxisSide side, bool includeZeroBaseline = true)
    {
        if (chart.YAxisLogScale && ChartTypeSupport.SupportsYAxisLogScale(chart.Type))
        {
            return AxisScale.CreateLogValueAxis(dataMin, dataMax, plot, side,
                chart.YAxisMinimum, chart.YAxisMaximum, chart.YAxisLogBase, chart.YAxisReverseOrder);
        }

        return AxisScale.CreateValueAxis(dataMin, dataMax, plot, side,
            chart.YAxisMinimum, chart.YAxisMaximum, chart.YAxisMajorUnit, reverseOrder: chart.YAxisReverseOrder,
            includeZeroBaseline: includeZeroBaseline);
    }

    /// <summary>
    /// R131-render-chart-date-category-axis: the portable (Avalonia in-app + PDF export) twin of the
    /// WPF renderer's <c>TryBuildDateCategoryAxis</c> (ChartRenderer.Axes.cs). When the category axis
    /// is marked as a date axis (<see cref="ChartModel.XAxisIsDateAxis"/>, OOXML's <c>&lt;c:dateAx&gt;</c>)
    /// and every category label parses as a date, returns one X position per category proportional to
    /// its actual date (day-granularity, via <see cref="DateTime.ToOADate"/> -- the same day-based
    /// scale OxyPlot's <c>DateTimeAxis.ToDouble</c> uses on the WPF side, so both shells plot at
    /// identical positions) instead of the plain 0,1,2… index every category axis used unconditionally
    /// before this fix. Returns false -- leaving every out parameter at its default -- whenever the
    /// chart isn't marked as a date axis, has no categories, or any single category fails to parse as a
    /// date, so callers fall back to exactly the previous evenly-spaced indexed category axis; this
    /// also means a plain (non-date) text category axis is completely unaffected by this method ever
    /// being called.
    /// </summary>
    private static bool TryBuildDateCategoryPositions(
        ChartModel chart,
        IReadOnlyList<string> categories,
        out double[] positions,
        out double minValue,
        out double maxValue)
    {
        positions = [];
        minValue = 0;
        maxValue = 0;
        if (!chart.XAxisIsDateAxis || categories.Count == 0)
            return false;

        var values = new double[categories.Count];
        var min = double.PositiveInfinity;
        var max = double.NegativeInfinity;
        for (var index = 0; index < categories.Count; index++)
        {
            if (!TryParseDateCategory(categories[index], out var parsed))
                return false;

            var value = parsed.Date.ToOADate();
            values[index] = value;
            if (value < min) min = value;
            if (value > max) max = value;
        }

        positions = values;
        minValue = min;
        maxValue = max;
        return true;
    }

    private static bool TryParseDateCategory(string category, out DateTime value) =>
        DateTime.TryParse(
            category,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out value) ||
        DateTime.TryParse(
            category,
            CultureInfo.CurrentCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out value);

    /// <summary>
    /// Resolves the plot-space X position for category index <paramref name="index"/>: the
    /// date-proportional position from <paramref name="positions"/> when non-null (a date category
    /// axis -- see <see cref="TryBuildDateCategoryPositions"/>), otherwise the plain category index
    /// unchanged.
    /// </summary>
    private static double CategoryX(double[]? positions, int index) =>
        positions is not null && index >= 0 && index < positions.Length ? positions[index] : index;

    /// <summary>
    /// R131-render-chart-axis-crosses: the portable (Avalonia in-app + PDF export) twin of the WPF
    /// renderer's <c>ApplyAxisCrossesPosition</c> (ChartRenderer.Axes.cs). Applies Excel's Format Axis
    /// &gt; Axis crosses &gt; Maximum category (<see cref="ChartAxisCrosses.Maximum"/>, OOXML's
    /// <c>&lt;c:crosses val="max"/&gt;</c>) to where the axis line and its labels are actually drawn --
    /// flipping the physical <paramref name="side"/> to the opposite edge of the plot rectangle
    /// (Bottom&lt;-&gt;Top, Left&lt;-&gt;Right) and recomputing <paramref name="linePosition"/> to that
    /// opposite edge's pixel coordinate. Every other <see cref="ChartAxisCrosses"/> value is a
    /// deliberate no-op -- <see cref="ChartAxisCrosses.AutoZero"/> (the <see cref="ChartModel"/>
    /// default for the overwhelming majority of charts that never touch this setting) and
    /// <see cref="ChartAxisCrosses.Minimum"/> both keep the axis at its original side, and
    /// <see cref="ChartAxisCrosses.Custom"/> (crosses at a specific authored value) has no single edge
    /// to flip to -- so this fix cannot regress any chart that didn't explicitly opt into "crosses at
    /// maximum".
    /// </summary>
    private static (AxisSide Side, double LinePosition) ApplyAxisCrosses(
        AxisSide side, double linePosition, ChartAxisCrosses crosses, PlotRect plot)
    {
        if (crosses != ChartAxisCrosses.Maximum)
            return (side, linePosition);

        return side switch
        {
            AxisSide.Bottom => (AxisSide.Top, plot.Top),
            AxisSide.Top => (AxisSide.Bottom, plot.Bottom),
            AxisSide.Left => (AxisSide.Right, plot.Right),
            AxisSide.Right => (AxisSide.Left, plot.Left),
            _ => (side, linePosition),
        };
    }

    // ---- Pie / Doughnut -------------------------------------------------------------------

    private static ChartLayout LayoutPie(ChartLayoutRequest request)
    {
        var chart = request.Chart;
        var legend = LegendLayoutBuilder.Build(request, out var plot);

        var center = plot.ToRect().Center;
        var outerRadius = Math.Max(0, Math.Min(plot.Width, plot.Height) / 2);

        // Excel's Doughnut chart draws every <c:ser> as its own concentric ring (series 0 innermost,
        // rising outward); a multi-series Pie/3D-Pie still shows only the first series as a single
        // ring (Excel silently ignores the rest). Both keep today's single-ring geometry unchanged
        // when there is exactly one series.
        var isDoughnut = chart.Type == ChartType.Doughnut;
        var ringSeries = isDoughnut ? request.Series : request.Series.Count > 0 ? [request.Series[0]] : [];

        var holeRadius = isDoughnut
            ? outerRadius * Math.Clamp(chart.DoughnutHoleSize, 0, 0.95)
            : 0;
        var ringCount = Math.Max(1, ringSeries.Count);
        var ringBandWidth = (outerRadius - holeRadius) / ringCount;

        var seriesLayouts = new List<SeriesLayout>(Math.Max(1, ringSeries.Count));
        var dataLabels = new List<DataLabelBox>();

        for (var ringIndex = 0; ringIndex < ringSeries.Count; ringIndex++)
        {
            var series = ringSeries[ringIndex];
            // Single-series case reproduces the original fixed outerRadius/innerRadius pair exactly
            // (ringIndex 0 of 1: inner = holeRadius, outer = outerRadius).
            var ringInnerRadius = holeRadius + (ringIndex * ringBandWidth);
            var ringOuterRadius = holeRadius + ((ringIndex + 1) * ringBandWidth);

            var values = new List<(int Index, double Value, string Label)>();
            var total = 0.0;
            for (var i = 0; i < series.Values.Count; i++)
            {
                if (series.Values[i] is not { } v || v <= 0)
                    continue;
                var label = i < request.Categories.Count ? request.Categories[i] : "";
                values.Add((i, v, label));
                total += v;
            }

            var slices = new List<SeriesSlice>(values.Count);
            // Angles measured clockwise from 12 o'clock, starting at the chart's first-slice angle.
            var angle = chart.FirstSliceAngle;
            for (var s = 0; s < values.Count; s++)
            {
                var (index, value, label) = values[s];
                var fraction = total > 0 ? value / total : 0;
                var sweep = fraction * 360.0;

                var sliceCenter = center;
                // Honor BOTH the legacy scalar ExplodedSliceIndex (single-slice explosion) AND every
                // entry in ExplodedSlices (per-point <c:dPt>/<c:explosion> overrides), so a chart
                // where several slices are individually exploded renders ALL of them exploded rather
                // than collapsing to just one -- mirrors the WPF renderer's IsPieSliceExploded.
                var isExploded = chart.ExplodedSliceIndex == index ||
                    chart.ExplodedSlices.Any(slice => slice.SeriesIndex == 0 && slice.PointIndex == index);
                if (isExploded && chart.ExplodedSliceDistance > 0)
                {
                    var mid = angle + (sweep / 2);
                    var offset = ringOuterRadius * chart.ExplodedSliceDistance;
                    sliceCenter = PolarToPixel(center, mid, offset);
                }

                var arc = new LayoutArc(sliceCenter, ringOuterRadius, ringInnerRadius, angle, sweep);
                slices.Add(new SeriesSlice(index, value, fraction, label, arc));

                if (chart.ShowDataLabels)
                {
                    var text = ChartDataLabelTextPlanner.FormatPieDataLabel(chart, series.Name ?? "", label, value, fraction);
                    if (!string.IsNullOrEmpty(text))
                        dataLabels.Add(BuildPieDataLabel(request, arc, index, text));
                }

                angle += sweep;
            }

            seriesLayouts.Add(new SeriesLayout
            {
                SeriesIndex = series.SeriesIndex,
                Name = series.Name,
                Kind = SeriesGeometryKind.PieSlices,
                Slices = slices,
            });
        }

        if (seriesLayouts.Count == 0)
        {
            // No series at all (empty chart): keep a single empty ring so consumers still see a
            // Series list shaped like every other pie/doughnut layout.
            seriesLayouts.Add(new SeriesLayout
            {
                SeriesIndex = 0,
                Kind = SeriesGeometryKind.PieSlices,
                Slices = [],
            });
        }

        return new ChartLayout
        {
            Type = chart.Type,
            PlotArea = plot.ToRect(),
            Series = seriesLayouts,
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

        // Stacked/100%-stacked column AND area both share the running-stack value range and category
        // axis; the two families differ only in the geometry builder chosen below (bars vs bands).
        var isStacked = chart.Type is ChartType.StackedColumn or ChartType.PercentStackedColumn
            or ChartType.StackedArea or ChartType.PercentStackedArea;
        var isStackedArea = chart.Type is ChartType.StackedArea or ChartType.PercentStackedArea;
        var isPercent = chart.Type is ChartType.PercentStackedColumn or ChartType.PercentStackedArea;

        // R131-render-chart-date-category-axis (Avalonia/PDF twin of the WPF fix in
        // ChartRenderer.cs/ChartRenderer.Stacked.cs): when the category axis is marked as a date axis
        // (XAxisIsDateAxis, OOXML's <c:dateAx>) and every category label parses as a date, plot each
        // category at its actual proportional date position instead of the plain 0,1,2… index --
        // covers stacked AND non-stacked Column/Area/Line alike since this portable engine (unlike the
        // WPF renderer's separate ChartRenderer.Stacked.cs) shares one category scale for both. When
        // this fails (not a date axis, no categories, or any category isn't parseable) categoryPositions
        // stays null and every call site below falls back to its original index-based behavior
        // unchanged -- including the catMin/catMax formula, so a plain (non-date) category axis is
        // completely unaffected by this ever being attempted.
        var hasDateCategoryAxis = TryBuildDateCategoryPositions(chart, request.Categories, out var categoryPositions, out var dateMin, out var dateMax);

        // Category axis: columns center categories over [-0.5, count-0.5]; line/area use [0, count-1].
        var isColumnFamily = chart.Type is ChartType.Column or ChartType.ThreeDColumn
            or ChartType.StackedColumn or ChartType.PercentStackedColumn;
        var (catMin, catMax) = hasDateCategoryAxis
            ? (dateMin - 0.5, dateMax + 0.5)
            : isColumnFamily
                ? (-0.5, Math.Max(0.5, categoryCount - 0.5))
                : (0.0, (double)Math.Max(1, categoryCount - 1));
        var categoryScale = AxisScale.CreateIndexAxis(catMin, catMax, plot, AxisSide.Bottom);

        var (dataMin, dataMax) = isStacked
            ? StackedValueRange(request, categoryCount, isPercent)
            : PlainValueRange(request);

        // Line/3-D Line have no zero-anchored geometry (unlike Column/Area, which draw bars/bands
        // down to a zero baseline), so their value axis should auto-fit tight to the actual data
        // extents instead of always widening out to include zero -- matching OxyPlot's own
        // LineSeries auto-range used by the WPF renderer (ChartRenderer.cs "else // Line / 3D Line").
        var isLineFamily = chart.Type is ChartType.Line or ChartType.ThreeDLine;
        var valueScale = CreateYValueAxis(chart, dataMin, dataMax, plot, AxisSide.Left, includeZeroBaseline: !isLineFamily);

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
            if (isStackedArea)
                LayoutStackedAreas(request, categoryCount, isPercent, categoryScale, valueScale, seriesLayouts, dataLabels, categoryPositions);
            else
                LayoutStackedColumns(request, categoryCount, isPercent, categoryScale, valueScale, seriesLayouts, dataLabels, categoryPositions);
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
                    laid = LayoutComboScatterSeries(request, series, categoryScale, yScale, dataLabels, categoryPositions);
                }
                else if (IsComboLineSeries(chart, series.SeriesIndex))
                {
                    laid = LayoutLineSeries(request, series, categoryScale, yScale, dataLabels, categoryPositions: categoryPositions);
                }
                else if (isClusteredColumn)
                {
                    laid = LayoutColumnSeries(request, series, categoryScale, yScale, baseY, dataLabels,
                        clusteredColumnOrdinal, clusteredColumnCount, categoryPositions);
                    clusteredColumnOrdinal++;
                }
                else
                {
                    laid = chart.Type switch
                    {
                        // Stacked/100%-stacked area are handled by LayoutStackedAreas above (true
                        // cumulative bands); only plain Area / 3-D Area reach this non-stacked path,
                        // which fills each band down to the flat zero baseline.
                        ChartType.Area or ChartType.ThreeDArea =>
                            LayoutAreaSeries(request, series, categoryScale, yScale, baseY, dataLabels, categoryPositions),
                        _ => LayoutLineSeries(request, series, categoryScale, yScale, dataLabels, categoryPositions: categoryPositions),
                    };
                }
                seriesLayouts.Add(laid with { UsesSecondaryAxis = onSecondary });
            }
        }

        AttachTrendline(request, seriesLayouts, x => categoryScale.Transform(x), valueScale, secondaryScale, useSecondary, categoryPositions);
        AttachErrorBars(request, seriesLayouts, x => categoryScale.Transform(x), valueScale, secondaryScale, useSecondary, categoryPositions);
        AddRangeDataLabels(request, dataLabels, categoryScale, valueScale, categoryPositions);

        // R131-render-chart-axis-crosses (Avalonia/PDF twin of the WPF fix in
        // ChartRenderer.Axes.cs ApplyAxisCrossesPosition): flip the physical side of whichever axis
        // sits Bottom/Left over to Top/Right when the chart explicitly requests "Axis crosses at
        // maximum" -- ChartAxisCrosses.AutoZero (the default for the overwhelming majority of charts)
        // and every other value are a deliberate no-op, so this cannot regress a chart that never
        // touched the setting.
        var (categorySide, categoryLine) = ApplyAxisCrosses(AxisSide.Bottom, valueScale.Transform(Clamp0(valueScale)), chart.XAxisCrosses, plot);
        var (valueSide, valueLine) = ApplyAxisCrosses(AxisSide.Left, plot.Left, chart.YAxisCrosses, plot);
        var (secondarySide, secondaryLine) = ApplyAxisCrosses(AxisSide.Right, plot.Right, chart.YAxisCrosses, plot);

        return new ChartLayout
        {
            Type = chart.Type,
            PlotArea = plot.ToRect(),
            CategoryAxis = BuildCategoryAxisLayout(request, categoryScale, categorySide, categoryLine, chart.XAxisLabelAngle, categoryPositions),
            ValueAxis = BuildValueAxisLayout(chart, valueScale, valueSide, valueLine, chart.YAxisNumberFormat, chart.YAxisNumberFormatCode, chart.YAxisLabelAngle),
            SecondaryValueAxis = secondaryScale is null
                ? null
                : BuildValueAxisLayout(chart, secondaryScale, secondarySide, secondaryLine, chart.YAxisNumberFormat, chart.YAxisNumberFormatCode, chart.YAxisLabelAngle),
            Series = seriesLayouts,
            Legend = legend,
            DataLabels = dataLabels,
        };
    }

    /// <summary>
    /// Draws Excel's "Value From Cells" data labels (<c>c15:datalabelsRange</c>,
    /// <see cref="ChartModel.RangeDataLabels"/>) as extra label boxes positioned above the tallest
    /// plotted value at each category (point) index, independent of <see cref="ChartModel.ShowDataLabels"/>
    /// — mirroring the WPF renderer's AddRangeDataLabelAnnotations (ChartRenderer.DeviationOverlay.cs),
    /// which floats the literal cached label text over the taller of the clustered column series for
    /// that category regardless of whether ordinary value/series/category data labels are on. When two
    /// series both label the same point, the first one wins (same precedence as the WPF path). No-op
    /// when the chart has no range data labels or no plotted values.
    /// </summary>
    private static void AddRangeDataLabels(
        ChartLayoutRequest request,
        List<DataLabelBox> dataLabels,
        AxisScale categoryScale,
        AxisScale valueScale,
        double[]? categoryPositions = null)
    {
        var chart = request.Chart;
        if (chart.RangeDataLabels.Count == 0 || request.Series.Count == 0)
            return;

        // Merge labels per category (point index): mirrors the WPF path's byPoint.TryAdd, which keeps
        // the first series' label when two series both label the same point.
        var byPoint = new Dictionary<int, string>();
        foreach (var label in chart.RangeDataLabels)
        {
            if (string.IsNullOrEmpty(label.Text))
                continue;
            byPoint.TryAdd(label.PointIndex, label.Text);
        }

        if (byPoint.Count == 0)
            return;

        foreach (var (pointIndex, text) in byPoint)
        {
            if (CategoryTopValue(request.Series, pointIndex) is not { } top)
                continue;

            var position = new LayoutPoint(categoryScale.Transform(CategoryX(categoryPositions, pointIndex)), valueScale.Transform(top));
            var size = request.TextMeasurer.Measure(text, null, chart.DataLabelFontSize, false, false);
            dataLabels.Add(new DataLabelBox(-1, pointIndex, text, position, CenteredRect(position, size)));
        }
    }

    /// <summary>Returns the tallest plotted value across all series at <paramref name="pointIndex"/>.</summary>
    private static double? CategoryTopValue(IReadOnlyList<ChartSeriesData> series, int pointIndex)
    {
        double? top = null;
        foreach (var s in series)
        {
            if (pointIndex < 0 || pointIndex >= s.Values.Count || s.Values[pointIndex] is not { } v)
                continue;
            top = top is { } existing ? Math.Max(existing, v) : v;
        }

        return top;
    }

    private static SeriesLayout LayoutColumnSeries(
        ChartLayoutRequest request,
        ChartSeriesData series,
        AxisScale categoryScale,
        AxisScale valueScale,
        double baselineY,
        List<DataLabelBox> dataLabels,
        int clusterOrdinal = 0,
        int clusterCount = 1,
        double[]? categoryPositions = null)
    {
        var chart = request.Chart;
        var bars = new List<SeriesBar>();
        // Compute the disjoint sub-slot for this series within the category slot.
        // With one series (clusterCount=1) the bar fills the full slot (no change).
        // With N series each occupies a 1/N sub-slot positioned at ordinal*subWidth,
        // mirroring WPF ClusteredBarOffsets so multi-series bars sit side by side.
        var halfWidth = ClusteredBarHalfWidth(chart);
        var (clusterLeft, clusterRight) = ClusteredBarOffsets(halfWidth, clusterOrdinal, clusterCount, EffectiveBarOverlap(chart));
        for (var i = 0; i < series.Values.Count; i++)
        {
            double v;
            if (series.Values[i] is { } actual)
                v = actual;
            else if (chart.BlankDisplayMode == ChartBlankDisplayMode.Zero)
                v = 0;
            else
                continue;

            // Mirrors WPF RectangleBarItem(x + clusterLeft, min(0,v), x + clusterRight, max(0,v))
            // where x is the date-proportional position (R131-render-chart-date-category-axis) when
            // this is a date category axis, otherwise the plain category index i unchanged.
            var x = CategoryX(categoryPositions, i);
            var x0 = categoryScale.Transform(x + clusterLeft);
            var x1 = categoryScale.Transform(x + clusterRight);
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
        List<DataLabelBox> dataLabels,
        double[]? categoryPositions = null)
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
                seriesLayouts.Add(LayoutLineSeries(request, series, categoryScale, valueScale, dataLabels, categoryPositions: categoryPositions));
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

                // R131-render-chart-date-category-axis (WPF-family gap): x is the date-proportional
                // position when this is a date category axis, otherwise the plain category index i
                // unchanged -- mirrors the WPF renderer's stacked-column date-axis fix in
                // ChartRenderer.Stacked.cs.
                var x = CategoryX(categoryPositions, i);
                var x0 = categoryScale.Transform(x - half);
                var x1 = categoryScale.Transform(x + half);
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

    /// <summary>
    /// Lays out Stacked Area / 100%-Stacked Area as one filled band per series, each riding on the
    /// cumulative baseline of the bands below it — the area-family analogue of
    /// <see cref="LayoutStackedColumns"/> and a mirror of the WPF renderer's <c>BuildStackedAreaModel</c>.
    /// Each band's top polyline is emitted as <see cref="SeriesLayout.Points"/> and its bottom (the
    /// running stack base) as <see cref="SeriesLayout.BaselinePoints"/>, so an area-fill consumer fills
    /// exactly the ribbon this series contributes (a true variable per-category baseline) instead of the
    /// old stopgap that dropped every band to the flat zero line. For <paramref name="isPercent"/> each
    /// category's stack is normalized to 100% via the same per-category totals
    /// (<see cref="StackedTotals"/>/<see cref="NormalizePercent"/>) the stacked column/bar path uses.
    /// A blank/non-numeric cell contributes 0 so the band stays continuous and the layers above keep a
    /// well-defined baseline (Excel stacks a blank area point as zero), matching WPF. A series promoted
    /// to a combo line overlay is drawn as a line over the stack and does not participate in the running
    /// totals (same as <see cref="LayoutStackedColumns"/>).
    /// </summary>
    private static void LayoutStackedAreas(
        ChartLayoutRequest request,
        int categoryCount,
        bool isPercent,
        AxisScale categoryScale,
        AxisScale valueScale,
        List<SeriesLayout> seriesLayouts,
        List<DataLabelBox> dataLabels,
        double[]? categoryPositions = null)
    {
        var chart = request.Chart;
        var (posTotals, negTotals) = StackedTotals(request, categoryCount);
        var posBases = new double[categoryCount];
        var negBases = new double[categoryCount];
        // Fallback zero-line baseline (only consulted if a consumer ignores BaselinePoints); the real
        // per-category bottom is carried in each band's BaselinePoints.
        var baselineY = valueScale.Transform(Clamp0(valueScale));

        foreach (var series in request.Series)
        {
            // A series promoted to a combo line overlay is drawn as a line over the stack instead of a
            // stacked band, and does not participate in the running stack totals (mirrors
            // LayoutStackedColumns / WPF BuildStackedAreaModel, which `continue` before touching bases).
            // Its own data labels still ride over the stack (WPF calls AddLineDataLabelAnnotations), so
            // the real dataLabels list is threaded through — unlike the stacked bands, which emit none.
            if (IsComboLineSeries(chart, series.SeriesIndex))
            {
                seriesLayouts.Add(LayoutLineSeries(request, series, categoryScale, valueScale, dataLabels, categoryPositions: categoryPositions));
                continue;
            }

            var topPoints = new List<SeriesPoint>(categoryCount);
            var bottomPoints = new List<SeriesPoint>(categoryCount);
            for (var i = 0; i < categoryCount; i++)
            {
                // Blank/non-numeric ⇒ 0 so the band is continuous and higher layers keep a defined base.
                var raw = i < series.Values.Count && series.Values[i] is { } v ? v : 0;
                var display = isPercent ? NormalizePercent(raw, i, posTotals, negTotals) : raw;
                var start = display >= 0 ? posBases[i] : negBases[i];
                var end = start + display;

                // R131-render-chart-date-category-axis (WPF-family gap): the band's top/bottom X
                // position uses the date-proportional position when this is a date category axis,
                // mirroring the WPF renderer's stacked-area date-axis fix in ChartRenderer.Stacked.cs.
                var catX = CategoryX(categoryPositions, i);
                var x = categoryScale.Transform(catX);
                topPoints.Add(new SeriesPoint(i, i, end, new LayoutPoint(x, valueScale.Transform(end))));
                bottomPoints.Add(new SeriesPoint(i, i, start, new LayoutPoint(x, valueScale.Transform(start))));

                if (display >= 0) posBases[i] = end; else negBases[i] = end;
            }

            seriesLayouts.Add(new SeriesLayout
            {
                SeriesIndex = series.SeriesIndex,
                Name = series.Name,
                Kind = SeriesGeometryKind.Area,
                Points = topPoints,
                BaselinePoints = bottomPoints,
                AreaBaseline = baselineY,
            });
        }
    }

    private static SeriesLayout LayoutLineSeries(
        ChartLayoutRequest request,
        ChartSeriesData series,
        AxisScale categoryScale,
        AxisScale valueScale,
        List<DataLabelBox> dataLabels,
        bool emitGapBreakPoint = true,
        double[]? categoryPositions = null)
    {
        var chart = request.Chart;
        var points = new List<SeriesPoint>();
        for (var i = 0; i < series.Values.Count; i++)
        {
            // R131-render-chart-date-category-axis: x is the date-proportional position when this is
            // a date category axis, otherwise the plain category index i unchanged.
            var x = CategoryX(categoryPositions, i);
            double v;
            if (series.Values[i] is { } actual)
            {
                v = actual;
            }
            else if (chart.BlankDisplayMode == ChartBlankDisplayMode.Zero)
            {
                v = 0;
            }
            else if (emitGapBreakPoint)
            {
                // Gap: emit an explicit NaN-valued point (mirrors the WPF renderer's
                // `DataPoint(i, double.NaN)` in ChartRenderer.SeriesFormatting.cs) so a NaN-aware
                // line/area renderer breaks the connecting geometry at this index, instead of
                // silently omitting the index — an omitted index lets the line/area jump straight
                // across the gap (round-29 finding R29-chart-render-pixel-deep-2). No data label
                // for a blank point.
                var gapPos = new LayoutPoint(categoryScale.Transform(x), valueScale.Transform(double.NaN));
                points.Add(new SeriesPoint(i, i, double.NaN, gapPos));
                continue;
            }
            else
            {
                // Gap, but this call is for a marker-only overlay (combo scatter): Excel never
                // draws a marker for a blank cell regardless of BlankDisplayMode, so the point is
                // omitted rather than given a NaN placeholder (see LayoutComboScatterSeries).
                continue;
            }

            var pos = new LayoutPoint(categoryScale.Transform(x), valueScale.Transform(v));
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
    // (a ScatterSeries with circle markers, no connecting line). Blanks are always omitted here
    // (emitGapBreakPoint: false) — unlike a connected line, a marker-only overlay has no line to
    // break, and the WPF reference renderer's combo-scatter path never plots a point for a blank
    // cell regardless of BlankDisplayMode (see ChartRenderer.cs's IsComboScatterSeries branch).
    private static SeriesLayout LayoutComboScatterSeries(
        ChartLayoutRequest request,
        ChartSeriesData series,
        AxisScale categoryScale,
        AxisScale valueScale,
        List<DataLabelBox> dataLabels,
        double[]? categoryPositions = null)
    {
        var line = LayoutLineSeries(request, series, categoryScale, valueScale, dataLabels, emitGapBreakPoint: false, categoryPositions: categoryPositions);
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
        List<DataLabelBox> dataLabels,
        double[]? categoryPositions = null)
    {
        var line = LayoutLineSeries(request, series, categoryScale, valueScale, dataLabels, categoryPositions: categoryPositions);
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
                (ySlotLeft, ySlotRight) = ClusteredBarOffsets(barHalfWidth, clusteredBarOrdinal, clusteredBarCount, EffectiveBarOverlap(chart));
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
        AttachBarErrorBars(request, seriesLayouts, categoryScale, valueScale);

        // R131-render-chart-axis-crosses (Avalonia/PDF twin of the WPF fix in
        // ChartRenderer.Axes.cs ApplyAxisCrossesPosition): the category axis physically sits Left ->
        // YAxisCrosses; the value axis physically sits Bottom -> XAxisCrosses. See the
        // ApplyAxisCrosses doc comment for why ChartAxisCrosses.AutoZero (the default) is a no-op.
        var (categorySide, categoryLine) = ApplyAxisCrosses(AxisSide.Left, baselineX, chart.YAxisCrosses, plot);
        var (valueSide, valueLine) = ApplyAxisCrosses(AxisSide.Bottom, plot.Bottom, chart.XAxisCrosses, plot);

        return new ChartLayout
        {
            Type = chart.Type,
            PlotArea = plot.ToRect(),
            CategoryAxis = BuildCategoryAxisLayout(request, categoryScale, categorySide, categoryLine, chart.YAxisLabelAngle),
            ValueAxis = BuildValueAxisLayout(chart, valueScale, valueSide, valueLine, chart.XAxisNumberFormat, chart.XAxisNumberFormatCode, chart.XAxisLabelAngle),
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

        // Scatter has no zero-anchored geometry (just plotted points), so both axes auto-fit tight
        // to the actual data extents rather than being forced to include zero -- matching the WPF
        // renderer's plain LinearAxis (no Minimum/Maximum override) for ChartType.Scatter.
        var xScale = CreateXValueAxis(chart, xMin, xMax, plot, AxisSide.Bottom, includeZeroBaseline: false);
        var yScale = CreateYValueAxis(chart, yMin, yMax, plot, AxisSide.Left, includeZeroBaseline: false);

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
        AttachScatterErrorBars(request, seriesLayouts, xScale, yScale);

        // R131-render-chart-axis-crosses (Avalonia/PDF twin of the WPF fix in ChartRenderer.Axes.cs
        // ApplyAxisCrossesPosition, reached for Scatter via the shared ApplyAxisBounds call at
        // ChartRenderer.cs:656): the X (value) axis physically sits Bottom -> XAxisCrosses; the Y
        // (value) axis physically sits Left -> YAxisCrosses. See the ApplyAxisCrosses doc comment for
        // why ChartAxisCrosses.AutoZero (the default) is a no-op.
        var (xSide, xLine) = ApplyAxisCrosses(AxisSide.Bottom, plot.Bottom, chart.XAxisCrosses, plot);
        var (ySide, yLine) = ApplyAxisCrosses(AxisSide.Left, plot.Left, chart.YAxisCrosses, plot);

        return new ChartLayout
        {
            Type = chart.Type,
            PlotArea = plot.ToRect(),
            CategoryAxis = BuildValueAxisLayout(chart, xScale, xSide, xLine, chart.XAxisNumberFormat, chart.XAxisNumberFormatCode, chart.XAxisLabelAngle),
            ValueAxis = BuildValueAxisLayout(chart, yScale, ySide, yLine, chart.YAxisNumberFormat, chart.YAxisNumberFormatCode, chart.YAxisLabelAngle),
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

        // Bubble has no zero-anchored geometry either (bubble radius encodes size, not a baseline
        // offset), so both axes auto-fit tight to the actual data extents, matching Scatter.
        var xScale = CreateXValueAxis(chart, xMin, xMax, plot, AxisSide.Bottom, includeZeroBaseline: false);
        var yScale = CreateYValueAxis(chart, yMin, yMax, plot, AxisSide.Left, includeZeroBaseline: false);

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
        AttachScatterErrorBars(request, seriesLayouts, xScale, yScale);

        // R131-render-chart-axis-crosses (Avalonia/PDF twin of the WPF fix in ChartRenderer.Axes.cs
        // ApplyAxisCrossesPosition, reached for Bubble via the explicit ApplyAxisBounds call at
        // ChartRenderer.cs:258): the X (value) axis physically sits Bottom -> XAxisCrosses; the Y
        // (value) axis physically sits Left -> YAxisCrosses. See the ApplyAxisCrosses doc comment for
        // why ChartAxisCrosses.AutoZero (the default) is a no-op.
        var (xSide, xLine) = ApplyAxisCrosses(AxisSide.Bottom, plot.Bottom, chart.XAxisCrosses, plot);
        var (ySide, yLine) = ApplyAxisCrosses(AxisSide.Left, plot.Left, chart.YAxisCrosses, plot);

        return new ChartLayout
        {
            Type = chart.Type,
            PlotArea = plot.ToRect(),
            CategoryAxis = BuildValueAxisLayout(chart, xScale, xSide, xLine, chart.XAxisNumberFormat, chart.XAxisNumberFormatCode, chart.XAxisLabelAngle),
            ValueAxis = BuildValueAxisLayout(chart, yScale, ySide, yLine, chart.YAxisNumberFormat, chart.YAxisNumberFormatCode, chart.YAxisLabelAngle),
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
        var isXAxis = side is AxisSide.Bottom or AxisSide.Top;
        var displayUnit = isXAxis ? chart.XAxisDisplayUnit : chart.YAxisDisplayUnit;
        var customDisplayUnit = isXAxis ? chart.XAxisCustomDisplayUnit : chart.YAxisCustomDisplayUnit;
        var divisor = GetAxisDisplayUnitDivisor(displayUnit, customDisplayUnit);

        var ticks = new List<AxisTick>();
        foreach (var value in scale.GetMajorTickValues())
        {
            var scaledValue = divisor is { } d && d > 0 && double.IsFinite(d) ? value / d : value;
            var label = !string.IsNullOrEmpty(numberFormatCode)
                ? NumberFormatter.Format(new NumberValue(scaledValue), numberFormatCode)
                : ChartDataLabelTextPlanner.FormatAxisValue(numberFormat, scaledValue);
            ticks.Add(new AxisTick(value, scale.Transform(value), label));
        }

        var title = isXAxis ? chart.XAxisTitle : chart.YAxisTitle;
        var unitLabel = GetAxisDisplayUnitLabel(displayUnit, customDisplayUnit);
        if (!string.IsNullOrEmpty(unitLabel))
            title = string.IsNullOrEmpty(title) ? unitLabel : $"{title} ({unitLabel})";

        // R87-render-chart-plot-5-4: the desktop renderer draws minor gridlines whenever the chart model
        // asks for them (ApplyGridlineStyle sets axis.MinorGridlineStyle = Dot); the portable layout
        // previously never computed minor-tick positions at all, so the portable-shell renderers
        // silently dropped them. Populate the same data here so the portable-shell renderers can draw them too.
        var showMinorGridlines = isXAxis ? chart.ShowXAxisMinorGridlines : chart.ShowYAxisMinorGridlines;
        var minorTickStyle = isXAxis ? chart.XAxisMinorTickStyle : chart.YAxisMinorTickStyle;
        var minorUnit = isXAxis ? chart.XAxisMinorUnit : chart.YAxisMinorUnit;
        var minorTicks = (showMinorGridlines || minorTickStyle != ChartAxisTickStyle.None) && !scale.IsLogarithmic
            ? BuildMinorTickValues(scale, minorUnit)
            : null;

        return new AxisLayout
        {
            Side = side,
            Title = title,
            LinePosition = linePosition,
            Ticks = ticks,
            MinorTicks = minorTicks,
            Scale = scale,
            LabelAngle = labelAngle,
        };
    }

    /// <summary>
    /// Enumerates minor-gridline tick positions between <paramref name="scale"/>'s bounds, stepping
    /// by <paramref name="minorUnit"/> when the chart specifies one, otherwise by a fifth of the
    /// major step (a conventional auto subdivision, matching OxyPlot's own auto minor-step behavior
    /// when no explicit minor interval is set).
    /// </summary>
    private static List<AxisTick> BuildMinorTickValues(AxisScale scale, double? minorUnit)
    {
        var ticks = new List<AxisTick>();
        var step = minorUnit is { } u && u > 0 ? u : scale.MajorStep / 5;
        if (step <= 0 || double.IsNaN(step) || double.IsInfinity(step))
            return ticks;

        var firstIndex = Math.Ceiling(scale.Minimum / step - 1e-9);
        var lastIndex = Math.Floor(scale.Maximum / step + 1e-9);
        for (var i = firstIndex; i <= lastIndex + 1e-9; i++)
        {
            var value = i * step;
            ticks.Add(new AxisTick(value, scale.Transform(value), ""));
        }

        return ticks;
    }

    /// <summary>
    /// Resolves Excel's Format Axis &gt; Display Units (<c>&lt;c:dispUnits&gt;</c>, round-tripped via
    /// <see cref="ChartModel.XAxisDisplayUnit"/>/<see cref="ChartModel.YAxisDisplayUnit"/> and their
    /// custom-unit overrides) to the numeric divisor Excel scales tick labels by. Mirrors
    /// ChartRenderer.Axes.cs GetAxisDisplayUnitDivisor (WPF) so both shells agree on tick text.
    /// </summary>
    private static double? GetAxisDisplayUnitDivisor(ChartAxisDisplayUnit? unit, double? customUnit)
    {
        if (customUnit is { } custom && double.IsFinite(custom) && custom > 0)
            return custom;

        return unit switch
        {
            ChartAxisDisplayUnit.Hundreds => 1e2,
            ChartAxisDisplayUnit.Thousands => 1e3,
            ChartAxisDisplayUnit.TenThousands => 1e4,
            ChartAxisDisplayUnit.HundredThousands => 1e5,
            ChartAxisDisplayUnit.Millions => 1e6,
            ChartAxisDisplayUnit.TenMillions => 1e7,
            ChartAxisDisplayUnit.HundredMillions => 1e8,
            ChartAxisDisplayUnit.Billions => 1e9,
            ChartAxisDisplayUnit.Trillions => 1e12,
            _ => null
        };
    }

    /// <summary>Mirrors ChartRenderer.Axes.cs GetAxisDisplayUnitLabel (WPF's axis-title suffix).</summary>
    private static string GetAxisDisplayUnitLabel(ChartAxisDisplayUnit? unit, double? customUnit)
    {
        if (customUnit is { } custom && double.IsFinite(custom) && custom > 0)
            return custom.ToString("0.###", CultureInfo.InvariantCulture);

        return unit switch
        {
            ChartAxisDisplayUnit.Hundreds => "Hundreds",
            ChartAxisDisplayUnit.Thousands => "Thousands",
            ChartAxisDisplayUnit.TenThousands => "Ten Thousands",
            ChartAxisDisplayUnit.HundredThousands => "Hundred Thousands",
            ChartAxisDisplayUnit.Millions => "Millions",
            ChartAxisDisplayUnit.TenMillions => "Ten Millions",
            ChartAxisDisplayUnit.HundredMillions => "Hundred Millions",
            ChartAxisDisplayUnit.Billions => "Billions",
            ChartAxisDisplayUnit.Trillions => "Trillions",
            _ => ""
        };
    }

    private static AxisLayout BuildCategoryAxisLayout(
        ChartLayoutRequest request,
        AxisScale scale,
        AxisSide side,
        double linePosition,
        double labelAngle = 0,
        double[]? categoryPositions = null)
    {
        // R90-render-chart-axis-titles-5-2: honor Excel's Format Axis > Labels "Interval between
        // labels" (<c:tickLblSkip>) and "Interval between tick marks" (<c:tickMarkSkip>), both read
        // into the model by XlsxChartAxisReader. A thinned label becomes an empty label (the tick
        // itself stays, so gridlines and the axis extent are untouched); a thinned tick mark clears
        // DrawTickMark. Both properties live on the X* model fields regardless of which side the
        // category axis sits on (the reader always writes XAxisLabelSkip/XAxisTickMarkSkip), so this
        // builder keys off them for its Bottom and Left callers alike.
        var labelInterval = ChartCategoryAxisSkip.ResolveInterval(request.Chart.XAxisLabelSkip);
        var tickInterval = ChartCategoryAxisSkip.ResolveInterval(request.Chart.XAxisTickMarkSkip);

        // R131-render-chart-date-category-axis: categoryPositions is non-null only for the
        // Column/Line/Area date-axis caller (LayoutColumnLineArea); every other caller passes null,
        // so its tick falls back to the plain category index i exactly as before.
        var ticks = new List<AxisTick>();
        for (var i = 0; i < request.Categories.Count; i++)
        {
            var label = ChartCategoryAxisSkip.IsShown(i, labelInterval) ? request.Categories[i] : "";
            var x = CategoryX(categoryPositions, i);
            ticks.Add(new AxisTick(i, scale.Transform(x), label, ChartCategoryAxisSkip.IsShown(i, tickInterval)));
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

        // At least one series must actually map to the secondary axis (any series, including the
        // first — see UsesSecondaryAxis).
        foreach (var series in request.Series)
        {
            if (UsesSecondaryAxis(chart, series.SeriesIndex))
                return true;
        }

        return false;
    }

    // Mirrors the source renderer (ChartRenderer.SeriesFormatting.UsesSecondaryAxis): a series uses
    // the secondary axis when the chart enables it and either an explicit assignment list contains
    // this series index — valid for ANY series, including the first (R25-chart-axis-series-deep-1,
    // Excel's Format Data Series > Secondary Axis works on series 0 too) — or no list is given, in
    // which case Excel's implicit default sends every series AFTER the first to the secondary axis.
    private static bool UsesSecondaryAxis(ChartModel chart, int seriesIndex)
    {
        if (!chart.ShowSecondaryAxis || seriesIndex < 0)
            return false;

        return chart.SecondaryAxisSeriesIndexes.Count == 0
            ? seriesIndex > 0
            : chart.SecondaryAxisSeriesIndexes.Contains(seriesIndex);
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

    // Mirrors the source renderer's AddTrendlineIfRequested: Excel only allows a fixed intercept on
    // the Linear trendline, so when ChartModel.TrendlineIntercept is set, the free-intercept fit
    // TrendlineCalculator.Calculate produced is discarded and refit with the intercept pinned
    // (least squares over the residual y - intercept), then the (possibly refit) trendline is
    // extended by the Forecast Forward/Backward periods. Without this step (the bug this method
    // fixes), both persisted, round-tripped chart options were silently dropped on this shell.
    private static IReadOnlyList<TrendPoint> ApplyTrendlineInterceptAndForecast(
        ChartModel chart,
        IReadOnlyList<TrendPoint> sourcePoints,
        IReadOnlyList<TrendPoint> trend)
    {
        if (chart.TrendlineType == ChartTrendlineType.Linear && chart.TrendlineIntercept is { } fixedIntercept)
            trend = CalculateLinearWithFixedIntercept(sourcePoints, fixedIntercept) ?? trend;

        return ApplyTrendlineForecast(chart, trend);
    }

    /// <summary>
    /// Refits a linear trendline with the intercept pinned to <paramref name="intercept"/> (Excel's
    /// "Set Intercept" option), returning the two fitted endpoints across the source X range. Uses
    /// ordinary least squares on the residual (y - intercept) so slope = Σx·(y-intercept) / Σx².
    /// Returns null when the fit is undefined (fewer than 2 points or a degenerate X range). Mirrors
    /// the source renderer's CalculateLinearWithFixedIntercept exactly.
    /// </summary>
    private static IReadOnlyList<TrendPoint>? CalculateLinearWithFixedIntercept(
        IReadOnlyList<TrendPoint> points,
        double intercept)
    {
        var sumXX = 0.0;
        var sumXResidual = 0.0;
        var minX = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var count = 0;
        for (var i = 0; i < points.Count; i++)
        {
            var point = points[i];
            sumXX += point.X * point.X;
            sumXResidual += point.X * (point.Y - intercept);
            minX = Math.Min(minX, point.X);
            maxX = Math.Max(maxX, point.X);
            count++;
        }

        if (count < 2 || Math.Abs(sumXX) < double.Epsilon)
            return null;

        var slope = sumXResidual / sumXX;
        return [new TrendPoint(minX, intercept + slope * minX), new TrendPoint(maxX, intercept + slope * maxX)];
    }

    /// <summary>
    /// Extends the fitted trendline by Excel's Forward/Backward forecast periods (measured in
    /// category-axis units, i.e. the same X units as the source points). Extrapolates using the
    /// trendline's own boundary segment (linear/exponential/logarithmic/power all sample a smooth
    /// curve whose two nearest boundary points define the local slope) so the extension continues the
    /// fitted shape rather than requiring a shared-file change to the trendline calculator. Moving
    /// Average has no Excel forecast option and is returned unchanged. Mirrors the source renderer's
    /// ApplyTrendlineForecast exactly.
    /// </summary>
    private static IReadOnlyList<TrendPoint> ApplyTrendlineForecast(
        ChartModel chart,
        IReadOnlyList<TrendPoint> trendPoints)
    {
        var forward = chart.TrendlineForward is { } f && f > 0 ? f : 0;
        var backward = chart.TrendlineBackward is { } b && b > 0 ? b : 0;
        if ((forward <= 0 && backward <= 0) || chart.TrendlineType == ChartTrendlineType.MovingAverage || trendPoints.Count < 2)
            return trendPoints;

        var result = new List<TrendPoint>(trendPoints.Count + 2);
        if (backward > 0)
        {
            var first = trendPoints[0];
            var second = trendPoints[1];
            var extendedX = first.X - backward;
            result.Add(new TrendPoint(extendedX, ExtrapolateY(chart.TrendlineType, first, second, extendedX)));
        }

        result.AddRange(trendPoints);

        if (forward > 0)
        {
            var last = trendPoints[^1];
            var secondToLast = trendPoints[^2];
            var extendedX = last.X + forward;
            result.Add(new TrendPoint(extendedX, ExtrapolateY(chart.TrendlineType, secondToLast, last, extendedX)));
        }

        return result;
    }

    /// <summary>
    /// Extrapolates a Y value at <paramref name="targetX"/> beyond the boundary segment
    /// (<paramref name="a"/>, <paramref name="b"/>) of a fitted trendline, using the closed-form shape
    /// appropriate to <paramref name="type"/> (log-linear for exponential/power in the relevant axis,
    /// straight-line extension otherwise). Falls back to a linear extension of the segment when the
    /// closed form is undefined for the given points (e.g. non-positive X/Y for log/power). Mirrors
    /// the source renderer's ExtrapolateY exactly.
    /// </summary>
    private static double ExtrapolateY(ChartTrendlineType type, TrendPoint a, TrendPoint b, double targetX)
    {
        var dx = b.X - a.X;
        if (Math.Abs(dx) < double.Epsilon)
            return b.Y;

        switch (type)
        {
            case ChartTrendlineType.Exponential when a.Y > 0 && b.Y > 0:
            {
                var slope = Math.Log(b.Y / a.Y) / dx;
                return a.Y * Math.Exp(slope * (targetX - a.X));
            }
            case ChartTrendlineType.Power when a.X > 0 && b.X > 0 && a.Y > 0 && b.Y > 0 && targetX > 0:
            {
                var dLogX = Math.Log(b.X) - Math.Log(a.X);
                if (Math.Abs(dLogX) < double.Epsilon)
                    break;
                var slope = Math.Log(b.Y / a.Y) / dLogX;
                return a.Y * Math.Pow(targetX / a.X, slope);
            }
            case ChartTrendlineType.Logarithmic when a.X > 0 && b.X > 0 && targetX > 0:
            {
                var dLogX = Math.Log(b.X) - Math.Log(a.X);
                if (Math.Abs(dLogX) < double.Epsilon)
                    break;
                var slope = (b.Y - a.Y) / dLogX;
                return b.Y + slope * (Math.Log(targetX) - Math.Log(b.X));
            }
        }

        // Linear (and any degenerate curve case above) extends the straight segment.
        var linearSlope = (b.Y - a.Y) / dx;
        return b.Y + linearSlope * (targetX - b.X);
    }

    // Computes the trendline overlay for the first plotted series (matching the source renderer,
    // which fits the trendline to the first series' points) and attaches it to that series' layout.
    private static void AttachTrendline(
        ChartLayoutRequest request,
        List<SeriesLayout> seriesLayouts,
        Func<double, double> xToPixel,
        AxisScale primaryScale,
        AxisScale? secondaryScale,
        bool useSecondary,
        double[]? categoryPositions = null)
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
            // R131-render-chart-date-category-axis: the trendline regression's X is the same
            // date-proportional position the plotted points use when this is a date category axis,
            // mirroring the WPF renderer's trendPoints (ChartRenderer.cs), so the fitted line stays
            // consistent with the actual (unevenly spaced) plotted geometry instead of the plain index.
            if (first.Values[i] is { } v && !double.IsNaN(v))
                sourcePoints.Add(new TrendPoint(CategoryX(categoryPositions, i), v));
        }

        if (sourcePoints.Count < 2)
            return;

        var trend = TrendlineCalculator.Calculate(chart.TrendlineType, sourcePoints, chart.TrendlinePeriod, chart.TrendlineOrder);
        if (trend.Count < 2)
            return;

        trend = ApplyTrendlineInterceptAndForecast(chart, sourcePoints, trend);

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

        trend = ApplyTrendlineInterceptAndForecast(chart, sourcePoints, trend);

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

        trend = ApplyTrendlineInterceptAndForecast(chart, sourcePoints, trend);

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

    // ---- Error bars (Std Error / Percentage / Fixed Value / Custom) -----------------------

    // Mirrors the source (WPF) renderer's AddErrorBarsIfRequested/AddWhisker/GetErrorBarAmount
    // (ChartRenderer.SeriesFormatting.cs) so every plotted series on every chart family that
    // supports error bars (column/bar/line/scatter/bubble/area — ChartTypeSupport.SupportsTrendlines)
    // draws identical whiskers on the portable rendering path. Unlike the trendline overlay (fitted
    // once for the first series only), error bars are attached per plotted series, matching Excel
    // (every series in the plot can carry its own error bars).

    /// <summary>
    /// Attaches the error-bar overlay to <paramref name="seriesLayouts"/> for the column/line/area
    /// family, where the category index maps to the horizontal pixel axis via
    /// <paramref name="xToPixel"/> and the plotted value maps to <paramref name="yScale"/> (or the
    /// secondary axis, when the series uses one).
    /// </summary>
    private static void AttachErrorBars(
        ChartLayoutRequest request,
        List<SeriesLayout> seriesLayouts,
        Func<double, double> xToPixel,
        AxisScale primaryScale,
        AxisScale? secondaryScale,
        bool useSecondary,
        double[]? categoryPositions = null)
    {
        var chart = request.Chart;
        if (!chart.ShowErrorBars || !ChartTypeSupport.SupportsTrendlines(chart.Type))
            return;

        for (var s = 0; s < seriesLayouts.Count && s < request.Series.Count; s++)
        {
            var series = request.Series[s];
            var onSecondary = useSecondary && UsesSecondaryAxis(chart, series.SeriesIndex) && secondaryScale is not null;
            var yScale = onSecondary ? secondaryScale! : primaryScale;

            // Mirrors the same blank-handling every column/line/area layout uses (LayoutColumnSeries/
            // LayoutLineSeries): a blank cell contributes a zero-valued anchor when BlankDisplayMode
            // is Zero, otherwise it is skipped entirely, so the error-bar overlay never draws a
            // whisker at a point the series geometry itself has no marker for.
            var anchors = new List<(int Index, double Value, LayoutPoint Pixel)>(series.Values.Count);
            for (var i = 0; i < series.Values.Count; i++)
            {
                double v;
                if (series.Values[i] is { } actual)
                    v = actual;
                else if (chart.BlankDisplayMode == ChartBlankDisplayMode.Zero)
                    v = 0;
                else
                    continue;

                // R131-render-chart-date-category-axis: the whisker's X anchor uses the same
                // date-proportional position the plotted points use when this is a date category axis.
                anchors.Add((i, v, new LayoutPoint(xToPixel(CategoryX(categoryPositions, i)), yScale.Transform(v))));
            }

            var whiskers = BuildErrorBarWhiskers(chart, anchors, isHorizontal: false, yScale);
            if (whiskers is not null)
                seriesLayouts[s] = seriesLayouts[s] with { ErrorBars = whiskers };
        }
    }

    /// <summary>
    /// Attaches the error-bar overlay for the horizontal Bar/StackedBar/PercentStackedBar/ThreeDBar
    /// family, where the category index maps to the vertical pixel axis via
    /// <paramref name="categoryScale"/> and the plotted value maps to the horizontal
    /// <paramref name="valueScale"/> — the mirror image of <see cref="AttachErrorBars"/> (matching the
    /// source renderer's isBarOrientedHorizontal path, which always whiskers along the value/X axis).
    /// </summary>
    private static void AttachBarErrorBars(
        ChartLayoutRequest request,
        List<SeriesLayout> seriesLayouts,
        AxisScale categoryScale,
        AxisScale valueScale)
    {
        var chart = request.Chart;
        if (!chart.ShowErrorBars || !ChartTypeSupport.SupportsTrendlines(chart.Type))
            return;

        for (var s = 0; s < seriesLayouts.Count && s < request.Series.Count; s++)
        {
            var series = request.Series[s];
            // Mirrors LayoutBar's own blank-handling (BlankDisplayMode.Zero ⇒ zero-valued anchor,
            // otherwise skipped) so the overlay never draws a whisker at a point with no bar.
            var anchors = new List<(int Index, double Value, LayoutPoint Pixel)>(series.Values.Count);
            for (var i = 0; i < series.Values.Count; i++)
            {
                double v;
                if (series.Values[i] is { } actual)
                    v = actual;
                else if (chart.BlankDisplayMode == ChartBlankDisplayMode.Zero)
                    v = 0;
                else
                    continue;

                anchors.Add((i, v, new LayoutPoint(valueScale.Transform(v), categoryScale.Transform(i))));
            }

            var whiskers = BuildErrorBarWhiskers(chart, anchors, isHorizontal: true, valueScale);
            if (whiskers is not null)
                seriesLayouts[s] = seriesLayouts[s] with { ErrorBars = whiskers };
        }
    }

    /// <summary>
    /// Attaches the error-bar overlay for scatter/bubble series, where each point's own X value (not
    /// the category index) maps through <paramref name="xScale"/>. Whiskers run along Y by default,
    /// matching every other family, unless the chart XML explicitly requests X-direction error bars
    /// (only meaningful — and only ever set by Excel — for Scatter/Bubble, whose X axis carries real
    /// values rather than a category index), mirroring the source renderer exactly.
    /// </summary>
    private static void AttachScatterErrorBars(
        ChartLayoutRequest request,
        List<SeriesLayout> seriesLayouts,
        AxisScale xScale,
        AxisScale yScale)
    {
        var chart = request.Chart;
        if (!chart.ShowErrorBars || !ChartTypeSupport.SupportsTrendlines(chart.Type))
            return;

        var isHorizontal = chart.ErrorBarAxisDirection == ChartErrorBarAxisDirection.X;
        for (var s = 0; s < seriesLayouts.Count && s < request.Series.Count; s++)
        {
            var series = request.Series[s];
            var anchors = new List<(int Index, double Value, LayoutPoint Pixel)>(series.Values.Count);
            for (var i = 0; i < series.Values.Count; i++)
            {
                if (series.Values[i] is not { } y || double.IsNaN(y))
                    continue;
                var x = series.XValues is { } xs && i < xs.Count ? xs[i] : i;
                var pixel = new LayoutPoint(xScale.Transform(x), yScale.Transform(y));
                // The amount kind (Standard Error/Percentage/Fixed) is always computed off the
                // plotted value the whisker direction runs along, mirroring GetErrorBarAnchorPoints'
                // `values[i] = isHorizontal ? points[i].X : points[i].Y`.
                anchors.Add((i, isHorizontal ? x : y, pixel));
            }

            var whiskers = BuildErrorBarWhiskers(chart, anchors, isHorizontal, isHorizontal ? xScale : yScale);
            if (whiskers is not null)
                seriesLayouts[s] = seriesLayouts[s] with { ErrorBars = whiskers };
        }
    }

    /// <summary>
    /// Builds the whisker overlay for one series from its plotted (index, value, pixel-anchor)
    /// triples, mirroring the source renderer's AddWhisker/GetErrorBarAmount: computes each point's
    /// plus/minus amount per <see cref="ChartModel.ErrorBarKind"/>, maps it through
    /// <paramref name="amountScale"/> (the axis the whisker direction runs along, so amounts are
    /// expressed and transformed in the same data units as the plotted value) to get the whisker's
    /// pixel half-length, and builds the perpendicular end-cap tick endpoints. Returns null when no
    /// point has a positive amount on either side (nothing to draw), matching the source renderer's
    /// `any` guard that skips adding an empty whiskers series.
    /// </summary>
    private static ErrorBarLayout? BuildErrorBarWhiskers(
        ChartModel chart,
        IReadOnlyList<(int Index, double Value, LayoutPoint Pixel)> anchors,
        bool isHorizontal,
        AxisScale amountScale)
    {
        if (anchors.Count == 0)
            return null;

        var values = new double[anchors.Count];
        for (var i = 0; i < anchors.Count; i++)
            values[i] = anchors[i].Value;

        var customPlus = ParseErrorBarRangeCache(chart.ErrorBarPlusRangeCacheXml);
        var customMinus = ParseErrorBarRangeCache(chart.ErrorBarMinusRangeCacheXml) ?? customPlus;

        // The end-cap tick runs perpendicular to the whisker (i.e. along the *other* screen axis than
        // the whisker itself). A fixed data-space half-width (matching the source renderer's 0.08
        // category-unit tick) has no meaning on the perpendicular axis, so the tick length is instead
        // derived as a small fraction of the whisker axis' own pixel extent — small, visible, and
        // resolution-independent regardless of which concrete axis backs the whisker.
        var capHalfLengthPixels = Math.Max(3.0, Math.Abs(amountScale.ScreenMax - amountScale.ScreenMin) * 0.015);

        var whiskers = new List<ErrorBarWhisker>(anchors.Count);
        var any = false;
        for (var i = 0; i < anchors.Count; i++)
        {
            var amount = GetErrorBarAmount(chart, values, i, customPlus, customMinus, out var plusAmount, out var minusAmount);
            var plus = chart.ErrorBarDirection == ChartErrorBarDirection.Minus ? 0 : (plusAmount > 0 ? plusAmount : amount);
            var minus = chart.ErrorBarDirection == ChartErrorBarDirection.Plus ? 0 : (minusAmount > 0 ? minusAmount : amount);
            if (plus <= 0 && minus <= 0)
                continue;

            any = true;
            var (index, value, pixel) = anchors[i];

            LayoutPoint PixelAt(double coordinate) =>
                isHorizontal ? new LayoutPoint(coordinate, pixel.Y) : new LayoutPoint(pixel.X, coordinate);

            var plusEnd = plus > 0 ? PixelAt(amountScale.Transform(value + plus)) : pixel;
            var minusEnd = minus > 0 ? PixelAt(amountScale.Transform(value - minus)) : pixel;

            LayoutPoint PerpendicularAt(LayoutPoint end, double offset) =>
                isHorizontal
                    ? new LayoutPoint(end.X, end.Y + offset)
                    : new LayoutPoint(end.X + offset, end.Y);

            whiskers.Add(new ErrorBarWhisker(
                index,
                pixel,
                plusEnd,
                minusEnd,
                HasPlus: plus > 0,
                HasMinus: minus > 0,
                PlusCapStart: plus > 0 ? PerpendicularAt(plusEnd, -capHalfLengthPixels) : plusEnd,
                PlusCapEnd: plus > 0 ? PerpendicularAt(plusEnd, capHalfLengthPixels) : plusEnd,
                MinusCapStart: minus > 0 ? PerpendicularAt(minusEnd, -capHalfLengthPixels) : minusEnd,
                MinusCapEnd: minus > 0 ? PerpendicularAt(minusEnd, capHalfLengthPixels) : minusEnd));
        }

        if (!any)
            return null;

        return new ErrorBarLayout { Whiskers = whiskers, EndCaps = chart.ErrorBarEndCaps };
    }

    /// <summary>
    /// Computes the whisker half-length for point <paramref name="index"/> of a series whose plotted
    /// values are <paramref name="values"/>, per Excel's error-bar amount kinds: Standard Error (the
    /// series' own sample standard error, same for every point), Percentage (a percentage of that
    /// point's value), Fixed Value (a constant amount), and Custom (explicit plus/minus amounts read
    /// from the cached range values, one entry per point). <paramref name="plusAmount"/>/
    /// <paramref name="minusAmount"/> carry the resolved Custom-kind asymmetric amounts (zero when not
    /// Custom or when no cached value exists for this point); the return value is the symmetric amount
    /// used for every other kind. Mirrors the source renderer's GetErrorBarAmount exactly.
    /// </summary>
    private static double GetErrorBarAmount(
        ChartModel chart,
        IReadOnlyList<double> values,
        int index,
        IReadOnlyList<double>? customPlus,
        IReadOnlyList<double>? customMinus,
        out double plusAmount,
        out double minusAmount)
    {
        plusAmount = 0;
        minusAmount = 0;
        switch (chart.ErrorBarKind)
        {
            case ChartErrorBarKind.Percentage:
                return Math.Abs(values[index]) * chart.ErrorBarValue / 100.0;
            case ChartErrorBarKind.FixedValue:
                return chart.ErrorBarValue;
            case ChartErrorBarKind.Custom:
                plusAmount = customPlus is not null && index < customPlus.Count ? Math.Abs(customPlus[index]) : 0;
                minusAmount = customMinus is not null && index < customMinus.Count ? Math.Abs(customMinus[index]) : 0;
                return 0;
            default:
                return CalculateStandardError(values);
        }
    }

    /// <summary>Sample standard error of the mean (sample stddev / sqrt(n)) — Excel's "Standard Error" amount.</summary>
    private static double CalculateStandardError(IReadOnlyList<double> values)
    {
        if (values.Count < 2)
            return 0;

        var mean = values.Average();
        var sumSquares = 0.0;
        for (var i = 0; i < values.Count; i++)
            sumSquares += (values[i] - mean) * (values[i] - mean);

        var variance = sumSquares / (values.Count - 1);
        return Math.Sqrt(variance) / Math.Sqrt(values.Count);
    }

    /// <summary>
    /// Parses a cached <c>&lt;c:numCache&gt;</c> XML fragment (as stored in
    /// <see cref="ChartModel.ErrorBarPlusRangeCacheXml"/>/<see cref="ChartModel.ErrorBarMinusRangeCacheXml"/>)
    /// into an index-ordered value list for Custom-kind error bars. Returns null for missing/unparsable input.
    /// </summary>
    private static IReadOnlyList<double>? ParseErrorBarRangeCache(string? cacheXml)
    {
        if (string.IsNullOrWhiteSpace(cacheXml))
            return null;

        try
        {
            var element = XElement.Parse(cacheXml);
            var points = new SortedDictionary<int, double>();
            foreach (var pt in element.Elements().Where(e => e.Name.LocalName == "pt"))
            {
                var idxAttribute = pt.Attribute("idx");
                var valueElement = pt.Elements().FirstOrDefault(e => e.Name.LocalName == "v");
                if (idxAttribute is null || valueElement is null)
                    continue;
                if (int.TryParse(idxAttribute.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx)
                    && double.TryParse(valueElement.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                    points[idx] = value;
            }

            return points.Count == 0 ? null : points.Values.ToArray();
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

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
