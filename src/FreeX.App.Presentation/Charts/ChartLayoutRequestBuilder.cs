using System.Globalization;
using System.Linq;

using FreeX.App.Presentation.Text;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Charts;

/// <summary>
/// Resolves a sheet-hosted <see cref="ChartModel"/> into a portable <see cref="ChartLayoutRequest"/>
/// the <see cref="ChartLayoutEngine"/> can lay out. It mirrors the desktop renderer's cell-lookup and
/// series/category extraction but reads cell values through a small <see cref="ChartCellAccessor"/>
/// delegate so the logic stays UI-free and unit-testable.
/// </summary>
public static class ChartLayoutRequestBuilder
{
    /// <summary>
    /// Resolves the numeric value and display text of a single cell in the chart's data range.
    /// Returns false when the cell is blank/absent.
    /// </summary>
    public delegate bool ChartCellAccessor(uint row, uint col, out double value, out string displayText);

    /// <summary>
    /// Builds a <see cref="ChartLayoutRequest"/> for <paramref name="chart"/> using
    /// <paramref name="cellAccessor"/> to read its data-range cells and <paramref name="plotArea"/>
    /// (the chart's on-sheet pixel rectangle) as the layout region. Returns null when the chart type
    /// is not laid out by the portable engine.
    /// </summary>
    public static ChartLayoutRequest? TryBuild(
        ChartModel chart,
        PlotRect plotArea,
        ChartCellAccessor cellAccessor,
        ITextMeasurer textMeasurer)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(cellAccessor);
        ArgumentNullException.ThrowIfNull(textMeasurer);

        if (!ChartLayoutEngine.IsSupported(chart.Type))
            return null;

        // R113-presentation-chart-embedded-fallback-1: a chart preserved through the named-range
        // embedded-cache fallback (XlsxChartPartReader.*'s numCache/strCache readers, r110-r112)
        // carries a synthetic 1x1 placeholder DataRange -- its real series/point data lives in
        // chart.EmbeddedSeriesData instead. This is the SAME authoritative-when-present rule
        // ChartTypeSupport.GetDataSeriesCount/GetDataPointCount already apply (r112); reuse it here
        // rather than re-deriving from the placeholder DataRange, which either returns null outright
        // (a header row makes the synthetic range's dataStartRow > endRow) or builds a meaningless
        // single-point series from whatever real cell happens to sit at the placeholder's (1,1)
        // address. Cell-accessor lookups can never recover the real data here anyway (the accessor
        // has no way to address a different sheet than the one it was built for), so preferring the
        // embedded cache whenever it is present is a strict improvement, never a regression.
        if (chart.EmbeddedSeriesData is { Count: > 0 } embeddedSeries)
            return BuildFromEmbeddedData(chart, embeddedSeries, plotArea, textMeasurer);

        var range = chart.DataRange;
        var startRow = range.Start.Row;
        var endRow = range.End.Row;
        var startCol = range.Start.Col;
        var endCol = range.End.Col;
        if (endRow < startRow || endCol < startCol)
            return null;

        if (chart.SeriesInRows)
        {
            // Excel's "Switch Row/Column": present the transposed range to the extraction below.
            // Virtual (row, col) reads actual (startRow + (col - startCol), startCol + (row - startRow));
            // the start corner is shared so only the end extents swap, and each ROW of the actual
            // range becomes one series (names from the first column, categories from the first row).
            var actual = cellAccessor;
            var baseRow = startRow;
            var baseCol = startCol;
            cellAccessor = (uint row, uint col, out double value, out string displayText) =>
                actual(baseRow + (col - baseCol), baseCol + (row - baseRow), out value, out displayText);
            (endRow, endCol) = (startRow + (endCol - startCol), startCol + (endRow - startRow));
        }

        var dataStartRow = chart.FirstRowIsHeader ? startRow + 1 : startRow;
        var dataStartCol = chart.FirstColIsCategories ? startCol + 1 : startCol;
        if (dataStartRow > endRow || dataStartCol > endCol)
            return null;

        var categories = ExtractCategories(chart, cellAccessor, startCol, dataStartRow, endRow);
        var series = chart.Type == ChartType.Scatter
            ? ExtractScatterSeries(chart, cellAccessor, startRow, dataStartRow, endRow, startCol, dataStartCol, endCol)
            : ExtractSeries(chart, cellAccessor, startRow, dataStartRow, endRow, dataStartCol, endCol);

