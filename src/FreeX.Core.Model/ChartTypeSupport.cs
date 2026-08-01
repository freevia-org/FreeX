namespace FreeX.Core.Model;

public static class ChartTypeSupport
{
    public static bool IsKnown(ChartType type) => Enum.IsDefined(type);

    public static bool IsAdvancedFamily(ChartType type) =>
        type is ChartType.Treemap
            or ChartType.Sunburst
            or ChartType.Histogram
            or ChartType.Pareto
            or ChartType.BoxAndWhisker
            or ChartType.Waterfall
            or ChartType.Funnel
            or ChartType.Map;

    public static bool IsChartExFamily(ChartType type) =>
        type is ChartType.Treemap
            or ChartType.Sunburst
            or ChartType.Histogram
            or ChartType.Pareto
            or ChartType.BoxAndWhisker
            or ChartType.Waterfall
            or ChartType.Funnel;

    public static bool IsAuthorable(ChartType type) =>
        IsRenderable(type);

    public static bool IsDeferredAuthoringFamily(ChartType type) =>
        IsKnown(type) && !IsAuthorable(type);

    public static bool IsRenderable(ChartType type) =>
        type is ChartType.Column
            or ChartType.StackedColumn
            or ChartType.PercentStackedColumn
            or ChartType.Line
            or ChartType.ThreeDLine
            or ChartType.Pie
            or ChartType.ThreeDPie
            or ChartType.Doughnut
            or ChartType.Bar
            or ChartType.StackedBar
            or ChartType.PercentStackedBar
            or ChartType.Scatter
            or ChartType.Bubble
            or ChartType.Area
            or ChartType.StackedArea
            or ChartType.PercentStackedArea
            or ChartType.Radar
            or ChartType.Stock
            or ChartType.Surface
            or ChartType.ThreeDSurface
            or ChartType.ThreeDColumn
            or ChartType.ThreeDBar
            or ChartType.ThreeDArea
            or ChartType.Waterfall
            or ChartType.Histogram
            or ChartType.Pareto
            or ChartType.BoxAndWhisker
            or ChartType.Treemap
            or ChartType.Sunburst
            or ChartType.Funnel;

    public static bool SupportsTrendlines(ChartType type) =>
        type is ChartType.Column or ChartType.Line or ChartType.ThreeDLine or ChartType.Bar or ChartType.Scatter or ChartType.Bubble or ChartType.Area or ChartType.ThreeDArea;

    public static bool SupportsSecondaryAxis(ChartType type) =>
        type is ChartType.Column or ChartType.StackedColumn or ChartType.PercentStackedColumn
            or ChartType.Line or ChartType.ThreeDLine
            or ChartType.Area or ChartType.StackedArea or ChartType.PercentStackedArea or ChartType.ThreeDArea
            or ChartType.Scatter
            or ChartType.Bar or ChartType.StackedBar or ChartType.PercentStackedBar;

    public static bool SupportsAxes(ChartType type) =>
        type is not ChartType.Pie and not ChartType.ThreeDPie and not ChartType.Doughnut;

    public static bool SupportsComboLineOverlay(ChartType type) =>
        type is ChartType.Column or ChartType.StackedColumn or ChartType.PercentStackedColumn
            or ChartType.Area or ChartType.StackedArea or ChartType.PercentStackedArea or ChartType.ThreeDArea;

    public static bool SupportsComboLineOverlay(ChartModel chart) =>
        SupportsComboLineOverlay(chart.Type) && GetDataSeriesCount(chart) >= 2;

    public static bool SupportsXAxisLogScale(ChartType type) =>
        type is ChartType.Bar or ChartType.StackedBar or ChartType.PercentStackedBar or ChartType.ThreeDBar or ChartType.Scatter or ChartType.Bubble;

    public static bool SupportsYAxisLogScale(ChartType type) =>
        type is ChartType.Column or ChartType.StackedColumn or ChartType.PercentStackedColumn or ChartType.Line or ChartType.ThreeDLine or ChartType.Scatter or ChartType.Bubble or ChartType.Area or ChartType.StackedArea or ChartType.PercentStackedArea or ChartType.ThreeDArea;

    public static bool SupportsXAxisBounds(ChartType type) => SupportsXAxisLogScale(type);

    public static bool SupportsYAxisBounds(ChartType type) => SupportsYAxisLogScale(type);

