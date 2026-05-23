using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

public static partial class XlsxChartPartReader
{
    private static bool TryReadBarLineComboChart(
        XDocument chartXml,
        XElement? plotArea,
        IReadOnlyList<XElement> barCharts,
        IReadOnlyList<XElement> lineCharts,
        SheetId sheetId,
        out ChartModel chart)
    {
        var firstBarChart = barCharts.FirstOrDefault();
        var barDirection = firstBarChart?.Element(ChartNs + "barDir")?.Attribute("val")?.Value;
        if (barDirection is not ("col" or "bar"))
        {
            chart = new ChartModel();
            return false;
        }

        var ranges = new List<GridRange>();
        var hasTitleRange = false;
        var hasCategoryRange = false;
        var result = new ChartModel
        {
            Type = ReadBarChartType(firstBarChart!, barDirection),
            Title = XlsxChartLevelReader.ReadTitle(chartXml),
            UseComboLineForSecondarySeries = true
        };
        ApplyBarChartMetadata(firstBarChart!, result);

        foreach (var barChart in barCharts)
        {
            if (barChart.Element(ChartNs + "barDir")?.Attribute("val")?.Value != barDirection)
            {
                chart = new ChartModel();
                return false;
            }

            var barUsesSecondaryAxis = XlsxChartSeriesRangeReader.UsesSecondaryValueAxis(plotArea, barChart);
            var fallbackSeriesIndex = 0;
            foreach (var series in barChart.Elements(ChartNs + "ser"))
            {
                var seriesIndex = XlsxChartSeriesRangeReader.ReadSeriesIndex(series, fallbackSeriesIndex);
                hasTitleRange |= XlsxChartSeriesRangeReader.HasSeriesRangeFormula(series, "tx");
                hasCategoryRange |= XlsxChartSeriesRangeReader.HasSeriesRangeFormula(series, "cat");
                foreach (var formula in XlsxChartSeriesRangeReader.ReadSeriesRangeFormulas(series))
                {
                    if (XlsxChartSeriesRangeReader.TryParseFormulaRange(formula, sheetId, out var range))
                        ranges.Add(range);
                }

                if (XlsxChartSeriesFormatReader.TryReadSeriesFill(series, seriesIndex, out var format))
                    result.SeriesFormats.Add(format);

                if (barUsesSecondaryAxis && seriesIndex > 0)
                    result.SecondaryAxisSeriesIndexes.Add(seriesIndex);

                XlsxChartDataLabelReader.ApplyPointDataLabels(series, seriesIndex, result);
                XlsxChartTrendlineErrorBarReader.ApplyTrendline(series, result);
                XlsxChartTrendlineErrorBarReader.ApplyErrorBars(series, result);
                fallbackSeriesIndex++;
            }
        }

        foreach (var lineChart in lineCharts)
        {
            XlsxChartTrendlineErrorBarReader.ApplyChartGuideLineMetadata(lineChart, result);
            var lineUsesSecondaryAxis = XlsxChartSeriesRangeReader.UsesSecondaryValueAxis(plotArea, lineChart);
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
                if (lineUsesSecondaryAxis && seriesIndex > 0)
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

    private static bool TryReadBarChart(
        XDocument chartXml,
        XElement? plotArea,
        IReadOnlyList<XElement> barCharts,
        SheetId sheetId,
        out ChartModel chart)
    {
        var firstBarChart = barCharts.FirstOrDefault();
        var barDirection = firstBarChart?.Element(ChartNs + "barDir")?.Attribute("val")?.Value;
        if (barDirection is not ("col" or "bar"))
        {
            chart = new ChartModel();
            return false;
        }

        var ranges = new List<GridRange>();
        var hasTitleRange = false;
        var hasCategoryRange = false;
        var result = new ChartModel
        {
            Type = ReadBarChartType(firstBarChart!, barDirection),
            Title = XlsxChartLevelReader.ReadTitle(chartXml)
        };
        ApplyBarChartMetadata(firstBarChart!, result);

        foreach (var barChart in barCharts)
        {
            if (barChart.Element(ChartNs + "barDir")?.Attribute("val")?.Value != barDirection)
            {
                chart = new ChartModel();
                return false;
            }

            var usesSecondaryAxis = XlsxChartSeriesRangeReader.UsesSecondaryValueAxis(plotArea, barChart);
            var fallbackSeriesIndex = 0;
            foreach (var series in barChart.Elements(ChartNs + "ser"))
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

        result.SecondaryAxisSeriesIndexes = result.SecondaryAxisSeriesIndexes
            .Distinct()
            .Order()
            .ToList();
        result.ShowSecondaryAxis = result.SecondaryAxisSeriesIndexes.Count > 0;
        result.DataRange = XlsxChartSeriesRangeReader.UnionRanges(ranges);
        result.FirstRowIsHeader = hasTitleRange;
        result.FirstColIsCategories = hasCategoryRange;
        XlsxChartLevelReader.ApplyChartLevelProperties(chartXml, result);
        XlsxChartSanitizer.SanitizeLoadedChart(result);
        chart = result;
        return true;
    }

    private static void ApplyBarChartMetadata(XElement barChart, ChartModel chart)
    {
        chart.BarGapWidth = XlsxChartScalarReader.ReadOptionalInt(barChart.Element(ChartNs + "gapWidth")?.Attribute("val")?.Value);
        chart.BarOverlap = XlsxChartScalarReader.ReadOptionalInt(barChart.Element(ChartNs + "overlap")?.Attribute("val")?.Value);
        chart.VaryColorsByPoint = XlsxChartScalarReader.ReadOptionalBool(barChart.Element(ChartNs + "varyColors")?.Attribute("val")?.Value);
    }

    private static ChartType ReadBarChartType(XElement barChart, string? barDirection)
    {
        var grouping = barChart.Element(ChartNs + "grouping")?.Attribute("val")?.Value;
        return (barDirection, grouping) switch
        {
            ("bar", "stacked") => ChartType.StackedBar,
            ("bar", "percentStacked") => ChartType.PercentStackedBar,
            ("bar", _) => ChartType.Bar,
            (_, "stacked") => ChartType.StackedColumn,
            (_, "percentStacked") => ChartType.PercentStackedColumn,
            _ => ChartType.Column
        };
    }
}
