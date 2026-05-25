using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static partial class XlsxChartXmlWriter
{
    private const string ChartContentType = "application/vnd.openxmlformats-officedocument.drawingml.chart+xml";
    private const string ChartExContentType = "application/vnd.ms-office.chartex+xml";

    public static string GetContentType(ChartModel chart) =>
        IsChartExChart(chart.Type) ? ChartExContentType : ChartContentType;

    private static XDocument ToChartExXml(ChartModel chart, Sheet sheet)
    {
        XNamespace chartExNs = "http://schemas.microsoft.com/office/drawing/2014/chartex";
        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

        return new XDocument(
            new XElement(chartExNs + "chartSpace",
                new XAttribute(XNamespace.Xmlns + "cx", chartExNs),
                new XAttribute(XNamespace.Xmlns + "a", drawingNs),
                new XElement(chartExNs + "chart",
                    string.IsNullOrWhiteSpace(chart.Title)
                        ? null
                        : ToChartTitleXml(chart, chartExNs, drawingNs),
                    new XElement(chartExNs + "plotArea",
                        CreateChartExPlotChart(chart, sheet, chartExNs, drawingNs)),
                    ToLegendXml(chart, chartExNs, drawingNs))));
    }

    private static XElement CreateChartExPlotChart(
        ChartModel chart,
        Sheet sheet,
        XNamespace chartExNs,
        XNamespace drawingNs) =>
        new(chartExNs + ToChartExFamilyElementName(chart.Type),
            chart.Type == ChartType.Pareto
                ? new XElement(chartExNs + "paretoLine", new XAttribute("val", "1"))
                : null,
            BuildChartSeries(chart, sheet, chartExNs, drawingNs));

    private static bool IsChartExChart(ChartType chartType) =>
        chartType is ChartType.Treemap
            or ChartType.Sunburst
            or ChartType.Histogram
            or ChartType.Pareto
            or ChartType.BoxAndWhisker
            or ChartType.Waterfall
            or ChartType.Funnel;

    private static string ToChartExFamilyElementName(ChartType chartType) =>
        chartType switch
        {
            ChartType.Treemap => "treemapChart",
            ChartType.Sunburst => "sunburstChart",
            ChartType.Histogram or ChartType.Pareto => "histogramChart",
            ChartType.BoxAndWhisker => "boxWhiskerChart",
            ChartType.Waterfall => "waterfallChart",
            ChartType.Funnel => "funnelChart",
            _ => throw new ArgumentOutOfRangeException(nameof(chartType), chartType, null)
        };
}