    public static bool SupportsSeriesMarkers(ChartType type) =>
        type is ChartType.Line or ChartType.ThreeDLine or ChartType.Scatter;

    public static bool SupportsSeriesLines(ChartType type) =>
        type is ChartType.StackedColumn
            or ChartType.PercentStackedColumn
            or ChartType.StackedBar
            or ChartType.PercentStackedBar;

    public static bool SupportsInvertIfNegative(ChartType type) =>
        type is ChartType.Column
            or ChartType.StackedColumn
            or ChartType.PercentStackedColumn
            or ChartType.Bar
            or ChartType.StackedBar
            or ChartType.PercentStackedBar
            or ChartType.ThreeDColumn
            or ChartType.ThreeDBar;

    public static bool SupportsPercentageDataLabels(ChartType type) =>
        type is ChartType.Pie or ChartType.ThreeDPie or ChartType.Doughnut or ChartType.PercentStackedColumn or ChartType.PercentStackedBar or ChartType.PercentStackedArea;

    public static bool SupportsFirstSliceAngle(ChartType type) =>
        type is ChartType.Pie or ChartType.ThreeDPie or ChartType.Doughnut;

    public static bool SupportsExplodedSlices(ChartType type) =>
        type is ChartType.Pie or ChartType.ThreeDPie or ChartType.Doughnut;

    public static bool SupportsDoughnutHoleSize(ChartType type) =>
        type is ChartType.Doughnut;

    public static bool SupportsBarGapWidth(ChartType type) =>
        type is ChartType.Column or ChartType.StackedColumn or ChartType.PercentStackedColumn or ChartType.ThreeDColumn
            or ChartType.Bar or ChartType.StackedBar or ChartType.PercentStackedBar or ChartType.ThreeDBar;

    /// <summary>
    /// Whether the OOXML plot-chart element for this type has a &lt;c:dLbls&gt; member in its schema.
    /// CT_SurfaceChart and CT_Surface3DChart (ECMA-376) have no dLbls member (only wireframe/ser/bandFmts/axId),
    /// so writing data labels there produces a schema-invalid chart part.
    /// </summary>
    public static bool SupportsDataLabels(ChartType type) =>
        type is not (ChartType.Surface or ChartType.ThreeDSurface);

    public static int GetDataSeriesCount(ChartModel chart)
    {
        // R112-charttypesupport-embedded-fallback-1: a chart preserved through the named-range
        // embedded-cache fallback (XlsxChartPartReader.*'s numCache/strCache readers) carries a
        // synthetic 1x1 placeholder DataRange -- its real series data lives in
        // EmbeddedSeriesData, one entry per series. Deriving purely from DataRange's row/column
        // span (as below) is UNCONDITIONALLY 0 or 1 for that placeholder regardless of how many
        // series/points the chart actually has, which made XlsxChartXmlWriter.IsSupportedXlsxChart
        // (GetDataSeriesCount(chart) > 0 && GetDataPointCount(chart) > 0) drop the whole chart on
        // the very next save, and starved every other DataRange-derived consumer (vary-colors,
        // combo-line eligibility, quick-format/quick-command enablement, etc.) of a real count.
        if (chart.EmbeddedSeriesData is { Count: > 0 } embeddedSeries)
            return embeddedSeries.Count;

        // One series per "strip": a column of DataRange by default, a row when SeriesInRows.
        var (seriesSpan, _) = GetOrientedSpans(chart);
        if (chart.Type == ChartType.Bubble)
            return Math.Max(0, (int)seriesSpan / 2);

        var skipped = SkippedLeadingSeriesStrips(chart);
        return seriesSpan + 1 <= skipped
            ? 0
            : (int)(seriesSpan + 1 - skipped);
    }

    public static int GetDataPointCount(ChartModel chart)
    {
        // See GetDataSeriesCount above: same synthetic-1x1-placeholder problem, so consult
        // EmbeddedSeriesData first. A series' point count is the larger of its cached Values and
        // Categories lists (categories can be populated even when a value is blank/missing), and
        // the chart-level count is the max across all series so a ragged embedded cache still
        // reports the longest series' extent (matching how the renderer sizes its category axis).
        if (chart.EmbeddedSeriesData is { Count: > 0 } embeddedPoints)
            return embeddedPoints.Max(s => Math.Max(s.Values.Count, s.Categories.Count));

        var (_, pointSpan) = GetOrientedSpans(chart);
        var skipped = chart.FirstRowIsHeader ? 1u : 0u;
        return pointSpan + 1 <= skipped
            ? 0
            : (int)(pointSpan + 1 - skipped);
    }

