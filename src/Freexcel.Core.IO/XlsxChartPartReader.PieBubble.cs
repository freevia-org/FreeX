using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

public static partial class XlsxChartPartReader
{
    private static bool TryReadPieFamilyChart(
        XDocument chartXml,
        XElement pieFamilyChart,
        SheetId sheetId,
        ChartType chartType,
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
                if (XlsxChartSeriesRangeReader.TryParseFormulaRange(formula, sheetId, out var range))
                    ranges.Add(range);
            }

            var modelSeriesIndex = XlsxChartSeriesRangeReader.ReadSeriesIndex(series, seriesIndex);
            if (XlsxChartSeriesFormatReader.TryReadSeriesFill(series, modelSeriesIndex, out var format))
                result.SeriesFormats.Add(format);

            ApplyPieExplosion(series, result);
            XlsxChartDataLabelReader.ApplyPointDataLabels(series, modelSeriesIndex, result);
            XlsxChartTrendlineErrorBarReader.ApplyTrendline(series, result);
            seriesIndex++;
        }

        if (ranges.Count == 0)
        {
            chart = new ChartModel();
            return false;
        }

        result.DataRange = XlsxChartSeriesRangeReader.UnionRanges(ranges);
        result.FirstRowIsHeader = hasTitleRange;
        result.FirstColIsCategories = hasCategoryRange;
        XlsxChartLevelReader.ApplyChartLevelProperties(chartXml, result);
        XlsxChartSanitizer.SanitizeLoadedChart(result);
        chart = result;
        return true;
    }

    private static void ApplyPieExplosion(XElement series, ChartModel chart)
    {
        var explodedPoint = series
            .Elements(ChartNs + "dPt")
            .FirstOrDefault(point => int.TryParse(point.Element(ChartNs + "explosion")?.Attribute("val")?.Value, out var value) && value > 0);
        if (explodedPoint is null)
            return;

        if (int.TryParse(explodedPoint.Element(ChartNs + "idx")?.Attribute("val")?.Value, out var index))
            chart.ExplodedSliceIndex = Math.Max(0, index);
        if (int.TryParse(explodedPoint.Element(ChartNs + "explosion")?.Attribute("val")?.Value, out var explosion))
            chart.ExplodedSliceDistance = Math.Clamp(explosion / 100.0, 0, 0.5);
    }

    private static bool TryReadBubbleChart(XDocument chartXml, XElement bubbleChart, SheetId sheetId, out ChartModel chart)
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
            hasTitleRange |= XlsxChartSeriesRangeReader.HasSeriesRangeFormula(series, "tx");
            foreach (var formula in XlsxChartSeriesRangeReader.ReadSeriesRangeFormulas(series, "tx", "xVal", "yVal", "bubbleSize"))
            {
                if (XlsxChartSeriesRangeReader.TryParseFormulaRange(formula, sheetId, out var range))
                    ranges.Add(range);
            }

            if (XlsxChartSeriesFormatReader.TryReadSeriesFill(series, modelSeriesIndex, out var format))
                result.SeriesFormats.Add(format);

            XlsxChartDataLabelReader.ApplyPointDataLabels(series, modelSeriesIndex, result);
            XlsxChartTrendlineErrorBarReader.ApplyTrendline(series, result);
            seriesIndex++;
        }

        if (ranges.Count == 0)
        {
            chart = new ChartModel();
            return false;
        }

        result.DataRange = XlsxChartSeriesRangeReader.UnionRanges(ranges);
        result.FirstRowIsHeader = hasTitleRange;
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
