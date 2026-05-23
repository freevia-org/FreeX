using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

public static partial class XlsxChartPartReader
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    public static bool TryReadSupportedChart(XDocument chartXml, SheetId sheetId, out ChartModel chart)
    {
        chart = new ChartModel();
        var plotArea = chartXml.Root?
            .Element(ChartNs + "chart")?
            .Element(ChartNs + "plotArea");
        var barCharts = plotArea?.Elements(ChartNs + "barChart").ToList() ?? [];
        var barChart = barCharts.FirstOrDefault();
        var lineCharts = plotArea?.Elements(ChartNs + "lineChart").ToList() ?? [];
        var lineChart = lineCharts.FirstOrDefault();
        var scatterCharts = plotArea?.Elements(ChartNs + "scatterChart").ToList() ?? [];
        var scatterChart = scatterCharts.FirstOrDefault();
        var areaCharts = plotArea?.Elements(ChartNs + "areaChart").ToList() ?? [];
        var areaChart = areaCharts.FirstOrDefault();
        var radarCharts = plotArea?.Elements(ChartNs + "radarChart").ToList() ?? [];
        var stockCharts = plotArea?.Elements(ChartNs + "stockChart").ToList() ?? [];
        var deferredAdvancedChart = HasDirectSupportedChart(plotArea) ? null : FindDeferredAdvancedChart(plotArea);
        var threeDColumnChart = plotArea?.Element(ChartNs + "bar3DChart");
        var bubbleChart = plotArea?.Element(ChartNs + "bubbleChart");
        var pieChart = plotArea?.Element(ChartNs + "pieChart");
        var doughnutChart = plotArea?.Element(ChartNs + "doughnutChart");
        bool read;
        if (doughnutChart is not null)
            read = TryReadPieFamilyChart(chartXml, doughnutChart, sheetId, ChartType.Doughnut, out chart);
        else if (pieChart is not null)
            read = TryReadPieFamilyChart(chartXml, pieChart, sheetId, ChartType.Pie, out chart);
        else if (bubbleChart is not null)
            read = TryReadBubbleChart(chartXml, bubbleChart, sheetId, out chart);
        else if (areaChart is not null && lineChart is not null)
            read = TryReadAreaLineComboChart(chartXml, plotArea, areaCharts, lineCharts, sheetId, out chart);
        else if (areaCharts.Count > 0)
            read = TryReadAreaChart(chartXml, plotArea, areaCharts, sheetId, out chart);
        else if (scatterCharts.Count > 0)
            read = TryReadScatterChart(chartXml, plotArea, scatterCharts, sheetId, out chart);
        else if (barChart is not null && lineChart is not null)
            read = TryReadBarLineComboChart(chartXml, plotArea, barCharts, lineCharts, sheetId, out chart);
        else if (lineCharts.Count > 1)
            read = TryReadLineChart(chartXml, plotArea, lineCharts, sheetId, out chart);
        else if (lineChart is not null)
            read = TryReadLineChart(chartXml, plotArea, [lineChart], sheetId, out chart);
        else if (radarCharts.Count > 0)
            read = TryReadLineLikeChart(chartXml, plotArea, radarCharts, sheetId, ChartType.Radar, out chart);
        else if (stockCharts.Count > 0)
            read = TryReadStockChart(chartXml, plotArea, stockCharts, barCharts, sheetId, out chart);
        else if (threeDColumnChart is not null)
            read = TryReadDeferredAdvancedChart(chartXml, threeDColumnChart, sheetId, ChartType.ThreeDColumn, out chart);
        else if (deferredAdvancedChart is { } advanced)
            read = TryReadDeferredAdvancedChart(chartXml, advanced.Element, sheetId, advanced.Type, out chart);
        else if (barChart is not null)
            read = TryReadBarChart(chartXml, plotArea, barCharts, sheetId, out chart);
        else
            return false;

        if (read)
        {
            XlsxChartMetadataReader.ApplyPackageMetadata(chartXml, chart);
            ApplyChartBehaviorMetadata(chartXml, chart);
            ApplyPivotSourceMetadata(chartXml, chart);
        }

        return read;
    }

    private static bool TryReadStockChart(
        XDocument chartXml,
        XElement? plotArea,
        IReadOnlyList<XElement> stockCharts,
        IReadOnlyList<XElement> barCharts,
        SheetId sheetId,
        out ChartModel chart)
    {
        if (!TryReadLineLikeChart(chartXml, plotArea, stockCharts, sheetId, ChartType.Stock, out chart))
            return false;

        var stockSeriesCount = stockCharts.Sum(plotChart => plotChart.Elements(ChartNs + "ser").Count());
        var volumeRanges = new List<GridRange>();
        foreach (var series in barCharts.SelectMany(plotChart => plotChart.Elements(ChartNs + "ser")))
        {
            foreach (var formula in XlsxChartSeriesRangeReader.ReadSeriesRangeFormulas(series))
            {
                if (XlsxChartSeriesRangeReader.TryParseFormulaRange(formula, sheetId, out var range))
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
                hasTitleRange |= XlsxChartSeriesRangeReader.HasSeriesRangeFormula(series, "tx");
                hasCategoryRange |= XlsxChartSeriesRangeReader.HasSeriesRangeFormula(series, "cat");
                foreach (var formula in XlsxChartSeriesRangeReader.ReadSeriesRangeFormulas(series))
                {
                    if (XlsxChartSeriesRangeReader.TryParseFormulaRange(formula, sheetId, out var range))
                        ranges.Add(range);
                }

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
        result.ShowSecondaryAxis = result.SecondaryAxisSeriesIndexes.Count > 0;
        result.DataRange = XlsxChartSeriesRangeReader.UnionRanges(ranges);
        result.FirstRowIsHeader = hasTitleRange;
        result.FirstColIsCategories = hasCategoryRange;
        XlsxChartLevelReader.ApplyChartLevelProperties(chartXml, result);
        XlsxChartSanitizer.SanitizeLoadedChart(result);
        chart = result;
        return true;
    }

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

    private static bool TryReadLineChart(
        XDocument chartXml,
        XElement? plotArea,
        IReadOnlyList<XElement> lineCharts,
        SheetId sheetId,
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
                hasTitleRange |= XlsxChartSeriesRangeReader.HasSeriesRangeFormula(series, "tx");
                hasCategoryRange |= XlsxChartSeriesRangeReader.HasSeriesRangeFormula(series, "cat");
                foreach (var formula in XlsxChartSeriesRangeReader.ReadSeriesRangeFormulas(series))
                {
                    if (XlsxChartSeriesRangeReader.TryParseFormulaRange(formula, sheetId, out var range))
                        ranges.Add(range);
                }

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
