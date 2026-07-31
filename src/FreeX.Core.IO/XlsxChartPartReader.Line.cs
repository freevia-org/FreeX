using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

public static partial class XlsxChartPartReader
{
    private static bool TryReadStockChart(
        XDocument chartXml,
        XElement? plotArea,
        IReadOnlyList<XElement> stockCharts,
        IReadOnlyList<XElement> barCharts,
        SheetId sheetId,
        IReadOnlyDictionary<string, SheetId>? sheetNameResolver,
        out ChartModel chart)
    {
        if (!TryReadLineLikeChart(chartXml, plotArea, stockCharts, sheetId, ChartType.Stock, sheetNameResolver, out chart))
            return false;

        var stockSeriesCount = stockCharts.Sum(plotChart => plotChart.Elements(ChartNs + "ser").Count());
        var volumeRanges = new List<GridRange>();
        foreach (var series in barCharts.SelectMany(plotChart => plotChart.Elements(ChartNs + "ser")))
        {
            foreach (var formula in XlsxChartSeriesRangeReader.ReadSeriesRangeFormulas(series))
            {
                if (XlsxChartSeriesRangeReader.TryParseFormulaRange(formula, sheetId, sheetNameResolver, out var range))
                    volumeRanges.Add(range);
            }
        }

        if (volumeRanges.Count > 0)
        {
            var ranges = new List<GridRange> { chart.DataRange };
            ranges.AddRange(volumeRanges);
            chart.DataRange = XlsxChartSeriesRangeReader.UnionRanges(ranges);
        }

        chart.StockSubtype = (volumeRanges.Count > 0, stockSeriesCount >= 4) switch
        {
            (true, true) => StockChartSubtype.VolumeOpenHighLowClose,
            (true, false) => StockChartSubtype.VolumeHighLowClose,
            (false, true) => StockChartSubtype.OpenHighLowClose,
            _ => StockChartSubtype.HighLowClose
        };

        return true;
    }

