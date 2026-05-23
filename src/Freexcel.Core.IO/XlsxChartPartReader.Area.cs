using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

public static partial class XlsxChartPartReader
{
    private static bool TryReadAreaLineComboChart(
        XDocument chartXml,
        XElement? plotArea,
        IReadOnlyList<XElement> areaCharts,
        IReadOnlyList<XElement> lineCharts,
        SheetId sheetId,
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
                hasTitleRange |= XlsxChartSeriesRangeReader.HasSeriesRangeFormula(series, "tx");
                hasCategoryRange |= XlsxChartSeriesRangeReader.HasSeriesRangeFormula(series, "cat");
                foreach (var formula in XlsxChartSeriesRangeReader.ReadSeriesRangeFormulas(series))
                {
                    if (XlsxChartSeriesRangeReader.TryParseFormulaRange(formula, sheetId, out var range))
                        ranges.Add(range);
                }

                if (usesSecondaryAxis && seriesIndex > 0)
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
                hasTitleRange |= XlsxChartSeriesRangeReader.HasSeriesRangeFormula(series, "tx");
                hasCategoryRange |= XlsxChartSeriesRangeReader.HasSeriesRangeFormula(series, "cat");
                foreach (var formula in XlsxChartSeriesRangeReader.ReadSeriesRangeFormulas(series))
                {
                    if (XlsxChartSeriesRangeReader.TryParseFormulaRange(formula, sheetId, out var range))
                        ranges.Add(range);
                }

                result.ComboLineSeriesIndexes.Add(seriesIndex);
                if (usesSecondaryAxis && seriesIndex > 0)
                    result.SecondaryAxisSeriesIndexes.Add(seriesIndex);

                if (XlsxChartSeriesFormatReader.TryReadSeriesLine(series, seriesIndex, out var format))
                    result.SeriesFormats.Add(format);

                XlsxChartDataLabelReader.ApplyPointDataLabels(series, seriesIndex, result);
                XlsxChartTrendlineErrorBarReader.ApplyTrendline(series, result);
                XlsxChartTrendlineErrorBarReader.ApplyErrorBars(series, result);
                fallbackSeriesIndex++;
            }
        }

        if (ranges.Count == 0)
        {
            chart = new ChartModel();
            return false;
        }

        result.SecondaryAxisSeriesIndexes = result.SecondaryAxisSeriesIndexes
            .Where(index => index > 0)
            .Distinct()
            .Order()
            .ToList();
        result.ComboLineSeriesIndexes = result.ComboLineSeriesIndexes
            .Where(index => index > 0)
            .Distinct()
            .Order()
            .ToList();
        result.ShowSecondaryAxis = result.SecondaryAxisSeriesIndexes.Count > 0;
        result.UseComboLineForSecondarySeries = result.ComboLineSeriesIndexes.Count > 0;
        result.DataRange = XlsxChartSeriesRangeReader.UnionRanges(ranges);
        result.FirstRowIsHeader = hasTitleRange;
        result.FirstColIsCategories = hasCategoryRange;
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
                hasTitleRange |= XlsxChartSeriesRangeReader.HasSeriesRangeFormula(series, "tx");
                hasCategoryRange |= XlsxChartSeriesRangeReader.HasSeriesRangeFormula(series, "cat");
                foreach (var formula in XlsxChartSeriesRangeReader.ReadSeriesRangeFormulas(series))
                {
                    if (XlsxChartSeriesRangeReader.TryParseFormulaRange(formula, sheetId, out var range))
                        ranges.Add(range);
                }

                if (usesSecondaryAxis && seriesIndex > 0)
                    result.SecondaryAxisSeriesIndexes.Add(seriesIndex);

                if (XlsxChartSeriesFormatReader.TryReadSeriesFill(series, seriesIndex, out var format))
                    result.SeriesFormats.Add(format);

                XlsxChartDataLabelReader.ApplyPointDataLabels(series, seriesIndex, result);
                XlsxChartTrendlineErrorBarReader.ApplyTrendline(series, result);
                XlsxChartTrendlineErrorBarReader.ApplyErrorBars(series, result);
                fallbackSeriesIndex++;
            }
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
        XlsxChartLevelReader.ApplyChartLevelProperties(chartXml, result);
        XlsxChartSanitizer.SanitizeLoadedChart(result);
        chart = result;
        return true;
    }
}
