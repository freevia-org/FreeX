using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

public static partial class XlsxChartPartReader
{
    private static bool TryReadScatterChart(
        XDocument chartXml,
        XElement? plotArea,
        IReadOnlyList<XElement> scatterCharts,
        SheetId sheetId,
        IReadOnlyDictionary<string, SheetId>? sheetNameResolver,
        out ChartModel chart)
    {
        var ranges = new List<GridRange>();
        var hasTitleRange = false;
        var result = new ChartModel
        {
            Type = ChartType.Scatter,
            Title = XlsxChartLevelReader.ReadTitle(chartXml),
            FirstColIsCategories = false
        };

        foreach (var scatterChart in scatterCharts)
        {
            var usesSecondaryAxis = XlsxChartSeriesRangeReader.UsesSecondaryValueAxis(plotArea, scatterChart);
            var fallbackSeriesIndex = 0;
            foreach (var series in scatterChart.Elements(ChartNs + "ser"))
            {
                var modelSeriesIndex = XlsxChartSeriesRangeReader.ReadSeriesIndex(series, fallbackSeriesIndex);
                XlsxChartSeriesRangeReader.CaptureSeriesRoundTripMetadata(series, modelSeriesIndex, result);
                hasTitleRange |= XlsxChartSeriesRangeReader.HasSeriesRangeFormula(series, "tx");
                foreach (var formula in XlsxChartSeriesRangeReader.ReadSeriesRangeFormulas(series, "tx", "xVal", "yVal"))
                {
                    if (XlsxChartSeriesRangeReader.TryParseFormulaRange(formula, sheetId, sheetNameResolver, out var range))
                        ranges.Add(range);
                }

                // Secondary-axis membership follows the series' own <c:scatterChart> value axis, so
                // it is authoritative even for series index 0 — Excel allows Format Data Series >
                // Secondary Axis on the first series too (R25-chart-axis-series-deep-1, mirrors Bar.cs).
                if (usesSecondaryAxis)
                    result.SecondaryAxisSeriesIndexes.Add(modelSeriesIndex);

                if (XlsxChartSeriesFormatReader.TryReadSeriesLine(series, modelSeriesIndex, out var format))
                    result.SeriesFormats.Add(format);

                // R44-io-chart-datapoint-3-1: per-point <c:dPt> fill overrides (e.g. a single
                // scatter/bubble marker highlighted with Format Data Point > Fill) were previously
                // only read for the pie/doughnut family; scatter series dropped them silently.
                // ApplyPiePointFills is generic over any <c:ser> with <c:dPt> children.
                XlsxChartSeriesFormatReader.ApplyPiePointFills(series, modelSeriesIndex, result);
                XlsxChartDataLabelReader.ApplyPointDataLabels(series, modelSeriesIndex, result);
                XlsxChartTrendlineErrorBarReader.ApplyTrendline(series, result);
                XlsxChartTrendlineErrorBarReader.ApplyErrorBars(series, result);
                fallbackSeriesIndex++;
            }
        }

        // R110-io-chart-series-embedded-scatter: mirrors XlsxChartPartReader.Bar.cs's
        // TryReadBarChart fallback — a Scatter series carries its point data in <c:xVal>/<c:yVal>
        // instead of <c:cat>/<c:val>, so the embedded-data helpers are called with "yVal"/"xVal" as
        // the value/category container names (matching DetectSeriesInRows below, which already
        // overrides the default "val" container the same way for Scatter).
        var allScatterSeriesElements = scatterCharts.SelectMany(c => c.Elements(ChartNs + "ser")).ToList();
        var scatterEmbeddedData = XlsxChartSeriesRangeReader.TryReadEmbeddedSeriesData(
                                      allScatterSeriesElements, sheetId, valueContainerName: "yVal", categoryContainerName: "xVal")
                                  ?? XlsxChartSeriesRangeReader.TryReadCrossSheetEmbeddedData(
                                      allScatterSeriesElements, sheetId, sheetNameResolver, valueContainerName: "yVal", categoryContainerName: "xVal");
        if (scatterEmbeddedData is not null)
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
            result.EmbeddedSeriesData = scatterEmbeddedData;
            result.SeriesInRows = XlsxChartSeriesRangeReader.DetectSeriesInRows(
                allScatterSeriesElements,
                sheetId,
                sheetNameResolver,
                valueContainerName: "yVal");
            ApplyVerbatimSeriesFormulasIfNeeded(allScatterSeriesElements, sheetId, sheetNameResolver, result);
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
        result.SeriesInRows = XlsxChartSeriesRangeReader.DetectSeriesInRows(
            scatterCharts.SelectMany(c => c.Elements(ChartNs + "ser")),
            sheetId,
            sheetNameResolver,
            valueContainerName: "yVal");
        ApplyVerbatimSeriesFormulasIfNeeded(
            scatterCharts.SelectMany(c => c.Elements(ChartNs + "ser")),
            sheetId,
            sheetNameResolver,
            result);
        XlsxChartLevelReader.ApplyChartLevelProperties(chartXml, result);
        XlsxChartSanitizer.SanitizeLoadedChart(result);
        chart = result;
        return true;
    }
}