    private static bool TryReadLineLikeChart(
        XDocument chartXml,
        XElement? plotArea,
        IReadOnlyList<XElement> plotCharts,
        SheetId sheetId,
        ChartType chartType,
        IReadOnlyDictionary<string, SheetId>? sheetNameResolver,
        out ChartModel chart)
    {
        var ranges = new List<GridRange>();
        var hasTitleRange = false;
        var hasCategoryRange = false;
        var result = new ChartModel
        {
            Type = chartType,
            Title = XlsxChartLevelReader.ReadTitle(chartXml)
        };

        foreach (var plotChart in plotCharts)
        {
            XlsxChartTrendlineErrorBarReader.ApplyChartGuideLineMetadata(plotChart, result);
            var usesSecondaryAxis = XlsxChartSeriesRangeReader.UsesSecondaryValueAxis(plotArea, plotChart);
            var fallbackSeriesIndex = 0;
            foreach (var series in plotChart.Elements(ChartNs + "ser"))
            {
                var seriesIndex = XlsxChartSeriesRangeReader.ReadSeriesIndex(series, fallbackSeriesIndex);
                XlsxChartSeriesRangeReader.CaptureSeriesRoundTripMetadata(series, seriesIndex, result);
                hasTitleRange |= XlsxChartSeriesRangeReader.HasSeriesRangeFormula(series, "tx");
                hasCategoryRange |= XlsxChartSeriesRangeReader.HasSeriesRangeFormula(series, "cat");
                foreach (var formula in XlsxChartSeriesRangeReader.ReadSeriesRangeFormulas(series))
                {
                    if (XlsxChartSeriesRangeReader.TryParseFormulaRange(formula, sheetId, sheetNameResolver, out var range))
                        ranges.Add(range);
                }

                // Secondary-axis membership follows the series' own <c:lineChart> value axis, so it
                // is authoritative even for series index 0 — Excel allows Format Data Series >
                // Secondary Axis on the first series too (R25-chart-axis-series-deep-1, mirrors Bar.cs).
                if (usesSecondaryAxis)
                    result.SecondaryAxisSeriesIndexes.Add(seriesIndex);

                if (XlsxChartSeriesFormatReader.TryReadSeriesLine(series, seriesIndex, out var format))
                    result.SeriesFormats.Add(format);

                XlsxChartDataLabelReader.ApplyPointDataLabels(series, seriesIndex, result);
                XlsxChartTrendlineErrorBarReader.ApplyTrendline(series, result);
                XlsxChartTrendlineErrorBarReader.ApplyErrorBars(series, result);
                fallbackSeriesIndex++;
            }
        }

        // R110-io-chart-series-embedded-line: mirrors XlsxChartPartReader.Bar.cs's
        // TryReadBarChart fallback — when all val/cat formulas are named ranges (e.g. an
        // OFFSET-based dynamic range like 'Sheet1'!rngCount, the classic "auto-expanding chart"
        // pattern) or all cross-sheet cell refs, TryParseFormulaRange never populates `ranges` and
        // this function used to unconditionally return false, silently dropping the whole
        // Line/Radar/3D-Line/Stock chart from the workbook on load. Fall back to the embedded
        // numCache/strCache values instead, exactly like Bar/Column already does.
        var allLineLikeSeriesElements = plotCharts.SelectMany(c => c.Elements(ChartNs + "ser")).ToList();
        var lineLikeEmbeddedData = XlsxChartSeriesRangeReader.TryReadEmbeddedSeriesData(allLineLikeSeriesElements, sheetId)
                                   ?? XlsxChartSeriesRangeReader.TryReadCrossSheetEmbeddedData(allLineLikeSeriesElements, sheetId, sheetNameResolver);
        if (lineLikeEmbeddedData is not null)
        {
            var placeholderSheet = ranges.Count > 0 ? ranges[0].Start.Sheet : sheetId;
            result.DataRange = ranges.Count > 0
                ? XlsxChartSeriesRangeReader.UnionRanges(ranges)
                : new GridRange(
                    new CellAddress(placeholderSheet, 1, 1),
                    new CellAddress(placeholderSheet, 1, 1));
            result.FirstRowIsHeader = hasTitleRange;
            result.FirstColIsCategories = hasCategoryRange;
            result.EmbeddedSeriesData = lineLikeEmbeddedData;
            // R108-io-chart-series-embedded-fastpath discipline: SeriesInRows/
            // ApplyVerbatimSeriesFormulasIfNeeded must run on this early-return path too, or the
            // writer has nothing but a degenerate recomputed DataRange to re-derive series from on
            // save (see the identical comment in XlsxChartPartReader.Bar.cs).
            result.SecondaryAxisSeriesIndexes = result.SecondaryAxisSeriesIndexes
                .Where(index => index >= 0)
                .Distinct()
                .Order()
                .ToList();
            result.ShowSecondaryAxis = result.SecondaryAxisSeriesIndexes.Count > 0;
            result.SeriesInRows = XlsxChartSeriesRangeReader.DetectSeriesInRows(
                allLineLikeSeriesElements,
                sheetId,
                sheetNameResolver);
            ApplyVerbatimSeriesFormulasIfNeeded(allLineLikeSeriesElements, sheetId, sheetNameResolver, result);
            XlsxChartLevelReader.ApplyChartLevelProperties(chartXml, result);
            XlsxChartSanitizer.SanitizeLoadedChart(result);
            chart = result;
            return true;
        }

        if (ranges.Count == 0)
        {
            chart = new ChartModel();
            return false;
        }

        // Keep index 0 — a secondary-axis assignment is valid for the first series too
        // (R25-chart-axis-series-deep-1); the sanitizer bounds this against the real series count.
        result.SecondaryAxisSeriesIndexes = result.SecondaryAxisSeriesIndexes
            .Where(index => index >= 0)
            .Distinct()
            .Order()
            .ToList();
        result.ShowSecondaryAxis = result.SecondaryAxisSeriesIndexes.Count > 0;
        result.DataRange = XlsxChartSeriesRangeReader.UnionRanges(ranges);
        result.FirstRowIsHeader = hasTitleRange;
        result.FirstColIsCategories = hasCategoryRange;
        result.SeriesInRows = XlsxChartSeriesRangeReader.DetectSeriesInRows(
            plotCharts.SelectMany(c => c.Elements(ChartNs + "ser")),
            sheetId,
            sheetNameResolver);
        ApplyVerbatimSeriesFormulasIfNeeded(
            plotCharts.SelectMany(c => c.Elements(ChartNs + "ser")),
            sheetId,
            sheetNameResolver,
            result);
        XlsxChartLevelReader.ApplyChartLevelProperties(chartXml, result);
        XlsxChartSanitizer.SanitizeLoadedChart(result);
        chart = result;
        return true;
    }