        return new ChartLayoutRequest
        {
            Chart = chart,
            Categories = categories,
            Series = series,
            PlotArea = plotArea,
            TextMeasurer = textMeasurer,
        };
    }

    /// <summary>
    /// Builds a <see cref="ChartLayoutRequest"/> directly from <see cref="ChartModel.EmbeddedSeriesData"/>,
    /// bypassing the cell accessor entirely. Used when the chart's series formulas are unresolvable
    /// named ranges (or unreachable cross-sheet references) but the chart XML carries embedded
    /// <c>&lt;c:numCache&gt;</c>/<c>&lt;c:strCache&gt;</c> values -- see the r113 note in
    /// <see cref="TryBuild"/>. Every <see cref="ChartLayoutEngine.Layout"/> branch consumes the same
    /// <see cref="ChartSeriesData"/>/category shape produced here, so this one accessor covers the
    /// whole chart-type family rather than special-casing each layout branch.
    /// </summary>
    private static ChartLayoutRequest BuildFromEmbeddedData(
        ChartModel chart,
        List<ChartEmbeddedSeriesData> embeddedSeries,
        PlotRect plotArea,
        ITextMeasurer textMeasurer)
    {
        // Categories are shared across series in the model (one axis), but each embedded series
        // carries its own cached copy (they read the same <c:cat>/<c:xVal> formula) -- use the first
        // non-empty one, matching the desktop renderer's BuildPlotModelFromEmbeddedData fallback.
        var categories = embeddedSeries
            .Select(s => s.Categories)
            .FirstOrDefault(c => c.Count > 0)?.ToList() ?? [];

        var isScatter = chart.Type == ChartType.Scatter;
        var series = new List<ChartSeriesData>(embeddedSeries.Count);
        foreach (var embedded in embeddedSeries)
        {
            IReadOnlyList<double>? xValues = null;
            if (isScatter)
            {
                // Scatter reads its embedded data with categoryContainerName "xVal" (see
                // XlsxChartPartReader.Scatter.cs), so this series' own Categories are its X values
                // as formatted numeric strings, not axis labels. A point whose cached text doesn't
                // parse falls back to its positional index, matching ExtractScatterSeries.
                var xs = new double[embedded.Values.Count];
                for (var i = 0; i < xs.Length; i++)
                {
                    var text = i < embedded.Categories.Count ? embedded.Categories[i] : null;
                    xs[i] = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                        ? x
                        : i;
                }

                xValues = xs;
            }

            series.Add(new ChartSeriesData
            {
                SeriesIndex = embedded.SeriesIndex,
                Name = embedded.SeriesName,
                Values = embedded.Values,
                XValues = xValues,
            });
        }

        return new ChartLayoutRequest
        {
            Chart = chart,
            Categories = isScatter ? [] : categories,
            Series = series,
            PlotArea = plotArea,
            TextMeasurer = textMeasurer,
        };
    }

    private static List<string> ExtractCategories(
        ChartModel chart,
        ChartCellAccessor cellAccessor,
        uint categoryCol,
        uint dataStartRow,
        uint endRow)
    {
        if (!chart.FirstColIsCategories)
            return [];

        var categories = new List<string>((int)Math.Min(endRow - dataStartRow + 1, int.MaxValue));
        for (var r = dataStartRow; r <= endRow; r++)
        {
            // Categories are label text — keep the display text regardless of whether the cell also
            // carries a numeric value.
            cellAccessor(r, categoryCol, out _, out var text);
            categories.Add(text);
        }

        return categories;
    }

    private static List<ChartSeriesData> ExtractSeries(
        ChartModel chart,
        ChartCellAccessor cellAccessor,
        uint startRow,
        uint dataStartRow,
        uint endRow,
        uint dataStartCol,
        uint endCol)
    {
        var series = new List<ChartSeriesData>((int)Math.Min(endCol - dataStartCol + 1, int.MaxValue));
        var seriesIndex = 0;
        for (var col = dataStartCol; col <= endCol; col++, seriesIndex++)
        {
            var values = new List<double?>((int)Math.Min(endRow - dataStartRow + 1, int.MaxValue));
            for (var r = dataStartRow; r <= endRow; r++)
                values.Add(cellAccessor(r, col, out var v, out _) ? v : null);

            series.Add(new ChartSeriesData
            {
                SeriesIndex = seriesIndex,
                Name = ResolveSeriesName(chart, cellAccessor, startRow, col, seriesIndex),
                Values = values,
            });
        }

        return series;
    }

    private static List<ChartSeriesData> ExtractScatterSeries(
        ChartModel chart,
        ChartCellAccessor cellAccessor,
        uint startRow,
        uint dataStartRow,
        uint endRow,
        uint startCol,
        uint dataStartCol,
        uint endCol)
    {
        // Scatter: the X values come from the first column (the category column when
        // FirstColIsCategories, otherwise the first data column), each later column is a Y series.
        var xCol = chart.FirstColIsCategories ? startCol : dataStartCol;
        var pointCapacity = (int)Math.Min(endRow - dataStartRow + 1, int.MaxValue);

        var xValues = new List<double>(pointCapacity);
        var rowOffset = 0;
        for (var r = dataStartRow; r <= endRow; r++, rowOffset++)
            xValues.Add(cellAccessor(r, xCol, out var x, out _) ? x : rowOffset);

        var series = new List<ChartSeriesData>();
        var seriesIndex = 0;
        for (var col = dataStartCol; col <= endCol; col++, seriesIndex++)
        {
            var values = new List<double?>(pointCapacity);
            for (var r = dataStartRow; r <= endRow; r++)
                values.Add(cellAccessor(r, col, out var y, out _) ? y : null);

            series.Add(new ChartSeriesData
            {
                SeriesIndex = seriesIndex,
                Name = ResolveSeriesName(chart, cellAccessor, startRow, col, seriesIndex),
                Values = values,
                XValues = xValues,
            });
        }

        return series;
    }

    private static string ResolveSeriesName(
        ChartModel chart,
        ChartCellAccessor cellAccessor,
        uint headerRow,
        uint col,
        int seriesIndex)
    {
        if (chart.FirstRowIsHeader)
        {
            cellAccessor(headerRow, col, out _, out var header);
            if (!string.IsNullOrEmpty(header))
                return header;
        }

        return string.Create(CultureInfo.InvariantCulture, $"Series {seriesIndex + 1}");
    }
}
