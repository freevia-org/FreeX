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
        var series = chart.Type switch
        {
            ChartType.Scatter => ExtractScatterSeries(chart, cellAccessor, startRow, dataStartRow, endRow, startCol, dataStartCol, endCol),
            ChartType.Bubble => ExtractBubbleSeries(chart, cellAccessor, startRow, dataStartRow, endRow, startCol, endCol),
            // R117-presentation-chart-stock-ohlc-1: Stock was falling through to the generic
            // ExtractSeries below (one ChartSeriesData PER COLUMN, only Values set), but
            // ChartLayoutEngine.LayoutStock reads a SINGLE series' HighValues/LowValues/OpenValues --
            // those were therefore never populated even for an ordinary (non-fallback) cell-range
            // Stock chart, so LayoutStock's `if (highs is null || lows is null)` guard always hit and
            // every Stock chart rendered with zero StockElements. ExtractStockSeries merges the
            // [Volume]/[Open]/High/Low/Close columns (per chart.StockSubtype, mirroring
            // ChartRenderer.Stock.cs's BuildStockModel column-offset convention) into that one shape.
            ChartType.Stock => ExtractStockSeries(chart, cellAccessor, dataStartRow, endRow, dataStartCol, endCol),
            _ => ExtractSeries(chart, cellAccessor, startRow, dataStartRow, endRow, dataStartCol, endCol),
        };

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
        // R117-presentation-chart-stock-ohlc-1: a Stock chart's embedded data is a LIST of
        // per-dimension series (one entry per Open/High/Low/Close <c:ser>, in that fixed OOXML
        // document order -- see XlsxChartPartReader.Line.cs), not one entry per plotted series like
        // every other chart type. Merge it into the single High/Low/Open/Close-shaped
        // ChartSeriesData LayoutStock expects instead of falling through to the generic per-entry
        // loop below (which only ever set Values, leaving HighValues/LowValues null and producing a
        // blank chart exactly like the live-range ExtractSeries gap this round also fixed).
        if (chart.Type == ChartType.Stock)
            return BuildStockRequestFromEmbeddedData(chart, embeddedSeries, plotArea, textMeasurer);

        // Categories are shared across series in the model (one axis), but each embedded series
        // carries its own cached copy (they read the same <c:cat>/<c:xVal> formula), and a series
        // whose own cache was truncated/short can still disagree with a sibling's -- e.g. series 0
        // cached only 2 categories while series 1's cache (or its Values) run to 5. Every
        // ChartLayoutEngine label lookup guards with `i < request.Categories.Count`, but several
        // geometry loops (see LayoutStackedColumns/LayoutStackedBars/LayoutClusteredBars/
        // StackedTotals's `i < series.Values.Count && i < categoryCount`, and ResolveCategoryCount's
        // own "Categories.Count wins when non-zero") use request.Categories.Count as the
        // authoritative point count too -- so picking a too-short cache doesn't just mislabel the
        // extra points, it can silently drop them from the plotted geometry. Use the LONGEST
        // non-empty cache across all series so the axis covers every series' points; this can only
        // add correctly-labelled category slots versus the old first-non-empty pick, never remove
        // any that were already covered.
        var categories = embeddedSeries
            .Select(s => s.Categories)
            .Where(c => c.Count > 0)
            .OrderByDescending(c => c.Count)
            .FirstOrDefault()?.ToList() ?? [];

        // Scatter AND Bubble both read their embedded data with categoryContainerName "xVal" (see
        // XlsxChartPartReader.Scatter.cs and XlsxChartPartReader.PieBubble.cs's TryReadBubbleChart),
        // so for either type this series' own Categories are its X values as formatted numeric
        // strings, not axis labels.
        var usesXValCache = chart.Type is ChartType.Scatter or ChartType.Bubble;
        var series = new List<ChartSeriesData>(embeddedSeries.Count);
        foreach (var embedded in embeddedSeries)
        {
            IReadOnlyList<double>? xValues = null;
            if (usesXValCache)
            {
                // A point whose cached text doesn't parse falls back to its positional index,
                // matching ExtractScatterSeries/ExtractBubbleSeries.
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

            // R117-io-chart-embedded-bubble-size-1: XlsxChartPartReader.PieBubble.cs now captures
            // the series' cached <c:bubbleSize> numCache into embedded.SizeValues, so a Bubble chart
            // reaching this fallback path renders real per-point sizes instead of always falling
            // back to the default/minimum radius (see ExtractBubbleSeries's live-cell-range
            // equivalent, which reads its size column the same way).
            series.Add(new ChartSeriesData
            {
                SeriesIndex = embedded.SeriesIndex,
                Name = embedded.SeriesName,
                Values = embedded.Values,
                XValues = xValues,
                SizeValues = chart.Type == ChartType.Bubble ? embedded.SizeValues : null,
            });
        }

        return new ChartLayoutRequest
        {
            Chart = chart,
            Categories = usesXValCache ? [] : categories,
            Series = series,
            PlotArea = plotArea,
            TextMeasurer = textMeasurer,
        };
    }

    /// <summary>
    /// Merges a Stock chart's embedded-fallback series list -- one <see cref="ChartEmbeddedSeriesData"/>
    /// per Open/High/Low/Close <c>&lt;c:ser&gt;</c>, in that fixed document order (see
    /// <c>XlsxChartPartReader.Line.cs</c>'s <c>TryReadLineLikeChart</c>, which the Stock reader reuses) --
    /// into the single High/Low/Open/Close-shaped <see cref="ChartSeriesData"/>
    /// <see cref="ChartLayoutEngine"/>'s <c>LayoutStock</c> expects. Mirrors
    /// <c>ChartRenderer.Stock.cs</c>'s <c>BuildStockModel</c> column-offset convention (using
    /// <see cref="ChartModel.StockSubtype"/> to tell Open-High-Low-Close from plain High-Low-Close),
    /// except there is no live volume column to fold in here: the volume bar series lives in a
    /// separate <c>&lt;c:barChart&gt;</c> the embedded-cache fallback never captures (only the
    /// <c>&lt;c:stockChart&gt;</c> OHLC series get read into <see cref="ChartModel.EmbeddedSeriesData"/>),
    /// so a fallback-loaded Volume-Stock chart still renders its price bars correctly but without the
    /// volume bars -- a known, separate follow-up (the portable engine also has no volume rendering
    /// support at all yet, live or fallback).
    /// </summary>
    private static ChartLayoutRequest BuildStockRequestFromEmbeddedData(
        ChartModel chart,
        List<ChartEmbeddedSeriesData> embeddedSeries,
        PlotRect plotArea,
        ITextMeasurer textMeasurer)
    {
        var categories = embeddedSeries
            .Select(s => s.Categories)
            .Where(c => c.Count > 0)
            .OrderByDescending(c => c.Count)
            .FirstOrDefault()?.ToList() ?? [];

        var hasOpenColumn = chart.StockSubtype is StockChartSubtype.OpenHighLowClose or StockChartSubtype.VolumeOpenHighLowClose
            || embeddedSeries.Count >= 4;

        List<ChartSeriesData> series;
        if (embeddedSeries.Count < 3)
        {
            series = [];
        }
        else
        {
            var index = 0;
            var opens = hasOpenColumn ? embeddedSeries[index++].Values : null;
            var highs = embeddedSeries[index++].Values;
            var lows = embeddedSeries[index++].Values;
            var closes = embeddedSeries[index].Values;

            series =
            [
                new ChartSeriesData
                {
                    SeriesIndex = 0,
                    Name = "Stock",
                    Values = closes,
                    HighValues = highs,
                    LowValues = lows,
                    OpenValues = opens,
                }
            ];
        }

        return new ChartLayoutRequest
        {
            Chart = chart,
            Categories = categories,
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

    private static List<ChartSeriesData> ExtractBubbleSeries(
        ChartModel chart,
        ChartCellAccessor cellAccessor,
        uint startRow,
        uint dataStartRow,
        uint endRow,
        uint startCol,
        uint endCol)
    {
        // Bubble deliberately ignores FirstColIsCategories -- the first column of the data range is
        // ALWAYS the shared X column (mirrors ChartRenderer.Bubble.cs's BuildBubbleModel, which reads
        // the unshifted startCol rather than the FirstColIsCategories-shifted dataStartCol other chart
        // types use here). Each subsequent (Y, Size) column pair becomes one series: yCol = xCol+1,
        // xCol+3, xCol+5, ... paired with the following size column.
        var xCol = startCol;
        var pointCapacity = (int)Math.Min(endRow - dataStartRow + 1, int.MaxValue);

        var xValues = new List<double>(pointCapacity);
        var rowOffset = 0;
        for (var r = dataStartRow; r <= endRow; r++, rowOffset++)
            xValues.Add(cellAccessor(r, xCol, out var x, out _) ? x : rowOffset);

        var series = new List<ChartSeriesData>();
        var seriesIndex = 0;
        for (var yCol = xCol + 1; yCol <= endCol; yCol += 2, seriesIndex++)
        {
            var sizeCol = yCol + 1;
            if (sizeCol > endCol)
                break;

            var values = new List<double?>(pointCapacity);
            var sizes = new List<double?>(pointCapacity);
            for (var r = dataStartRow; r <= endRow; r++)
            {
                values.Add(cellAccessor(r, yCol, out var y, out _) ? y : null);
                sizes.Add(cellAccessor(r, sizeCol, out var s, out _) ? s : null);
            }

            series.Add(new ChartSeriesData
            {
                SeriesIndex = seriesIndex,
                Name = ResolveSeriesName(chart, cellAccessor, startRow, yCol, seriesIndex),
                Values = values,
                XValues = xValues,
                SizeValues = sizes,
            });
        }

        return series;
    }

    /// <summary>
    /// Builds the single High/Low/Open/Close-shaped series <see cref="ChartLayoutEngine"/>'s
    /// <c>LayoutStock</c> expects from an ordinary (live) cell-range Stock chart's data columns.
    /// R117-presentation-chart-stock-ohlc-1: mirrors <c>ChartRenderer.Stock.cs</c>'s
    /// <c>BuildStockModel</c> column-offset convention exactly (volume column, if any, first; then
    /// Open, if any; then High, Low, Close) so the two renderers agree on which physical column is
    /// which OHLC dimension for the same chart.
    /// </summary>
    private static List<ChartSeriesData> ExtractStockSeries(
        ChartModel chart,
        ChartCellAccessor cellAccessor,
        uint dataStartRow,
        uint endRow,
        uint dataStartCol,
        uint endCol)
    {
        var valueColumnCount = endCol >= dataStartCol ? endCol - dataStartCol + 1 : 0;
        var hasVolumeColumn = chart.StockSubtype is StockChartSubtype.VolumeHighLowClose or StockChartSubtype.VolumeOpenHighLowClose;
        var hasOpenColumn = chart.StockSubtype is StockChartSubtype.OpenHighLowClose or StockChartSubtype.VolumeOpenHighLowClose ||
                            (!hasVolumeColumn && valueColumnCount >= 4);
        var volumeOffset = hasVolumeColumn ? 1u : 0u;
        var requiredValueColumns = volumeOffset + (hasOpenColumn ? 4u : 3u);
        if (valueColumnCount < requiredValueColumns)
            return [];

        var openCol = hasOpenColumn ? dataStartCol + volumeOffset : (uint?)null;
        var highCol = dataStartCol + volumeOffset + (hasOpenColumn ? 1u : 0u);
        var lowCol = highCol + 1;
        var closeCol = highCol + 2;
        if (closeCol > endCol)
            return [];

        var pointCapacity = (int)Math.Min(endRow - dataStartRow + 1, int.MaxValue);
        var highs = new List<double?>(pointCapacity);
        var lows = new List<double?>(pointCapacity);
        var closes = new List<double?>(pointCapacity);
        List<double?>? opens = openCol is null ? null : new List<double?>(pointCapacity);

        for (var r = dataStartRow; r <= endRow; r++)
        {
            highs.Add(cellAccessor(r, highCol, out var h, out _) ? h : null);
            lows.Add(cellAccessor(r, lowCol, out var l, out _) ? l : null);
            closes.Add(cellAccessor(r, closeCol, out var c, out _) ? c : null);
            opens?.Add(openCol is { } oc && cellAccessor(r, oc, out var o, out _) ? o : null);
        }

        return
        [
            new ChartSeriesData
            {
                SeriesIndex = 0,
                Name = "Stock",
                Values = closes,
                HighValues = highs,
                LowValues = lows,
                OpenValues = opens,
            }
        ];
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
