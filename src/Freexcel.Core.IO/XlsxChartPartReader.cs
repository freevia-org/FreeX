using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

public static class XlsxChartPartReader
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

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
            read = TryReadLineLikeChart(chartXml, plotArea, stockCharts, sheetId, ChartType.Stock, out chart);
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

    private static void ApplyChartBehaviorMetadata(XDocument chartXml, ChartModel chart)
    {
        var chartElement = chartXml.Root?.Element(ChartNs + "chart");
        chart.AutoTitleDeleted = XlsxChartScalarReader.IsTrue(chartElement?
            .Element(ChartNs + "autoTitleDeleted")?
            .Attribute("val")?
            .Value);

        chart.BlankDisplayMode = chartElement?
            .Element(ChartNs + "dispBlanksAs")?
            .Attribute("val")?
            .Value switch
            {
                "span" => ChartBlankDisplayMode.Span,
                "zero" => ChartBlankDisplayMode.Zero,
                _ => ChartBlankDisplayMode.Gap
            };

        chart.ShowDataLabelsOverMaximum = XlsxChartScalarReader.IsTrue(chartElement?
            .Element(ChartNs + "showDLblsOverMax")?
            .Attribute("val")?
            .Value);

        chart.ShowDataInHiddenRowsAndColumns = chartElement?
            .Element(ChartNs + "plotVisOnly")?
            .Attribute("val")?
            .Value is "0" or "false" or "False";
    }

    private static (XElement Element, ChartType Type)? FindDeferredAdvancedChart(XElement? plotArea)
    {
        if (plotArea is null)
            return null;

        foreach (var element in plotArea.Descendants())
        {
            var chartType = element.Name.LocalName switch
            {
                "surfaceChart" => ChartType.Surface,
                "surface3DChart" => ChartType.Surface,
                "treemapChart" => ChartType.Treemap,
                "sunburstChart" => ChartType.Sunburst,
                "histogramChart" => HasDescendant(element, "paretoLine") ? ChartType.Pareto : ChartType.Histogram,
                "boxWhiskerChart" => ChartType.BoxAndWhisker,
                "waterfallChart" => ChartType.Waterfall,
                "funnelChart" => ChartType.Funnel,
                _ => (ChartType?)null
            };
            if (chartType is { } type)
                return (element, type);
        }

        var mapChart = plotArea
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName is "geoChart" or "mapChart" or "regionMapChart");
        return mapChart is null ? null : (mapChart, ChartType.Map);
    }

    private static bool HasDirectSupportedChart(XElement? plotArea) =>
        plotArea?.Elements().Any(element => element.Name.LocalName is
            "areaChart" or
            "barChart" or
            "bubbleChart" or
            "doughnutChart" or
            "lineChart" or
            "ofPieChart" or
            "pieChart" or
            "radarChart" or
            "scatterChart" or
            "stockChart") == true;

    private static bool TryReadDeferredAdvancedChart(
        XDocument chartXml,
        XElement plotChart,
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
            Title = ReadTitle(chartXml)
        };

        foreach (var series in plotChart.Descendants().Where(element => element.Name.LocalName == "ser"))
        {
            hasTitleRange |= HasSeriesRangeFormula(series, "tx");
            hasCategoryRange |= HasSeriesRangeFormula(series, "cat");
            foreach (var formula in ReadSeriesRangeFormulas(series))
            {
                if (TryParseFormulaRange(formula, sheetId, out var range))
                    ranges.Add(range);
            }
        }

        if (ranges.Count == 0)
        {
            chart = new ChartModel();
            return false;
        }

        result.DataRange = UnionRanges(ranges);
        result.FirstRowIsHeader = hasTitleRange;
        result.FirstColIsCategories = hasCategoryRange;
        SanitizeLoadedChart(result);
        chart = result;
        return true;
    }

    private static void ApplyPivotSourceMetadata(XDocument chartXml, ChartModel chart)
    {
        chart.PivotFormatsXml = chartXml.Root?
            .Element(ChartNs + "chart")?
            .Element(ChartNs + "pivotFmts")?
            .ToString(SaveOptions.DisableFormatting);

        var pivotSourceName = chartXml.Root?
            .Element(ChartNs + "pivotSource")?
            .Element(ChartNs + "name")?
            .Value;
        if (string.IsNullOrWhiteSpace(pivotSourceName))
            return;

        chart.IsPivotChart = true;
        chart.PivotSourceSheetName = ExtractPivotSourceSheetName(pivotSourceName);
        chart.PivotTableName = ExtractPivotTableName(pivotSourceName);
    }

    private static string? ExtractPivotSourceSheetName(string pivotSourceName)
    {
        var bangIndex = pivotSourceName.LastIndexOf('!');
        if (bangIndex <= 0)
            return null;

        return UnquoteSheetName(pivotSourceName[..bangIndex].Trim());
    }

    private static string ExtractPivotTableName(string pivotSourceName)
    {
        var bangIndex = pivotSourceName.LastIndexOf('!');
        var name = bangIndex >= 0 ? pivotSourceName[(bangIndex + 1)..] : pivotSourceName;
        return name.Trim().Trim('\'');
    }

    private static string UnquoteSheetName(string value)
    {
        if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
            return value[1..^1].Replace("''", "'", StringComparison.Ordinal);

        return value;
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
            Title = ReadTitle(chartXml)
        };

        foreach (var plotChart in plotCharts)
        {
            XlsxChartTrendlineErrorBarReader.ApplyChartGuideLineMetadata(plotChart, result);
            var usesSecondaryAxis = UsesSecondaryValueAxis(plotArea, plotChart);
            var fallbackSeriesIndex = 0;
            foreach (var series in plotChart.Elements(ChartNs + "ser"))
            {
                var seriesIndex = ReadSeriesIndex(series, fallbackSeriesIndex);
                hasTitleRange |= HasSeriesRangeFormula(series, "tx");
                hasCategoryRange |= HasSeriesRangeFormula(series, "cat");
                foreach (var formula in ReadSeriesRangeFormulas(series))
                {
                    if (TryParseFormulaRange(formula, sheetId, out var range))
                        ranges.Add(range);
                }

                if (usesSecondaryAxis && seriesIndex > 0)
                    result.SecondaryAxisSeriesIndexes.Add(seriesIndex);

                if (TryReadSeriesLine(series, seriesIndex, out var format))
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
        result.DataRange = UnionRanges(ranges);
        result.FirstRowIsHeader = hasTitleRange;
        result.FirstColIsCategories = hasCategoryRange;
        ApplyChartLevelProperties(chartXml, result);
        SanitizeLoadedChart(result);
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
            Title = ReadTitle(chartXml),
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

            var barUsesSecondaryAxis = UsesSecondaryValueAxis(plotArea, barChart);
            var fallbackSeriesIndex = 0;
            foreach (var series in barChart.Elements(ChartNs + "ser"))
            {
                var seriesIndex = ReadSeriesIndex(series, fallbackSeriesIndex);
                hasTitleRange |= HasSeriesRangeFormula(series, "tx");
                hasCategoryRange |= HasSeriesRangeFormula(series, "cat");
                foreach (var formula in ReadSeriesRangeFormulas(series))
                {
                    if (TryParseFormulaRange(formula, sheetId, out var range))
                        ranges.Add(range);
                }

                if (TryReadSeriesFill(series, seriesIndex, out var format))
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
            var lineUsesSecondaryAxis = UsesSecondaryValueAxis(plotArea, lineChart);
            var fallbackSeriesIndex = 0;
            foreach (var series in lineChart.Elements(ChartNs + "ser"))
            {
                var seriesIndex = ReadSeriesIndex(series, fallbackSeriesIndex);
                hasTitleRange |= HasSeriesRangeFormula(series, "tx");
                hasCategoryRange |= HasSeriesRangeFormula(series, "cat");
                foreach (var formula in ReadSeriesRangeFormulas(series))
                {
                    if (TryParseFormulaRange(formula, sheetId, out var range))
                        ranges.Add(range);
                }

                result.ComboLineSeriesIndexes.Add(seriesIndex);
                if (lineUsesSecondaryAxis && seriesIndex > 0)
                    result.SecondaryAxisSeriesIndexes.Add(seriesIndex);

                if (TryReadSeriesLine(series, seriesIndex, out var format))
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
        result.DataRange = UnionRanges(ranges);
        result.FirstRowIsHeader = hasTitleRange;
        result.FirstColIsCategories = hasCategoryRange;
        ApplyChartLevelProperties(chartXml, result);
        SanitizeLoadedChart(result);
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
            Title = ReadTitle(chartXml)
        };
        ApplyBarChartMetadata(firstBarChart!, result);

        foreach (var barChart in barCharts)
        {
            if (barChart.Element(ChartNs + "barDir")?.Attribute("val")?.Value != barDirection)
            {
                chart = new ChartModel();
                return false;
            }

            var usesSecondaryAxis = UsesSecondaryValueAxis(plotArea, barChart);
            var fallbackSeriesIndex = 0;
            foreach (var series in barChart.Elements(ChartNs + "ser"))
            {
                var seriesIndex = ReadSeriesIndex(series, fallbackSeriesIndex);
                hasTitleRange |= HasSeriesRangeFormula(series, "tx");
                hasCategoryRange |= HasSeriesRangeFormula(series, "cat");
                foreach (var formula in ReadSeriesRangeFormulas(series))
                {
                    if (TryParseFormulaRange(formula, sheetId, out var range))
                        ranges.Add(range);
                }

                if (usesSecondaryAxis && seriesIndex > 0)
                    result.SecondaryAxisSeriesIndexes.Add(seriesIndex);

                if (TryReadSeriesFill(series, seriesIndex, out var format))
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
        result.DataRange = UnionRanges(ranges);
        result.FirstRowIsHeader = hasTitleRange;
        result.FirstColIsCategories = hasCategoryRange;
        ApplyChartLevelProperties(chartXml, result);
        SanitizeLoadedChart(result);
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
            Title = ReadTitle(chartXml)
        };

        foreach (var lineChart in lineCharts)
        {
            XlsxChartTrendlineErrorBarReader.ApplyChartGuideLineMetadata(lineChart, result);
            var usesSecondaryAxis = UsesSecondaryValueAxis(plotArea, lineChart);
            var fallbackSeriesIndex = 0;
            foreach (var series in lineChart.Elements(ChartNs + "ser"))
            {
                var seriesIndex = ReadSeriesIndex(series, fallbackSeriesIndex);
                hasTitleRange |= HasSeriesRangeFormula(series, "tx");
                hasCategoryRange |= HasSeriesRangeFormula(series, "cat");
                foreach (var formula in ReadSeriesRangeFormulas(series))
                {
                    if (TryParseFormulaRange(formula, sheetId, out var range))
                        ranges.Add(range);
                }

                if (usesSecondaryAxis && seriesIndex > 0)
                    result.SecondaryAxisSeriesIndexes.Add(seriesIndex);

                if (TryReadSeriesLine(series, seriesIndex, out var format))
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

        result.DataRange = UnionRanges(ranges);
        result.SecondaryAxisSeriesIndexes = result.SecondaryAxisSeriesIndexes
            .Distinct()
            .Order()
            .ToList();
        result.ShowSecondaryAxis = result.SecondaryAxisSeriesIndexes.Count > 0;
        result.FirstRowIsHeader = hasTitleRange;
        result.FirstColIsCategories = hasCategoryRange;
        ApplyChartLevelProperties(chartXml, result);
        SanitizeLoadedChart(result);
        chart = result;
        return true;
    }

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
            Title = ReadTitle(chartXml)
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
            hasTitleRange |= HasSeriesRangeFormula(series, "tx");
            hasCategoryRange |= HasSeriesRangeFormula(series, "cat");
            foreach (var formula in ReadSeriesRangeFormulas(series))
            {
                if (TryParseFormulaRange(formula, sheetId, out var range))
                    ranges.Add(range);
            }

            var modelSeriesIndex = ReadSeriesIndex(series, seriesIndex);
            if (TryReadSeriesFill(series, modelSeriesIndex, out var format))
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

        result.DataRange = UnionRanges(ranges);
        result.FirstRowIsHeader = hasTitleRange;
        result.FirstColIsCategories = hasCategoryRange;
        ApplyChartLevelProperties(chartXml, result);
        SanitizeLoadedChart(result);
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
            Title = ReadTitle(chartXml),
            FirstColIsCategories = false
        };

        var seriesIndex = 0;
        foreach (var series in bubbleChart.Elements(ChartNs + "ser"))
        {
            var modelSeriesIndex = ReadSeriesIndex(series, seriesIndex);
            hasTitleRange |= HasSeriesRangeFormula(series, "tx");
            foreach (var formula in ReadSeriesRangeFormulas(series, "tx", "xVal", "yVal", "bubbleSize"))
            {
                if (TryParseFormulaRange(formula, sheetId, out var range))
                    ranges.Add(range);
            }

            if (TryReadSeriesFill(series, modelSeriesIndex, out var format))
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

        result.DataRange = UnionRanges(ranges);
        result.FirstRowIsHeader = hasTitleRange;
        ApplyChartLevelProperties(chartXml, result);
        SanitizeLoadedChart(result);
        chart = result;
        return true;
    }

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
            Title = ReadTitle(chartXml)
        };

        foreach (var areaChart in areaCharts)
        {
            var usesSecondaryAxis = UsesSecondaryValueAxis(plotArea, areaChart);
            var fallbackSeriesIndex = 0;
            foreach (var series in areaChart.Elements(ChartNs + "ser"))
            {
                var seriesIndex = ReadSeriesIndex(series, fallbackSeriesIndex);
                hasTitleRange |= HasSeriesRangeFormula(series, "tx");
                hasCategoryRange |= HasSeriesRangeFormula(series, "cat");
                foreach (var formula in ReadSeriesRangeFormulas(series))
                {
                    if (TryParseFormulaRange(formula, sheetId, out var range))
                        ranges.Add(range);
                }

                if (usesSecondaryAxis && seriesIndex > 0)
                    result.SecondaryAxisSeriesIndexes.Add(seriesIndex);

                if (TryReadSeriesFill(series, seriesIndex, out var format))
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
            var usesSecondaryAxis = UsesSecondaryValueAxis(plotArea, lineChart);
            var fallbackSeriesIndex = 0;
            foreach (var series in lineChart.Elements(ChartNs + "ser"))
            {
                var seriesIndex = ReadSeriesIndex(series, fallbackSeriesIndex);
                hasTitleRange |= HasSeriesRangeFormula(series, "tx");
                hasCategoryRange |= HasSeriesRangeFormula(series, "cat");
                foreach (var formula in ReadSeriesRangeFormulas(series))
                {
                    if (TryParseFormulaRange(formula, sheetId, out var range))
                        ranges.Add(range);
                }

                result.ComboLineSeriesIndexes.Add(seriesIndex);
                if (usesSecondaryAxis && seriesIndex > 0)
                    result.SecondaryAxisSeriesIndexes.Add(seriesIndex);

                if (TryReadSeriesLine(series, seriesIndex, out var format))
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
        result.DataRange = UnionRanges(ranges);
        result.FirstRowIsHeader = hasTitleRange;
        result.FirstColIsCategories = hasCategoryRange;
        ApplyChartLevelProperties(chartXml, result);
        SanitizeLoadedChart(result);
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
            Title = ReadTitle(chartXml)
        };

        foreach (var areaChart in areaCharts)
        {
            var usesSecondaryAxis = UsesSecondaryValueAxis(plotArea, areaChart);
            var fallbackSeriesIndex = 0;
            foreach (var series in areaChart.Elements(ChartNs + "ser"))
            {
                var seriesIndex = ReadSeriesIndex(series, fallbackSeriesIndex);
                hasTitleRange |= HasSeriesRangeFormula(series, "tx");
                hasCategoryRange |= HasSeriesRangeFormula(series, "cat");
                foreach (var formula in ReadSeriesRangeFormulas(series))
                {
                    if (TryParseFormulaRange(formula, sheetId, out var range))
                        ranges.Add(range);
                }

                if (usesSecondaryAxis && seriesIndex > 0)
                    result.SecondaryAxisSeriesIndexes.Add(seriesIndex);

                if (TryReadSeriesFill(series, seriesIndex, out var format))
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

        result.DataRange = UnionRanges(ranges);
        result.SecondaryAxisSeriesIndexes = result.SecondaryAxisSeriesIndexes
            .Distinct()
            .Order()
            .ToList();
        result.ShowSecondaryAxis = result.SecondaryAxisSeriesIndexes.Count > 0;
        result.FirstRowIsHeader = hasTitleRange;
        result.FirstColIsCategories = hasCategoryRange;
        ApplyChartLevelProperties(chartXml, result);
        SanitizeLoadedChart(result);
        chart = result;
        return true;
    }

    private static bool TryReadScatterChart(
        XDocument chartXml,
        XElement? plotArea,
        IReadOnlyList<XElement> scatterCharts,
        SheetId sheetId,
        out ChartModel chart)
    {
        var ranges = new List<GridRange>();
        var hasTitleRange = false;
        var result = new ChartModel
        {
            Type = ChartType.Scatter,
            Title = ReadTitle(chartXml),
            FirstColIsCategories = false
        };

        foreach (var scatterChart in scatterCharts)
        {
            var usesSecondaryAxis = UsesSecondaryValueAxis(plotArea, scatterChart);
            var fallbackSeriesIndex = 0;
            foreach (var series in scatterChart.Elements(ChartNs + "ser"))
            {
                var modelSeriesIndex = ReadSeriesIndex(series, fallbackSeriesIndex);
                hasTitleRange |= HasSeriesRangeFormula(series, "tx");
                foreach (var formula in ReadSeriesRangeFormulas(series, "tx", "xVal", "yVal"))
                {
                    if (TryParseFormulaRange(formula, sheetId, out var range))
                        ranges.Add(range);
                }

                if (usesSecondaryAxis && modelSeriesIndex > 0)
                    result.SecondaryAxisSeriesIndexes.Add(modelSeriesIndex);

                if (TryReadSeriesLine(series, modelSeriesIndex, out var format))
                    result.SeriesFormats.Add(format);

                XlsxChartDataLabelReader.ApplyPointDataLabels(series, modelSeriesIndex, result);
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

        result.DataRange = UnionRanges(ranges);
        result.SecondaryAxisSeriesIndexes = result.SecondaryAxisSeriesIndexes
            .Distinct()
            .Order()
            .ToList();
        result.ShowSecondaryAxis = result.SecondaryAxisSeriesIndexes.Count > 0;
        result.FirstRowIsHeader = hasTitleRange;
        ApplyChartLevelProperties(chartXml, result);
        SanitizeLoadedChart(result);
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

    private static string? ReadTitle(XDocument chartXml)
    {
        var title = chartXml.Root?
            .Element(ChartNs + "chart")?
            .Element(ChartNs + "title");

        return title?
            .Descendants(DrawingNs + "t")
            .Select(element => element.Value)
            .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));
    }

    private static void ApplyChartLevelProperties(XDocument chartXml, ChartModel chart)
    {
        var chartElement = chartXml.Root?.Element(ChartNs + "chart");
        XlsxChartFormattingReader.ApplyChartTitleFormatting(chartElement?.Element(ChartNs + "title"), chart);
        XlsxChartFormattingReader.ApplyChartAreaShapeProperties(chartXml.Root?.Element(ChartNs + "spPr"), chart);
        var plotArea = chartElement?.Element(ChartNs + "plotArea");
        chart.PlotAreaLayout = XlsxChartMetadataReader.ReadManualLayout(plotArea?.Element(ChartNs + "layout"));
        chart.DataTable = ReadChartDataTable(plotArea?.Element(ChartNs + "dTable"));
        XlsxChartFormattingReader.ApplyPlotAreaShapeProperties(plotArea?.Element(ChartNs + "spPr"), chart);
        XlsxChartAxisReader.ApplyAxisMetadata(plotArea, chart);
        XlsxChartDataLabelReader.ApplyDataLabels(plotArea, chart);

        var legend = chartElement?.Element(ChartNs + "legend");
        if (legend is null)
        {
            chart.ShowLegend = false;
            chart.LegendPosition = ChartLegendPosition.None;
            chart.LegendOverlay = false;
            return;
        }

        chart.ShowLegend = true;
        chart.LegendLayout = XlsxChartMetadataReader.ReadManualLayout(legend.Element(ChartNs + "layout"));
        chart.LegendPosition = legend.Element(ChartNs + "legendPos")?.Attribute("val")?.Value switch
        {
            "l" => ChartLegendPosition.Left,
            "t" => ChartLegendPosition.Top,
            "b" => ChartLegendPosition.Bottom,
            "r" => ChartLegendPosition.Right,
            _ => ChartLegendPosition.Right
        };
        chart.LegendOverlay = XlsxChartScalarReader.IsTrue(legend.Element(ChartNs + "overlay")?.Attribute("val")?.Value);
        ApplyLegendFormatting(legend, chart);
    }

    private static ChartDataTableModel? ReadChartDataTable(XElement? dataTable)
    {
        if (dataTable is null)
            return null;

        return new ChartDataTableModel
        {
            ShowHorizontalBorder = XlsxChartScalarReader.ReadOptionalBool(dataTable.Element(ChartNs + "showHorzBorder")?.Attribute("val")?.Value),
            ShowVerticalBorder = XlsxChartScalarReader.ReadOptionalBool(dataTable.Element(ChartNs + "showVertBorder")?.Attribute("val")?.Value),
            ShowOutline = XlsxChartScalarReader.ReadOptionalBool(dataTable.Element(ChartNs + "showOutline")?.Attribute("val")?.Value),
            ShowLegendKeys = XlsxChartScalarReader.ReadOptionalBool(dataTable.Element(ChartNs + "showKeys")?.Attribute("val")?.Value)
        };
    }

    private static void ApplyLegendFormatting(XElement legend, ChartModel chart)
    {
        var shapeProperties = legend.Element(ChartNs + "spPr");
        var fill = shapeProperties?.Element(DrawingNs + "solidFill");
        if (fill is not null)
        {
            if (XlsxDrawingColorReader.TryReadThemeColorReference(fill, DrawingNs, out var fillThemeColor))
            {
                chart.LegendFillThemeColor = fillThemeColor;
                chart.LegendFillColor = null;
            }
            else if (XlsxDrawingColorReader.TryReadConcreteColor(fill, DrawingNs, out var fillColor))
            {
                chart.LegendFillColor = fillColor;
                chart.LegendFillThemeColor = null;
            }
        }

        var line = shapeProperties?.Element(DrawingNs + "ln");
        if (line is not null)
        {
            if (int.TryParse(line.Attribute("w")?.Value, out var emus))
                chart.LegendBorderThickness = Math.Clamp(emus / 12700.0, 0, 10);

            var lineFill = line.Element(DrawingNs + "solidFill");
            if (lineFill is not null)
            {
                if (XlsxDrawingColorReader.TryReadThemeColorReference(lineFill, DrawingNs, out var borderThemeColor))
                {
                    chart.LegendBorderThemeColor = borderThemeColor;
                    chart.LegendBorderColor = null;
                }
                else if (XlsxDrawingColorReader.TryReadConcreteColor(lineFill, DrawingNs, out var borderColor))
                {
                    chart.LegendBorderColor = borderColor;
                    chart.LegendBorderThemeColor = null;
                }
            }
        }

        var textProperties = legend
            .Element(ChartNs + "txPr")?
            .Descendants(DrawingNs + "defRPr")
            .FirstOrDefault();
        if (textProperties is null)
            return;

        if (int.TryParse(textProperties.Attribute("sz")?.Value, out var size))
            chart.LegendFontSize = Math.Clamp(size / 100.0, 6, 72);

        var textFill = textProperties.Element(DrawingNs + "solidFill");
        if (textFill is not null && XlsxDrawingColorReader.TryReadThemeColorReference(textFill, DrawingNs, out var textThemeColor))
        {
            chart.LegendTextThemeColor = textThemeColor;
            chart.LegendTextColor = null;
        }
        else if (textFill is not null && XlsxDrawingColorReader.TryReadConcreteColor(textFill, DrawingNs, out var textColor))
        {
            chart.LegendTextColor = textColor;
            chart.LegendTextThemeColor = null;
        }
    }

    private static void SanitizeLoadedChart(ChartModel chart)
    {
        var seriesCount = ChartTypeSupport.GetDataSeriesCount(chart);
        chart.SecondaryAxisSeriesIndexes = chart.SecondaryAxisSeriesIndexes
            .Where(index => index > 0 && index < seriesCount)
            .Distinct()
            .Order()
            .ToList();

        if (!ChartTypeSupport.SupportsSecondaryAxis(chart.Type)
            || (chart.ShowSecondaryAxis && chart.SecondaryAxisSeriesIndexes.Count == 0))
        {
            chart.ShowSecondaryAxis = false;
            chart.SecondaryAxisSeriesIndexes = [];
        }

        chart.ComboLineSeriesIndexes = chart.ComboLineSeriesIndexes
            .Where(index => index > 0 && index < seriesCount)
            .Distinct()
            .Order()
            .ToList();

        if (!ChartTypeSupport.SupportsComboLineOverlay(chart)
            || (chart.UseComboLineForSecondarySeries && chart.ComboLineSeriesIndexes.Count == 0))
        {
            chart.UseComboLineForSecondarySeries = false;
            chart.ComboLineSeriesIndexes = [];
        }

        if (!ChartTypeSupport.SupportsTrendlines(chart.Type))
        {
            chart.ShowLinearTrendline = false;
            chart.TrendlineType = ChartTrendlineType.Linear;
            chart.TrendlinePeriod = 2;
            chart.TrendlineOrder = 2;
            chart.ShowTrendlineEquation = false;
            chart.ShowTrendlineRSquared = false;
            chart.TrendlineColor = null;
            chart.TrendlineThemeColor = null;
            chart.TrendlineThickness = 1.5;
            chart.TrendlineDashStyle = ChartLineDashStyle.Dash;
        }

        var dataPointCount = ChartTypeSupport.GetDataPointCount(chart);
        if (chart.ExplodedSliceIndex < 0 || chart.ExplodedSliceIndex >= dataPointCount)
        {
            chart.ExplodedSliceIndex = -1;
            chart.ExplodedSliceDistance = 0.1;
        }
        chart.SeriesFormats = chart.SeriesFormats
            .Where(format => format.SeriesIndex >= 0)
            .GroupBy(format => format.SeriesIndex)
            .Select(group => group.Last())
            .OrderBy(format => format.SeriesIndex)
            .ToList();
        chart.PointDataLabelFormats = chart.PointDataLabelFormats
            .Where(format => format.SeriesIndex >= 0 && format.PointIndex >= 0 && format.PointIndex < dataPointCount)
            .GroupBy(format => (format.SeriesIndex, format.PointIndex))
            .Select(group => group.Last())
            .OrderBy(format => format.SeriesIndex)
            .ThenBy(format => format.PointIndex)
            .ToList();
        if (!ChartTypeSupport.SupportsAxes(chart.Type))
        {
            chart.XAxisTitle = null;
            chart.YAxisTitle = null;
            chart.AxisTitleTextColor = null;
            chart.AxisTitleFontSize = 12;
            ClearXAxisBounds(chart);
            ClearYAxisBounds(chart);
            return;
        }

        if (!ChartTypeSupport.SupportsXAxisBounds(chart.Type))
            ClearXAxisValueBounds(chart);
        if (!ChartTypeSupport.SupportsYAxisBounds(chart.Type))
            ClearYAxisValueBounds(chart);
    }

    private static void ClearXAxisValueBounds(ChartModel chart)
    {
        chart.XAxisMinimum = null;
        chart.XAxisMaximum = null;
        chart.XAxisMajorUnit = null;
        chart.XAxisMinorUnit = null;
        chart.XAxisLogScale = false;
        chart.XAxisNumberFormat = ChartDataLabelNumberFormat.General;
    }

    private static void ClearYAxisValueBounds(ChartModel chart)
    {
        chart.YAxisMinimum = null;
        chart.YAxisMaximum = null;
        chart.YAxisMajorUnit = null;
        chart.YAxisMinorUnit = null;
        chart.YAxisLogScale = false;
        chart.YAxisNumberFormat = ChartDataLabelNumberFormat.General;
    }

    private static void ClearXAxisBounds(ChartModel chart)
    {
        ClearXAxisValueBounds(chart);
        chart.ShowXAxisMajorGridlines = false;
        chart.ShowXAxisMinorGridlines = false;
        chart.XAxisMajorGridlineColor = null;
        chart.XAxisMinorGridlineColor = null;
        chart.XAxisGridlineThickness = 1;
        chart.XAxisMajorTickStyle = ChartAxisTickStyle.Outside;
        chart.XAxisMinorTickStyle = ChartAxisTickStyle.None;
        chart.ShowXAxisLabels = true;
        chart.XAxisLabelTextColor = null;
        chart.XAxisLabelFontSize = 11;
        chart.XAxisLabelAngle = 0;
        chart.XAxisLineColor = null;
        chart.XAxisLineThickness = 1;
    }

    private static void ClearYAxisBounds(ChartModel chart)
    {
        ClearYAxisValueBounds(chart);
        chart.ShowYAxisMajorGridlines = false;
        chart.ShowYAxisMinorGridlines = false;
        chart.YAxisMajorGridlineColor = null;
        chart.YAxisMinorGridlineColor = null;
        chart.YAxisGridlineThickness = 1;
        chart.YAxisMajorTickStyle = ChartAxisTickStyle.Outside;
        chart.YAxisMinorTickStyle = ChartAxisTickStyle.None;
        chart.ShowYAxisLabels = true;
        chart.YAxisLabelTextColor = null;
        chart.YAxisLabelFontSize = 11;
        chart.YAxisLabelAngle = 0;
        chart.YAxisLineColor = null;
        chart.YAxisLineThickness = 1;
    }

    private static int ReadSeriesIndex(XElement series, int fallback) =>
        int.TryParse(ElementByLocalName(series, "idx")?.Attribute("val")?.Value, out var index)
            ? index
            : fallback;

    private static bool UsesSecondaryValueAxis(XElement? plotArea, XElement plotChart)
    {
        if (plotArea is null)
            return false;

        var secondaryAxisIds = plotArea
            .Elements(ChartNs + "valAx")
            .Where(axis => axis.Element(ChartNs + "axPos")?.Attribute("val")?.Value == "r")
            .Select(axis => axis.Element(ChartNs + "axId")?.Attribute("val")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.Ordinal);
        if (secondaryAxisIds.Count == 0)
            return false;

        return plotChart
            .Elements(ChartNs + "axId")
            .Select(axis => axis.Attribute("val")?.Value)
            .Any(value => value is not null && secondaryAxisIds.Contains(value));
    }

    private static IEnumerable<string> ReadSeriesRangeFormulas(XElement series) =>
        ReadSeriesRangeFormulas(series, "tx", "cat", "val");

    private static bool HasSeriesRangeFormula(XElement series, string containerName) =>
        ElementByLocalName(series, containerName)?
            .Descendants()
            .Where(element => element.Name.LocalName == "f")
            .Any(element => !string.IsNullOrWhiteSpace(element.Value)) == true;

    private static IEnumerable<string> ReadSeriesRangeFormulas(XElement series, params string[] containerNames)
    {
        foreach (var containerName in containerNames)
        {
            foreach (var formula in series
                         .Elements()
                         .FirstOrDefault(element => element.Name.LocalName == containerName)?
                         .Descendants()
                         .Where(element => element.Name.LocalName == "f")
                         .Select(element => element.Value)
                         .Where(text => !string.IsNullOrWhiteSpace(text))
                     ?? [])
            {
                yield return formula;
            }
        }
    }

    private static XElement? ElementByLocalName(XElement element, string localName) =>
        element.Elements().FirstOrDefault(child => child.Name.LocalName == localName);

    private static bool HasDescendant(XElement element, string localName) =>
        element.Descendants().Any(descendant => descendant.Name.LocalName == localName);

    private static bool TryReadSeriesFill(XElement series, int seriesIndex, out ChartSeriesFormat format)
    {
        format = default!;
        var shapeProperties = series.Element(ChartNs + "spPr");
        var solidFill = shapeProperties?.Element(DrawingNs + "solidFill");

        CellColor? fillColor = null;
        WorkbookThemeColorReference? fillThemeColor = null;
        if (solidFill is not null && XlsxDrawingColorReader.TryReadThemeColorReference(solidFill, DrawingNs, out var themeColor))
            fillThemeColor = themeColor;
        else if (solidFill is not null && XlsxDrawingColorReader.TryReadConcreteColor(solidFill, DrawingNs, out var color))
            fillColor = color;

        var line = shapeProperties?.Element(DrawingNs + "ln");
        var lineFill = line?.Element(DrawingNs + "solidFill");
        CellColor? strokeColor = null;
        WorkbookThemeColorReference? strokeThemeColor = null;
        if (lineFill is not null && XlsxDrawingColorReader.TryReadThemeColorReference(lineFill, DrawingNs, out var lineThemeColor))
            strokeThemeColor = lineThemeColor;
        else if (lineFill is not null && XlsxDrawingColorReader.TryReadConcreteColor(lineFill, DrawingNs, out var lineColor))
            strokeColor = lineColor;

        double? strokeThickness = null;
        if (int.TryParse(line?.Attribute("w")?.Value, out var emus))
            strokeThickness = Math.Clamp(emus / 12700.0, 0.5, 10);

        ChartLineDashStyle? dashStyle = line?.Element(DrawingNs + "prstDash") is { } dashElement
            ? XlsxChartTrendlineErrorBarReader.FromXlsxPresetDash(dashElement.Attribute("val")?.Value)
            : null;

        if (fillColor is null &&
            fillThemeColor is null &&
            strokeColor is null &&
            strokeThemeColor is null &&
            strokeThickness is null &&
            dashStyle is null)
        {
            return false;
        }

        format = new ChartSeriesFormat(
            seriesIndex,
            FillColor: fillColor,
            StrokeColor: strokeColor,
            StrokeThickness: strokeThickness,
            DashStyle: dashStyle,
            FillThemeColor: fillThemeColor,
            StrokeThemeColor: strokeThemeColor);
        return true;
    }

    private static bool TryReadSeriesLine(XElement series, int seriesIndex, out ChartSeriesFormat format)
    {
        format = default!;
        var line = series
            .Element(ChartNs + "spPr")?
            .Element(DrawingNs + "ln");

        CellColor? strokeColor = null;
        WorkbookThemeColorReference? strokeThemeColor = null;
        var solidFill = line?.Element(DrawingNs + "solidFill");
        if (solidFill is not null && XlsxDrawingColorReader.TryReadThemeColorReference(solidFill, DrawingNs, out var themeColor))
            strokeThemeColor = themeColor;
        else if (solidFill is not null && XlsxDrawingColorReader.TryReadConcreteColor(solidFill, DrawingNs, out var color))
            strokeColor = color;

        double? strokeThickness = null;
        if (int.TryParse(line?.Attribute("w")?.Value, out var emus))
            strokeThickness = Math.Clamp(emus / 12700.0, 0.5, 10);

        ChartLineDashStyle? dashStyle = line?.Element(DrawingNs + "prstDash") is { } dashElement
            ? XlsxChartTrendlineErrorBarReader.FromXlsxPresetDash(dashElement.Attribute("val")?.Value)
            : null;

        var marker = series.Element(ChartNs + "marker");
        var markerStyle = marker?.Element(ChartNs + "symbol") is { } symbolElement
            ? FromXlsxMarkerStyle(symbolElement.Attribute("val")?.Value)
            : (ChartMarkerStyle?)null;
        double? markerSize = null;
        if (int.TryParse(marker?.Element(ChartNs + "size")?.Attribute("val")?.Value, out var size))
            markerSize = Math.Clamp(size, 1, 30);
        CellColor? fillColor = null;
        WorkbookThemeColorReference? fillThemeColor = null;
        var markerFill = marker?
            .Element(ChartNs + "spPr")?
            .Element(DrawingNs + "solidFill");
        if (markerFill is not null && XlsxDrawingColorReader.TryReadThemeColorReference(markerFill, DrawingNs, out var markerThemeColor))
            fillThemeColor = markerThemeColor;
        else if (markerFill is not null && XlsxDrawingColorReader.TryReadConcreteColor(markerFill, DrawingNs, out var markerColor))
            fillColor = markerColor;

        if (strokeColor is null &&
            strokeThemeColor is null &&
            strokeThickness is null &&
            dashStyle is null &&
            fillColor is null &&
            fillThemeColor is null &&
            markerStyle is null &&
            markerSize is null)
        {
            return false;
        }

        format = new ChartSeriesFormat(
            seriesIndex,
            FillColor: fillColor,
            StrokeColor: strokeColor,
            StrokeThickness: strokeThickness,
            DashStyle: dashStyle,
            MarkerStyle: markerStyle,
            MarkerSize: markerSize,
            FillThemeColor: fillThemeColor,
            StrokeThemeColor: strokeThemeColor);
        return true;
    }

    private static ChartMarkerStyle FromXlsxMarkerStyle(string? value) =>
        value switch
        {
            "none" => ChartMarkerStyle.None,
            "square" => ChartMarkerStyle.Square,
            "diamond" => ChartMarkerStyle.Diamond,
            "triangle" => ChartMarkerStyle.Triangle,
            _ => ChartMarkerStyle.Circle
        };

    private static bool TryParseFormulaRange(string formula, SheetId sheetId, out GridRange range)
    {
        range = default;
        var local = formula.Trim();
        var bang = local.LastIndexOf('!');
        if (bang >= 0)
            local = local[(bang + 1)..];

        local = local.Replace("$", "", StringComparison.Ordinal).Trim('\'');
        var parts = local.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 1)
        {
            if (!CellAddress.TryParse(parts[0], sheetId, out var address))
                return false;

            range = new GridRange(address, address);
            return true;
        }

        if (parts.Length != 2 ||
            !CellAddress.TryParse(parts[0], sheetId, out var start) ||
            !CellAddress.TryParse(parts[1], sheetId, out var end))
        {
            return false;
        }

        range = new GridRange(start, end);
        return true;
    }

    private static GridRange UnionRanges(IReadOnlyList<GridRange> ranges)
    {
        var sheetId = ranges[0].Start.Sheet;
        var minRow = ranges.Min(range => range.Start.Row);
        var minCol = ranges.Min(range => range.Start.Col);
        var maxRow = ranges.Max(range => range.End.Row);
        var maxCol = ranges.Max(range => range.End.Col);
        return new GridRange(
            new CellAddress(sheetId, minRow, minCol),
            new CellAddress(sheetId, maxRow, maxCol));
    }
}
