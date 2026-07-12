using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

public static partial class XlsxChartPartReader
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    public static bool TryReadSupportedChart(XDocument chartXml, SheetId sheetId, out ChartModel chart) =>
        TryReadSupportedChart(chartXml, sheetId, fallbackDataRange: null, sheetNameResolver: null, out chart);

    public static bool TryReadSupportedChart(
        XDocument chartXml,
        SheetId sheetId,
        GridRange? fallbackDataRange,
        out ChartModel chart) =>
        TryReadSupportedChart(chartXml, sheetId, fallbackDataRange, sheetNameResolver: null, out chart);

    public static bool TryReadSupportedChart(
        XDocument chartXml,
        SheetId sheetId,
        GridRange? fallbackDataRange,
        IReadOnlyDictionary<string, SheetId>? sheetNameResolver,
        out ChartModel chart)
    {
        chart = new ChartModel();
        var plotArea = FindPlotArea(chartXml);
        var barCharts = FindChartElements(plotArea, "barChart");
        var barChart = FirstChartElement(barCharts);
        var lineCharts = FindChartElements(plotArea, "lineChart");
        var lineChart = FirstChartElement(lineCharts);
        var threeDLineCharts = FindChartElements(plotArea, "line3DChart");
        var threeDLineChart = FirstChartElement(threeDLineCharts);
        var scatterCharts = FindChartElements(plotArea, "scatterChart");
        var scatterChart = FirstChartElement(scatterCharts);
        var areaCharts = FindChartElements(plotArea, "areaChart");
        var areaChart = FirstChartElement(areaCharts);
        var threeDAreaCharts = FindChartElements(plotArea, "area3DChart");
        var threeDAreaChart = FirstChartElement(threeDAreaCharts);
        var radarCharts = FindChartElements(plotArea, "radarChart");
        var stockCharts = FindChartElements(plotArea, "stockChart");
        var deferredAdvancedChart = HasDirectSupportedChart(plotArea) ? null : FindDeferredAdvancedChart(plotArea);
        var threeDBarChart = plotArea?.Element(ChartNs + "bar3DChart");
        var bubbleChart = plotArea?.Element(ChartNs + "bubbleChart");
        var threeDPieChart = plotArea?.Element(ChartNs + "pie3DChart");
        var pieChart = plotArea?.Element(ChartNs + "pieChart");
        var doughnutChart = plotArea?.Element(ChartNs + "doughnutChart");
        bool read;
        if (doughnutChart is not null)
            read = TryReadPieFamilyChart(chartXml, doughnutChart, sheetId, ChartType.Doughnut, sheetNameResolver, out chart);
        else if (threeDPieChart is not null)
            read = TryReadPieFamilyChart(chartXml, threeDPieChart, sheetId, ChartType.ThreeDPie, sheetNameResolver, out chart);
        else if (pieChart is not null)
            read = TryReadPieFamilyChart(chartXml, pieChart, sheetId, ChartType.Pie, sheetNameResolver, out chart);
        else if (bubbleChart is not null)
            read = TryReadBubbleChart(chartXml, bubbleChart, sheetId, sheetNameResolver, out chart);
        else if (areaChart is not null && lineChart is not null)
            read = TryReadAreaLineComboChart(chartXml, plotArea, areaCharts, lineCharts, sheetId, sheetNameResolver, out chart);
        else if (areaCharts.Count > 0)
            read = TryReadAreaChart(chartXml, plotArea, areaCharts, sheetId, ReadAreaChartType(areaChart), sheetNameResolver, out chart);
        else if (threeDAreaChart is not null)
            read = TryReadAreaChart(chartXml, plotArea, threeDAreaCharts, sheetId, ChartType.ThreeDArea, sheetNameResolver, out chart);
        else if (barChart is not null && lineChart is not null && scatterCharts.Count > 0)
            read = TryReadBarLineComboChart(chartXml, plotArea, barCharts, lineCharts, scatterCharts, sheetId, sheetNameResolver, out chart);
        else if (scatterCharts.Count > 0)
            read = TryReadScatterChart(chartXml, plotArea, scatterCharts, sheetId, sheetNameResolver, out chart);
        else if (barChart is not null && lineChart is not null)
            read = TryReadBarLineComboChart(chartXml, plotArea, barCharts, lineCharts, [], sheetId, sheetNameResolver, out chart);
        else if (lineCharts.Count > 1)
            read = TryReadLineChart(chartXml, plotArea, lineCharts, sheetId, sheetNameResolver, out chart);
        else if (lineChart is not null)
            read = TryReadLineChart(chartXml, plotArea, [lineChart], sheetId, sheetNameResolver, out chart);
        else if (threeDLineChart is not null)
            read = TryReadLineLikeChart(chartXml, plotArea, threeDLineCharts, sheetId, ChartType.ThreeDLine, sheetNameResolver, out chart);
        else if (radarCharts.Count > 0)
            read = TryReadLineLikeChart(chartXml, plotArea, radarCharts, sheetId, ChartType.Radar, sheetNameResolver, out chart);
        else if (stockCharts.Count > 0)
            read = TryReadStockChart(chartXml, plotArea, stockCharts, barCharts, sheetId, sheetNameResolver, out chart);
        else if (threeDBarChart is not null)
            read = TryReadThreeDBarChart(chartXml, threeDBarChart, sheetId, sheetNameResolver, out chart);
        else if (deferredAdvancedChart is { } advanced)
            read = TryReadDeferredAdvancedChart(chartXml, advanced.Element, sheetId, advanced.Type, fallbackDataRange, sheetNameResolver, out chart);
        else if (barChart is not null)
            read = TryReadBarChart(chartXml, plotArea, barCharts, sheetId, sheetNameResolver, out chart);
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

    private static XElement? FindPlotArea(XDocument chartXml)
    {
        var standardPlotArea = chartXml.Root?
            .Element(ChartNs + "chart")?
            .Element(ChartNs + "plotArea");
        if (standardPlotArea is not null)
            return standardPlotArea;

        return FindDescendantByLocalName(chartXml.Root, "plotArea");
    }

    private static List<XElement> FindChartElements(XElement? plotArea, string localName) =>
        plotArea?.Elements(ChartNs + localName).ToList() ?? [];

    private static XElement? FirstChartElement(IReadOnlyList<XElement> elements) =>
        elements.Count == 0 ? null : elements[0];

    private static XElement? FindDescendantByLocalName(XElement? element, string localName)
    {
        if (element is null)
            return null;

        foreach (var candidate in element.Descendants())
        {
            if (candidate.Name.LocalName == localName)
                return candidate;
        }

        return null;
    }

    /// <summary>
    /// Collects verbatim series formulas when any formula in the set cannot be parsed
    /// as a single rectangular range (multi-area case). Sets
    /// <see cref="ChartModel.VerbatimSeriesFormulas"/> on the model when triggered.
    /// </summary>
    private static void ApplyVerbatimSeriesFormulasIfNeeded(
        IEnumerable<XElement> allSeries,
        SheetId sheetId,
        ChartModel chart)
    {
        var verbatim = XlsxChartSeriesRangeReader.TryCollectVerbatimFormulas(allSeries, sheetId);
        if (verbatim is not null)
            chart.VerbatimSeriesFormulas = verbatim;
    }
}
