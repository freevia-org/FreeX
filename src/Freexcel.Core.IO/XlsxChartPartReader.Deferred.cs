using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

public static partial class XlsxChartPartReader
{
    private static (XElement Element, ChartType Type)? FindDeferredAdvancedChart(XElement? plotArea)
    {
        if (plotArea is null)
            return null;

        foreach (var element in plotArea.Descendants())
        {
            var chartType = element.Name.LocalName switch
            {
                "surfaceChart" => ChartType.Surface,
                "surface3DChart" => ChartType.ThreeDSurface,
                "treemapChart" => ChartType.Treemap,
                "sunburstChart" => ChartType.Sunburst,
                "histogramChart" => XlsxChartSeriesRangeReader.HasDescendant(element, "paretoLine") ? ChartType.Pareto : ChartType.Histogram,
                "boxWhiskerChart" => ChartType.BoxAndWhisker,
                "waterfallChart" => ChartType.Waterfall,
                "funnelChart" => ChartType.Funnel,
                _ => (ChartType?)null
            };
            if (chartType is { } type)
                return (element, type);
        }

        var chartExSeries = plotArea
            .Descendants()
            .Where(element => element.Name.LocalName == "series")
            .ToList();
        if (chartExSeries.Count > 0)
        {
            var hasParetoLine = chartExSeries.Any(series =>
                string.Equals(series.Attribute("layoutId")?.Value, "paretoLine", StringComparison.OrdinalIgnoreCase));
            var primarySeries = chartExSeries.FirstOrDefault(series =>
                !string.Equals(series.Attribute("layoutId")?.Value, "paretoLine", StringComparison.OrdinalIgnoreCase));
            if (primarySeries is not null && ToChartExChartType(primarySeries.Attribute("layoutId")?.Value, hasParetoLine) is { } chartType)
                return (primarySeries.Parent ?? primarySeries, chartType);
        }

        var mapChart = plotArea
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName is "geoChart" or "mapChart" or "regionMapChart");
        return mapChart is null ? null : (mapChart, ChartType.Map);
    }

    private static ChartType? ToChartExChartType(string? layoutId, bool hasParetoLine) =>
        layoutId?.ToLowerInvariant() switch
        {
            "treemap" => ChartType.Treemap,
            "sunburst" => ChartType.Sunburst,
            "clusteredcolumn" => hasParetoLine ? ChartType.Pareto : ChartType.Histogram,
            "boxwhisker" => ChartType.BoxAndWhisker,
            "waterfall" => ChartType.Waterfall,
            "funnel" => ChartType.Funnel,
            _ => null
        };

    private static bool HasDirectSupportedChart(XElement? plotArea) =>
        plotArea?.Elements().Any(element => element.Name.LocalName is
            "areaChart" or
            "area3DChart" or
            "barChart" or
            "bubbleChart" or
            "doughnutChart" or
            "line3DChart" or
            "lineChart" or
            "ofPieChart" or
            "pie3DChart" or
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
            Title = XlsxChartLevelReader.ReadTitle(chartXml)
        };

        var chartExSeries = plotChart.Name.LocalName == "series"
            ? [plotChart]
            : plotChart.Descendants()
                .Where(element =>
                    element.Name.LocalName == "series" &&
                    !string.Equals(element.Attribute("layoutId")?.Value, "paretoLine", StringComparison.OrdinalIgnoreCase))
                .ToArray();
        var seriesElements = chartExSeries.Length > 0
            ? chartExSeries
            : plotChart.Descendants().Where(element => element.Name.LocalName == "ser");
        foreach (var series in seriesElements)
        {
            if (series.Name.LocalName == "series")
            {
                var data = FindChartExData(chartXml, series);
                hasTitleRange |= data?.Elements().Any(element =>
                    element.Name.LocalName == "numDim" &&
                    element.Elements().Any(child => child.Name.LocalName == "nf" && !string.IsNullOrWhiteSpace(child.Value))) == true;
                hasCategoryRange |= data?.Elements().Any(element =>
                    element.Name.LocalName == "strDim" &&
                    string.Equals(element.Attribute("type")?.Value, "cat", StringComparison.OrdinalIgnoreCase) &&
                    element.Elements().Any(child => child.Name.LocalName == "f" && !string.IsNullOrWhiteSpace(child.Value))) == true;
            }
            else
            {
                hasTitleRange |= XlsxChartSeriesRangeReader.HasSeriesRangeFormula(series, "tx");
                hasCategoryRange |= XlsxChartSeriesRangeReader.HasSeriesRangeFormula(series, "cat");
            }

            foreach (var formula in ReadDeferredAdvancedSeriesRangeFormulas(chartXml, series))
            {
                if (XlsxChartSeriesRangeReader.TryParseFormulaRange(formula, sheetId, out var range))
                    ranges.Add(range);
            }
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

    private static IEnumerable<string> ReadDeferredAdvancedSeriesRangeFormulas(XDocument chartXml, XElement series)
    {
        if (series.Name.LocalName != "series")
            return XlsxChartSeriesRangeReader.ReadSeriesRangeFormulas(series);

        var dataId = series
            .Elements()
            .FirstOrDefault(element => element.Name.LocalName == "dataId")?
            .Attribute("val")?
            .Value;
        if (string.IsNullOrWhiteSpace(dataId))
            return [];

        var data = FindChartExData(chartXml, dataId);
        return data is null
            ? []
            : data.Descendants()
                .Where(element => element.Name.LocalName is "f" or "nf")
                .Select(element => element.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value));
    }

    private static XElement? FindChartExData(XDocument chartXml, XElement series)
    {
        var dataId = series
            .Elements()
            .FirstOrDefault(element => element.Name.LocalName == "dataId")?
            .Attribute("val")?
            .Value;
        return string.IsNullOrWhiteSpace(dataId) ? null : FindChartExData(chartXml, dataId);
    }

    private static XElement? FindChartExData(XDocument chartXml, string dataId) =>
        chartXml.Root?
            .Descendants()
            .FirstOrDefault(element =>
                element.Name.LocalName == "data" &&
                string.Equals(element.Attribute("id")?.Value, dataId, StringComparison.Ordinal));

    private static bool TryReadThreeDBarChart(
        XDocument chartXml,
        XElement plotChart,
        SheetId sheetId,
        out ChartModel chart)
    {
        var barDirection = plotChart
            .Elements()
            .FirstOrDefault(element => element.Name.LocalName == "barDir")?
            .Attribute("val")?
            .Value;
        var chartType = string.Equals(barDirection, "bar", StringComparison.OrdinalIgnoreCase)
            ? ChartType.ThreeDBar
            : ChartType.ThreeDColumn;

        return TryReadDeferredAdvancedChart(chartXml, plotChart, sheetId, chartType, out chart);
    }
}