    private static bool TryReadLineChart(
        XDocument chartXml,
        XElement? plotArea,
        IReadOnlyList<XElement> lineCharts,
        SheetId sheetId,
        IReadOnlyDictionary<string, SheetId>? sheetNameResolver,
        out ChartModel chart)
    {
        var ranges = new List<GridRange>();
        var hasTitleRange = false;
        var hasCategoryRange = false;
        var result = new ChartModel
        {
            Type = ChartType.Line,
            Title = XlsxChartLevelReader.ReadTitle(chartXml)
        };

        foreach (var lineChart in lineCharts)
        {
            XlsxChartTrendlineErrorBarReader.ApplyChartGuideLineMetadata(lineChart, result);
            var usesSecondaryAxis = XlsxChartSeriesRangeReader.UsesSecondaryValueAxis(plotArea, lineChart);
            var fallbackSeriesIndex = 0;
            foreach (var series in lineChart.Elements(ChartNs + "ser"))
            {
                var seriesIndex = XlsxChartSeriesRangeReader.ReadSeriesIndex(series, fallbackSeriesIndex);
                XlsxChartSeriesRangeReader.CaptureSeriesRoundTripMetadata(series, seriesIndex, result);
                hasTitleRange |= XlsxChartSeriesRangeReader.HasSeriesRangeFormula(series, "tx");
                hasCategoryRange |= XlsxChartSeriesRangeReader.HasSeriesRangeFormula(series, "cat");
                foreach (var formula in XlsxChartSeriesRangeReader.ReadSeriesRangeFormulas(series))
                {
                    if (XlsxChartSeriesRangeReader.TryParseFormulaRange(formula, sheetId, sheetNameResolver, out var range))
                        ranges.Add(range);
                }

                // Secondary-axis membership follows the series' own <c:lineChart> value axis, so it
                // is authoritative even for series index 0 — Excel allows Format Data Series >
                // Secondary Axis on the first series too (R25-chart-axis-series-deep-1, mirrors Bar.cs).
                if (usesSecondaryAxis)
                    result.SecondaryAxisSeriesIndexes.Add(seriesIndex);

                if (XlsxChartSeriesFormatReader.TryReadSeriesLine(series, seriesIndex, out var format))
                    result.SeriesFormats.Add(format);

                XlsxChartDataLabelReader.ApplyPointDataLabels(series, seriesIndex, result);
                XlsxChartTrendlineErrorBarReader.ApplyTrendline(series, result);
                XlsxChartTrendlineErrorBarReader.ApplyErrorBars(series, result);
                fallbackSeriesIndex++;
            }
        }

        // R110-io-chart-series-embedded-line: mirrors XlsxChartPartReader.Bar.cs's
        // TryReadBarChart fallback — see the identical block/comment in TryReadLineLikeChart above.
        var allLineSeriesElements = lineCharts.SelectMany(c => c.Elements(ChartNs + "ser")).ToList();
        var lineEmbeddedData = XlsxChartSeriesRangeReader.TryReadEmbeddedSeriesData(allLineSeriesElements, sheetId)
                               ?? XlsxChartSeriesRangeReader.TryReadCrossSheetEmbeddedData(allLineSeriesElements, sheetId, sheetNameResolver);
        if (lineEmbeddedData is not null)
        {
            var placeholderSheet = ranges.Count > 0 ? ranges[0].Start.Sheet : sheetId;
            result.DataRange = ranges.Count > 0
                ? XlsxChartSeriesRangeReader.UnionRanges(ranges)
                : new GridRange(
                    new CellAddress(placeholderSheet, 1, 1),
                    new CellAddress(placeholderSheet, 1, 1));
            result.SecondaryAxisSeriesIndexes = result.SecondaryAxisSeriesIndexes
                .Distinct()
                .Order()
                .ToList();
            result.ShowSecondaryAxis = result.SecondaryAxisSeriesIndexes.Count > 0;
            result.FirstRowIsHeader = hasTitleRange;
            result.FirstColIsCategories = hasCategoryRange;
            result.EmbeddedSeriesData = lineEmbeddedData;
            result.SeriesInRows = XlsxChartSeriesRangeReader.DetectSeriesInRows(
                allLineSeriesElements,
                sheetId,
                sheetNameResolver);
            ApplyVerbatimSeriesFormulasIfNeeded(allLineSeriesElements, sheetId, sheetNameResolver, result);
            XlsxChartLevelReader.ApplyChartLevelProperties(chartXml, result);
            XlsxChartSanitizer.SanitizeLoadedChart(result);
            chart = result;
            return true;
        }

        if (ranges.Count == 0)
        {
            chart = new ChartModel();
            return false;
        }

        result.DataRange = XlsxChartSeriesRangeReader.UnionRanges(ranges);
        result.SecondaryAxisSeriesIndexes = result.SecondaryAxisSeriesIndexes
            .Distinct()
            .Order()
            .ToList();
        result.ShowSecondaryAxis = result.SecondaryAxisSeriesIndexes.Count > 0;
        result.FirstRowIsHeader = hasTitleRange;
        result.FirstColIsCategories = hasCategoryRange;
        result.SeriesInRows = XlsxChartSeriesRangeReader.DetectSeriesInRows(
            lineCharts.SelectMany(c => c.Elements(ChartNs + "ser")),
            sheetId,
            sheetNameResolver);
        ApplyVerbatimSeriesFormulasIfNeeded(
            lineCharts.SelectMany(c => c.Elements(ChartNs + "ser")),
            sheetId,
            sheetNameResolver,
            result);
        XlsxChartLevelReader.ApplyChartLevelProperties(chartXml, result);
        XlsxChartSanitizer.SanitizeLoadedChart(result);
        chart = result;
        return true;
    }
}
