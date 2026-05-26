using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static partial class XlsxChartXmlWriter
{
    private const string ChartContentType = "application/vnd.openxmlformats-officedocument.drawingml.chart+xml";
    private const string ChartExContentType = "application/vnd.ms-office.chartex+xml";
    private const string ChartRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart";
    private const string ChartExRelationshipType = "http://schemas.microsoft.com/office/2014/relationships/chartEx";

    public static string GetContentType(ChartModel chart) =>
        IsChartExChart(chart.Type) ? ChartExContentType : ChartContentType;

    public static string GetRelationshipType(ChartModel chart) =>
        IsChartExChart(chart.Type) ? ChartExRelationshipType : ChartRelationshipType;

    private static XDocument ToChartExXml(ChartModel chart, Sheet sheet)
    {
        XNamespace chartExNs = "http://schemas.microsoft.com/office/drawing/2014/chartex";
        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
        var chartData = BuildChartExData(chart, sheet, chartExNs).ToList();

        return new XDocument(
            new XElement(chartExNs + "chartSpace",
                new XAttribute(XNamespace.Xmlns + "cx", chartExNs),
                new XAttribute(XNamespace.Xmlns + "a", drawingNs),
                new XElement(chartExNs + "chartData", chartData),
                new XElement(chartExNs + "chart",
                    string.IsNullOrWhiteSpace(chart.Title)
                        ? null
                        : ToChartTitleXml(chart, chartExNs, drawingNs),
                    new XElement(chartExNs + "plotArea",
                        new XElement(chartExNs + "plotAreaRegion",
                            BuildChartExSeries(chart, chartExNs, chartData.Count))),
                    ToLegendXml(chart, chartExNs, drawingNs))));
    }

    private static IEnumerable<XElement> BuildChartExData(
        ChartModel chart,
        Sheet sheet,
        XNamespace chartExNs)
    {
        var dataStartRow = chart.FirstRowIsHeader ? chart.DataRange.Start.Row + 1 : chart.DataRange.Start.Row;
        var seriesStartCol = chart.FirstColIsCategories ? chart.DataRange.Start.Col + 1 : chart.DataRange.Start.Col;
        var categoryRange = chart.FirstColIsCategories
            ? FormatSheetRange(sheet.Name, dataStartRow, chart.DataRange.Start.Col, chart.DataRange.End.Row, chart.DataRange.Start.Col)
            : null;

        var seriesIndex = 0;
        for (var col = seriesStartCol; col <= chart.DataRange.End.Col; col++)
        {
            var valueRange = FormatSheetRange(sheet.Name, dataStartRow, col, chart.DataRange.End.Row, col);
            yield return new XElement(chartExNs + "data",
                new XAttribute("id", ToChartExDataId(seriesIndex)),
                string.IsNullOrWhiteSpace(categoryRange)
                    ? null
                    : new XElement(chartExNs + "strDim",
                        new XAttribute("type", "cat"),
                        new XElement(chartExNs + "f", categoryRange)),
                new XElement(chartExNs + "numDim",
                    new XAttribute("type", "val"),
                    new XElement(chartExNs + "f", valueRange),
                    chart.FirstRowIsHeader
                        ? new XElement(chartExNs + "nf",
                            FormatSheetRange(sheet.Name, chart.DataRange.Start.Row, col, chart.DataRange.Start.Row, col))
                        : null));
            seriesIndex++;
        }
    }

    private static IEnumerable<XElement> BuildChartExSeries(
        ChartModel chart,
        XNamespace chartExNs,
        int dataCount)
    {
        for (var seriesIndex = 0; seriesIndex < dataCount; seriesIndex++)
        {
            var dataId = ToChartExDataId(seriesIndex);
            yield return new XElement(chartExNs + "series",
                new XAttribute("layoutId", ToChartExSeriesLayoutId(chart.Type)),
                new XElement(chartExNs + "dataId", new XAttribute("val", dataId)));

            if (chart.Type == ChartType.Pareto)
            {
                yield return new XElement(chartExNs + "series",
                    new XAttribute("layoutId", "paretoLine"),
                    new XElement(chartExNs + "dataId", new XAttribute("val", dataId)));
            }
        }
    }

    private static bool IsChartExChart(ChartType chartType) =>
        chartType is ChartType.Treemap
            or ChartType.Sunburst
            or ChartType.Histogram
            or ChartType.Pareto
            or ChartType.BoxAndWhisker
            or ChartType.Waterfall
            or ChartType.Funnel;

    private static string ToChartExDataId(int seriesIndex) =>
        FormattableString.Invariant($"data{seriesIndex}");

    private static string ToChartExSeriesLayoutId(ChartType chartType) =>
        chartType switch
        {
            ChartType.Treemap => "treemap",
            ChartType.Sunburst => "sunburst",
            ChartType.Histogram or ChartType.Pareto => "clusteredColumn",
            ChartType.BoxAndWhisker => "boxWhisker",
            ChartType.Waterfall => "waterfall",
            ChartType.Funnel => "funnel",
            _ => throw new ArgumentOutOfRangeException(nameof(chartType), chartType, null)
        };
}