    /// <summary>
    /// Inclusive extents of the data range along the series axis (strips) and the point axis,
    /// as zero-based spans (count - 1). Column-major charts have series strips across columns and
    /// points down rows; <see cref="ChartModel.SeriesInRows"/> transposes both.
    /// </summary>
    private static (uint SeriesSpan, uint PointSpan) GetOrientedSpans(ChartModel chart)
    {
        var rowSpan = chart.DataRange.End.Row >= chart.DataRange.Start.Row
            ? chart.DataRange.End.Row - chart.DataRange.Start.Row
            : 0;
        var colSpan = chart.DataRange.End.Col >= chart.DataRange.Start.Col
            ? chart.DataRange.End.Col - chart.DataRange.Start.Col
            : 0;
        return chart.SeriesInRows ? (rowSpan, colSpan) : (colSpan, rowSpan);
    }

    /// <summary>
    /// Number of leading strips on the series axis that are not value series: the category strip
    /// (when present) plus the scatter X-value strip.
    /// </summary>
    private static uint SkippedLeadingSeriesStrips(ChartModel chart)
    {
        var skipped = HasCategoryStrip(chart) ? 1u : 0u;
        return chart.Type == ChartType.Scatter && !chart.FirstColIsCategories
            ? skipped + 1
            : skipped;
    }

    public static uint? GetXAxisValueColumn(ChartModel chart)
    {
        if (chart.Type is ChartType.Scatter or ChartType.Bubble)
            return chart.DataRange.Start.Col;

        return HasCategoryColumn(chart) ? chart.DataRange.Start.Col : null;
    }

    public static IReadOnlyList<uint> GetXAxisValueColumns(ChartModel chart)
    {
        if (chart.Type is ChartType.Scatter or ChartType.Bubble)
            return [chart.DataRange.Start.Col];

        if (chart.Type is ChartType.Bar or ChartType.StackedBar or ChartType.PercentStackedBar or ChartType.ThreeDBar)
            return GetSeriesValueColumns(chart);

        return [];
    }

    public static IReadOnlyList<uint> GetYAxisValueColumns(ChartModel chart)
    {
        if (chart.Type == ChartType.Bubble)
        {
            var columns = new List<uint>();
            for (var col = chart.DataRange.Start.Col + 1; col < chart.DataRange.End.Col; col += 2)
                columns.Add(col);
            return columns;
        }

        return GetSeriesValueColumns(chart);
    }

    private static IReadOnlyList<uint> GetSeriesValueColumns(ChartModel chart)
    {
        var startCol = GetSeriesValueStartColumn(chart);
        if (IsPastEndColumn(chart, startCol))
            return [];

        var columns = new List<uint>();
        for (var col = startCol; col <= chart.DataRange.End.Col; col++)
            columns.Add(col);
        return columns;
    }

    private static uint GetSeriesValueStartColumn(ChartModel chart)
    {
        var startCol = HasCategoryColumn(chart) ? chart.DataRange.Start.Col + 1 : chart.DataRange.Start.Col;
        return chart.Type == ChartType.Scatter && !chart.FirstColIsCategories
            ? startCol + 1
            : startCol;
    }

    private static bool IsPastEndColumn(ChartModel chart, uint column) =>
        column > chart.DataRange.End.Col;

    private static bool HasCategoryColumn(ChartModel chart) =>
        chart.FirstColIsCategories &&
        (chart.DataRange.End.Col > chart.DataRange.Start.Col ||
         chart.Type is not (ChartType.Histogram or ChartType.Pareto or ChartType.BoxAndWhisker));

    /// <summary>Orientation-aware <see cref="HasCategoryColumn"/>: is the first series-axis strip categories?</summary>
    private static bool HasCategoryStrip(ChartModel chart)
    {
        var (seriesSpan, _) = GetOrientedSpans(chart);
        return chart.FirstColIsCategories &&
            (seriesSpan > 0 ||
             chart.Type is not (ChartType.Histogram or ChartType.Pareto or ChartType.BoxAndWhisker));
    }
}
