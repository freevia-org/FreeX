using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

public static partial class XlsxChartPartReader
{
    private static bool TryReadPieFamilyChart(
        XDocument chartXml,
        XElement pieFamilyChart,
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

        if (chartType == ChartType.Doughnut &&
            int.TryParse(pieFamilyChart.Element(ChartNs + "holeSize")?.Attribute("val")?.Value, out var holeSize))
        {
            result.DoughnutHoleSize = Math.Clamp(holeSize, 10, 90) / 100.0;
        }

        if (int.TryParse(pieFamilyChart.Element(ChartNs + "firstSliceAng")?.Attribute("val")?.Value, out var firstSliceAngle))
            result.FirstSliceAngle = Math.Clamp(firstSliceAngle, 0, 360);

        var seriesIndex = 0;
        foreach (var series in pieFamilyChart.Elements(ChartNs + "ser"))
        {
            hasTitleRange |= XlsxChartSeriesRangeReader.HasSeriesRangeFormula(series, "tx");
            hasCategoryRange |= XlsxChartSeriesRangeReader.HasSeriesRangeFormula(series, "cat");
            foreach (var formula in XlsxChartSeriesRangeReader.ReadSeriesRangeFormulas(series))
            {
                if (XlsxChartSeriesRangeReader.TryParseFormulaRange(formula, sheetId, sheetNameResolver, out var range))
                    ranges.Add(range);
            }

            var modelSeriesIndex = XlsxChartSeriesRangeReader.ReadSeriesIndex(series, seriesIndex);
            XlsxChartSeriesRangeReader.CaptureSeriesRoundTripMetadata(series, modelSeriesIndex, result);
            if (XlsxChartSeriesFormatReader.TryReadSeriesFill(series, modelSeriesIndex, out var format))
                result.SeriesFormats.Add(format);

            ApplyPieExplosion(series, modelSeriesIndex, result);
            XlsxChartSeriesFormatReader.ApplyPiePointFills(series, modelSeriesIndex, result);
            XlsxChartDataLabelReader.ApplyPointDataLabels(series, modelSeriesIndex, result);
            XlsxChartTrendlineErrorBarReader.ApplyTrendline(series, result);
            seriesIndex++;
        }

        // R110-io-chart-series-embedded-pie: mirrors XlsxChartPartReader.Bar.cs's TryReadBarChart
        // fallback — when all val/cat formulas are named ranges or all cross-sheet cell refs,
        // `ranges` stays empty and this function used to unconditionally return false, silently
        // dropping the whole Pie/Doughnut/3D-Pie chart from the workbook on load.
        var allPieSeriesElements = pieFamilyChart.Elements(ChartNs + "ser").ToList();
        var pieEmbeddedData = XlsxChartSeriesRangeReader.TryReadEmbeddedSeriesData(allPieSeriesElements, sheetId)
                              ?? XlsxChartSeriesRangeReader.TryReadCrossSheetEmbeddedData(allPieSeriesElements, sheetId, sheetNameResolver);
        if (pieEmbeddedData is not null)
        {
            var placeholderSheet = ranges.Count > 0 ? ranges[0].Start.Sheet : sheetId;
            result.DataRange = ranges.Count > 0
                ? XlsxChartSeriesRangeReader.UnionRanges(ranges)
                : new GridRange(
                    new CellAddress(placeholderSheet, 1, 1),
                    new CellAddress(placeholderSheet, 1, 1));
            result.FirstRowIsHeader = hasTitleRange;
            result.FirstColIsCategories = hasCategoryRange;
            result.EmbeddedSeriesData = pieEmbeddedData;
            result.SeriesInRows = XlsxChartSeriesRangeReader.DetectSeriesInRows(
                allPieSeriesElements,
                sheetId,
                sheetNameResolver);
            ApplyVerbatimSeriesFormulasIfNeeded(allPieSeriesElements, sheetId, sheetNameResolver, result);
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
        result.FirstRowIsHeader = hasTitleRange;
        result.FirstColIsCategories = hasCategoryRange;
        result.SeriesInRows = XlsxChartSeriesRangeReader.DetectSeriesInRows(
            pieFamilyChart.Elements(ChartNs + "ser"),
            sheetId,
            sheetNameResolver);
        ApplyVerbatimSeriesFormulasIfNeeded(
            pieFamilyChart.Elements(ChartNs + "ser"),
            sheetId,
            sheetNameResolver,
            result);
        XlsxChartLevelReader.ApplyChartLevelProperties(chartXml, result);
        XlsxChartSanitizer.SanitizeLoadedChart(result);
        chart = result;
        return true;
    }

    /// <summary>
    /// Reads every exploded <c>&lt;c:dPt&gt;</c> point on the series into
    /// <see cref="ChartModel.ExplodedSlices"/> (not just the first — a fully-exploded pie has
    /// one <c>dPt</c> per slice). The first exploded point read is also mirrored onto the
    /// scalar <see cref="ChartModel.ExplodedSliceIndex"/>/<see cref="ChartModel.ExplodedSliceDistance"/>
    /// for callers that only understand the single-explosion representation.
    /// </summary>
    private static void ApplyPieExplosion(XElement series, int seriesIndex, ChartModel chart)
    {
        var sawFirst = false;
        foreach (var point in series.Elements(ChartNs + "dPt"))
        {
            if (!int.TryParse(point.Element(ChartNs + "explosion")?.Attribute("val")?.Value, out var explosion) || explosion <= 0)
                continue;
            if (!int.TryParse(point.Element(ChartNs + "idx")?.Attribute("val")?.Value, out var index))
                continue;

            index = Math.Max(0, index);
            var distance = Math.Clamp(explosion / 100.0, 0, 0.5);
            chart.ExplodedSlices.Add(new ChartPointExplosion(seriesIndex, index, distance));

            if (!sawFirst)
            {
                chart.ExplodedSliceIndex = index;
                chart.ExplodedSliceDistance = distance;
                sawFirst = true;
            }
        }
    }

    private static bool TryReadBubbleChart(
        XDocument chartXml,
        XElement bubbleChart,
        SheetId sheetId,
        IReadOnlyDictionary<string, SheetId>? sheetNameResolver,
        out ChartModel chart)
    {
        var ranges = new List<GridRange>();
        var hasTitleRange = false;
        var result = new ChartModel
        {
            Type = ChartType.Bubble,
            Title = XlsxChartLevelReader.ReadTitle(chartXml),
            FirstColIsCategories = false,
            BubbleScale = ReadBubbleScale(bubbleChart),
            ShowNegativeBubbles = XlsxChartScalarReader.IsTrue(bubbleChart.Element(ChartNs + "showNegBubbles")?.Attribute("val")?.Value),
            BubbleSizeRepresents = ReadBubbleSizeRepresents(bubbleChart)
        };

        var seriesIndex = 0;
        foreach (var series in bubbleChart.Elements(ChartNs + "ser"))
        {
            var modelSeriesIndex = XlsxChartSeriesRangeReader.ReadSeriesIndex(series, seriesIndex);
            XlsxChartSeriesRangeReader.CaptureSeriesRoundTripMetadata(series, modelSeriesIndex, result);
            hasTitleRange |= XlsxChartSeriesRangeReader.HasSeriesRangeFormula(series, "tx");
            foreach (var formula in XlsxChartSeriesRangeReader.ReadSeriesRangeFormulas(series, "tx", "xVal", "yVal", "bubbleSize"))
            {
                if (XlsxChartSeriesRangeReader.TryParseFormulaRange(formula, sheetId, sheetNameResolver, out var range))
                    ranges.Add(range);
            }

            if (XlsxChartSeriesFormatReader.TryReadSeriesFill(series, modelSeriesIndex, out var format))
                result.SeriesFormats.Add(format);

            XlsxChartDataLabelReader.ApplyPointDataLabels(series, modelSeriesIndex, result);
            XlsxChartTrendlineErrorBarReader.ApplyTrendline(series, result);
            seriesIndex++;
        }

        // R110-io-chart-series-embedded-bubble: mirrors XlsxChartPartReader.Bar.cs's TryReadBarChart
        // fallback — a Bubble series carries its point data in <c:xVal>/<c:yVal> instead of
        // <c:cat>/<c:val>, so the embedded-data helpers are called with "yVal"/"xVal" as the
        // value/category container names (matching TryReadSeriesValueColumn/DetectSeriesInRows
        // below, which already override the default "val" container the same way for Bubble).
        // R117-io-chart-embedded-bubble-size-1: also pass sizeContainerName: "bubbleSize" so each
        // series' cached point sizes are captured into EmbeddedSeriesData.SizeValues — previously
        // this fell back to a chart with correct X/Y positions but every bubble at the
        // default/minimum radius, since ChartEmbeddedSeriesData had nowhere to carry the size cache.
        var allBubbleSeriesElements = bubbleChart.Elements(ChartNs + "ser").ToList();
        var bubbleEmbeddedData = XlsxChartSeriesRangeReader.TryReadEmbeddedSeriesData(
                                     allBubbleSeriesElements, sheetId, valueContainerName: "yVal", categoryContainerName: "xVal", sizeContainerName: "bubbleSize")
                                 ?? XlsxChartSeriesRangeReader.TryReadCrossSheetEmbeddedData(
                                     allBubbleSeriesElements, sheetId, sheetNameResolver, valueContainerName: "yVal", categoryContainerName: "xVal", sizeContainerName: "bubbleSize");
        if (bubbleEmbeddedData is not null)
        {
            var placeholderSheet = ranges.Count > 0 ? ranges[0].Start.Sheet : sheetId;
            result.DataRange = ranges.Count > 0
                ? XlsxChartSeriesRangeReader.UnionRanges(ranges)
                : new GridRange(
                    new CellAddress(placeholderSheet, 1, 1),
                    new CellAddress(placeholderSheet, 1, 1));
            result.FirstRowIsHeader = hasTitleRange;
            result.EmbeddedSeriesData = bubbleEmbeddedData;
            result.SeriesInRows = XlsxChartSeriesRangeReader.DetectSeriesInRows(
                allBubbleSeriesElements,
                sheetId,
                sheetNameResolver,
                valueContainerName: "yVal");
            ApplyVerbatimSeriesFormulasIfNeeded(allBubbleSeriesElements, sheetId, sheetNameResolver, result);
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
        result.FirstRowIsHeader = hasTitleRange;
        result.SeriesInRows = XlsxChartSeriesRangeReader.DetectSeriesInRows(
            bubbleChart.Elements(ChartNs + "ser"),
            sheetId,
            sheetNameResolver,
            valueContainerName: "yVal");
        ApplyVerbatimSeriesFormulasIfNeeded(
            bubbleChart.Elements(ChartNs + "ser"),
            sheetId,
            sheetNameResolver,
            result);
        XlsxChartLevelReader.ApplyChartLevelProperties(chartXml, result);
        XlsxChartSanitizer.SanitizeLoadedChart(result);
        chart = result;
        return true;
    }

    private static int ReadBubbleScale(XElement bubbleChart)
    {
        return int.TryParse(bubbleChart.Element(ChartNs + "bubbleScale")?.Attribute("val")?.Value, out var scale)
            ? Math.Clamp(scale, 0, 300)
            : 100;
    }

    private static ChartBubbleSizeRepresents ReadBubbleSizeRepresents(XElement bubbleChart) =>
        bubbleChart.Element(ChartNs + "sizeRepresents")?.Attribute("val")?.Value == "w"
            ? ChartBubbleSizeRepresents.Width
            : ChartBubbleSizeRepresents.Area;
}
