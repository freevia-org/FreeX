using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

public static partial class XlsxChartPartReader
{
    private static bool TryReadAreaLineComboChart(
        XDocument chartXml,
        XElement? plotArea,
        IReadOnlyList<XElement> areaCharts,
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
            Type = ChartType.Area,
            Title = XlsxChartLevelReader.ReadTitle(chartXml)
        };

        foreach (var areaChart in areaCharts)
        {
            var usesSecondaryAxis = XlsxChartSeriesRangeReader.UsesSecondaryValueAxis(plotArea, areaChart);
            var fallbackSeriesIndex = 0;
            foreach (var series in areaChart.Elements(ChartNs + "ser"))
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

                // Secondary-axis membership comes straight from which value axis the series' own
                // <c:areaChart> targets, so it is authoritative even for series index 0 — Excel
                // allows Format Data Series > Secondary Axis on the first series too
                // (R25-chart-axis-series-deep-1). Mirrors the XlsxChartPartReader.Bar.cs fix.
                if (usesSecondaryAxis)
                    result.SecondaryAxisSeriesIndexes.Add(seriesIndex);

                if (XlsxChartSeriesFormatReader.TryReadSeriesFill(series, seriesIndex, out var format))
                    result.SeriesFormats.Add(format);

                XlsxChartDataLabelReader.ApplyPointDataLabels(series, seriesIndex, result);
                XlsxChartTrendlineErrorBarReader.ApplyTrendline(series, result);
                XlsxChartTrendlineErrorBarReader.ApplyErrorBars(series, result);
                fallbackSeriesIndex++;
            }
        }

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

                result.ComboLineSeriesIndexes.Add(seriesIndex);
                // Same rationale as the areaChart loop above: a <c:lineChart> that declares the
                // secondary axId is authoritative regardless of series index — a line overlay is
                // frequently plotted first, at idx 0, over the primary-axis area series.
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

        // R110-io-chart-series-embedded-area: mirrors XlsxChartPartReader.Bar.cs's
        // TryReadBarLineComboChart fallback — when all val/cat formulas across the area+line combo
        // series are named ranges or all cross-sheet cell refs, `ranges` stays empty and this
        // function used to unconditionally return false, silently dropping the whole combo chart.
        var allAreaComboSeriesElements = areaCharts.Concat(lineCharts)
            .SelectMany(c => c.Elements(ChartNs + "ser"))
            .ToList();
        var areaComboEmbeddedData = XlsxChartSeriesRangeReader.TryReadEmbeddedSeriesData(allAreaComboSeriesElements, sheetId)
                                    ?? XlsxChartSeriesRangeReader.TryReadCrossSheetEmbeddedData(allAreaComboSeriesElements, sheetId, sheetNameResolver);
        if (areaComboEmbeddedData is not null)
        {
            var placeholderSheet = ranges.Count > 0 ? ranges[0].Start.Sheet : sheetId;
            result.DataRange = ranges.Count > 0
                ? XlsxChartSeriesRangeReader.UnionRanges(ranges)
                : new GridRange(
                    new CellAddress(placeholderSheet, 1, 1),
                    new CellAddress(placeholderSheet, 1, 1));
            result.FirstRowIsHeader = hasTitleRange;
            result.FirstColIsCategories = hasCategoryRange;
            result.EmbeddedSeriesData = areaComboEmbeddedData;
            result.SecondaryAxisSeriesIndexes = result.SecondaryAxisSeriesIndexes
                .Where(index => index >= 0)
                .Distinct()
                .Order()
                .ToList();
            result.ComboLineSeriesIndexes = result.ComboLineSeriesIndexes
                .Where(index => index >= 0)
                .Distinct()
                .Order()
                .ToList();
            result.ShowSecondaryAxis = result.SecondaryAxisSeriesIndexes.Count > 0;
            result.UseComboLineForSecondarySeries = result.ComboLineSeriesIndexes.Count > 0;
            result.SeriesInRows = XlsxChartSeriesRangeReader.DetectSeriesInRows(
                allAreaComboSeriesElements,
                sheetId,
                sheetNameResolver);
            ApplyVerbatimSeriesFormulasIfNeeded(allAreaComboSeriesElements, sheetId, sheetNameResolver, result);
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

        // Do NOT drop index 0 from either list — secondary-axis and combo-line membership are both
        // authoritative for the first series (R25-chart-axis-series-deep-1). Mirrors Bar.cs; the
        // sanitizer bounds these against the real series count.
        result.SecondaryAxisSeriesIndexes = result.SecondaryAxisSeriesIndexes
            .Where(index => index >= 0)
            .Distinct()
            .Order()
            .ToList();
        result.ComboLineSeriesIndexes = result.ComboLineSeriesIndexes
            .Where(index => index >= 0)
            .Distinct()
            .Order()
            .ToList();
        result.ShowSecondaryAxis = result.SecondaryAxisSeriesIndexes.Count > 0;
        result.UseComboLineForSecondarySeries = result.ComboLineSeriesIndexes.Count > 0;
        result.DataRange = XlsxChartSeriesRangeReader.UnionRanges(ranges);
        result.FirstRowIsHeader = hasTitleRange;
        result.FirstColIsCategories = hasCategoryRange;
        result.SeriesInRows = XlsxChartSeriesRangeReader.DetectSeriesInRows(
            areaCharts.Concat(lineCharts).SelectMany(c => c.Elements(ChartNs + "ser")),
            sheetId,
            sheetNameResolver);
        ApplyVerbatimSeriesFormulasIfNeeded(
            areaCharts.Concat(lineCharts).SelectMany(c => c.Elements(ChartNs + "ser")),
            sheetId,
            sheetNameResolver,
            result);
        XlsxChartLevelReader.ApplyChartLevelProperties(chartXml, result);
        XlsxChartSanitizer.SanitizeLoadedChart(result);
        chart = result;
        return true;
    }

    private static bool TryReadAreaChart(
        XDocument chartXml,
        XElement? plotArea,
        IReadOnlyList<XElement> areaCharts,
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

        foreach (var areaChart in areaCharts)
        {
            var usesSecondaryAxis = XlsxChartSeriesRangeReader.UsesSecondaryValueAxis(plotArea, areaChart);
            var fallbackSeriesIndex = 0;
            foreach (var series in areaChart.Elements(ChartNs + "ser"))
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

                // Authoritative for any series index, including 0 (R25-chart-axis-series-deep-1) —
                // see the combo reader above and XlsxChartPartReader.Bar.cs.
                if (usesSecondaryAxis)
                    result.SecondaryAxisSeriesIndexes.Add(seriesIndex);

                if (XlsxChartSeriesFormatReader.TryReadSeriesFill(series, seriesIndex, out var format))
                    result.SeriesFormats.Add(format);

                XlsxChartDataLabelReader.ApplyPointDataLabels(series, seriesIndex, result);
                XlsxChartTrendlineErrorBarReader.ApplyTrendline(series, result);
                XlsxChartTrendlineErrorBarReader.ApplyErrorBars(series, result);
                fallbackSeriesIndex++;
            }
        }

        // R110-io-chart-series-embedded-area: mirrors XlsxChartPartReader.Bar.cs's
        // TryReadBarChart fallback — see the identical block/comment in TryReadAreaLineComboChart above.
        var allAreaSeriesElements = areaCharts.SelectMany(c => c.Elements(ChartNs + "ser")).ToList();
        var areaEmbeddedData = XlsxChartSeriesRangeReader.TryReadEmbeddedSeriesData(allAreaSeriesElements, sheetId)
                               ?? XlsxChartSeriesRangeReader.TryReadCrossSheetEmbeddedData(allAreaSeriesElements, sheetId, sheetNameResolver);
        if (areaEmbeddedData is not null)
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
            result.EmbeddedSeriesData = areaEmbeddedData;
            result.SeriesInRows = XlsxChartSeriesRangeReader.DetectSeriesInRows(
                allAreaSeriesElements,
                sheetId,
                sheetNameResolver);
            ApplyVerbatimSeriesFormulasIfNeeded(allAreaSeriesElements, sheetId, sheetNameResolver, result);
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
            areaCharts.SelectMany(c => c.Elements(ChartNs + "ser")),
            sheetId,
            sheetNameResolver);
        ApplyVerbatimSeriesFormulasIfNeeded(
            areaCharts.SelectMany(c => c.Elements(ChartNs + "ser")),
            sheetId,
            sheetNameResolver,
            result);
        XlsxChartLevelReader.ApplyChartLevelProperties(chartXml, result);
        XlsxChartSanitizer.SanitizeLoadedChart(result);
        chart = result;
        return true;
    }

    /// <summary>
    /// Maps a <c>&lt;c:areaChart&gt;</c>'s <c>&lt;c:grouping val="…"/&gt;</c> to the matching Area
    /// <see cref="ChartType"/> (mirroring <see cref="ReadBarChartType"/> for the bar/column family):
    /// <c>stacked</c> → <see cref="ChartType.StackedArea"/>, <c>percentStacked</c> →
    /// <see cref="ChartType.PercentStackedArea"/>, and <c>standard</c> / missing → plain
    /// <see cref="ChartType.Area"/>. Without this, a Stacked or 100%-Stacked Area chart round-trips
    /// as a plain overlapping Area chart.
    /// </summary>
    private static ChartType ReadAreaChartType(XElement? areaChart) =>
        areaChart?.Element(ChartNs + "grouping")?.Attribute("val")?.Value switch
        {
            "stacked" => ChartType.StackedArea,
            "percentStacked" => ChartType.PercentStackedArea,
            _ => ChartType.Area
        };
}
