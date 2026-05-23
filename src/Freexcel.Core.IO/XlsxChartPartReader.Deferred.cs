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
                "surface3DChart" => ChartType.Surface,
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
            Title = XlsxChartLevelReader.ReadTitle(chartXml)
        };

        foreach (var series in plotChart.Descendants().Where(element => element.Name.LocalName == "ser"))
        {
            hasTitleRange |= XlsxChartSeriesRangeReader.HasSeriesRangeFormula(series, "tx");
            hasCategoryRange |= XlsxChartSeriesRangeReader.HasSeriesRangeFormula(series, "cat");
            foreach (var formula in XlsxChartSeriesRangeReader.ReadSeriesRangeFormulas(series))
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
        XlsxChartSanitizer.SanitizeLoadedChart(result);
        chart = result;
        return true;
    }
}
