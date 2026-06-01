using System.Globalization;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static partial class XlsxChartXmlWriter
{
    private const string ChartContentType = "application/vnd.openxmlformats-officedocument.drawingml.chart+xml";
    private const string ChartExContentType = "application/vnd.ms-office.chartex+xml";
    private const string ChartRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart";
    private const string ChartExRelationshipType = "http://schemas.microsoft.com/office/2014/relationships/chartEx";

    public static string GetContentType(ChartModel chart) =>
        ChartTypeSupport.IsChartExFamily(chart.Type) ? ChartExContentType : ChartContentType;

    public static string GetRelationshipType(ChartModel chart) =>
        ChartTypeSupport.IsChartExFamily(chart.Type) ? ChartExRelationshipType : ChartRelationshipType;

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
                    // chartEx (cx:) has its own title/legend shapes — distinct from the classic c:
                    // schema — so it must not reuse the classic builders.
                    string.IsNullOrWhiteSpace(chart.Title)
                        ? null
                        : ToChartExTitleXml(chart, chartExNs, drawingNs),
                    new XElement(chartExNs + "plotArea",
                        new XElement(chartExNs + "plotAreaRegion",
                            BuildChartExSeries(chart, chartExNs, chartData.Count))),
                    ToChartExLegendXml(chart, chartExNs))));
    }

    // CT_Title (chartEx): a tx with a rich text body (a:bodyPr must come first per CT_TextBody).
    private static XElement ToChartExTitleXml(ChartModel chart, XNamespace chartExNs, XNamespace drawingNs) =>
        new(chartExNs + "title",
            new XElement(chartExNs + "tx",
                new XElement(chartExNs + "rich",
                    new XElement(drawingNs + "bodyPr"),
                    new XElement(drawingNs + "p",
                        new XElement(drawingNs + "r",
                            new XElement(drawingNs + "t", chart.Title))))));

    // CT_Legend (chartEx): position is the "pos" attribute (t/b/l/r), not a legendPos child element.
    private static XElement? ToChartExLegendXml(ChartModel chart, XNamespace chartExNs)
    {
        if (!chart.ShowLegend || chart.LegendPosition == ChartLegendPosition.None)
            return null;

        return new XElement(chartExNs + "legend",
            new XAttribute("pos", ToChartExLegendPosition(chart.LegendPosition)),
            new XAttribute("align", "ctr"),
            new XAttribute("overlay", chart.LegendOverlay ? "1" : "0"));
    }

    private static string ToChartExLegendPosition(ChartLegendPosition position) =>
        position switch
        {
            ChartLegendPosition.Top => "t",
            ChartLegendPosition.Bottom => "b",
            ChartLegendPosition.Left => "l",
            ChartLegendPosition.Right => "r",
            _ => "b",
        };

    private static IEnumerable<XElement> BuildChartExData(
        ChartModel chart,
        Sheet sheet,
        XNamespace chartExNs)
    {
        var dataStartRow = chart.FirstRowIsHeader ? chart.DataRange.Start.Row + 1 : chart.DataRange.Start.Row;
        var hasCategoryColumn = chart.FirstColIsCategories && chart.DataRange.End.Col > chart.DataRange.Start.Col;
        var seriesStartCol = hasCategoryColumn ? chart.DataRange.Start.Col + 1 : chart.DataRange.Start.Col;
        var categoryRange = hasCategoryColumn
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
            // Per CT_Series the optional layoutPr (binning / subtotals) follows dataId.
            yield return new XElement(chartExNs + "series",
                new XAttribute("layoutId", ToChartExSeriesLayoutId(chart.Type)),
                new XElement(chartExNs + "dataId", new XAttribute("val", dataId)),
                BuildChartExSeriesLayoutPr(chart, chartExNs));

            if (chart.Type == ChartType.Pareto)
            {
                yield return new XElement(chartExNs + "series",
                    new XAttribute("layoutId", "paretoLine"),
                    new XElement(chartExNs + "dataId", new XAttribute("val", dataId)));
            }
        }
    }

    /// <summary>
    /// Optional per-series layout properties for chartEx families: waterfall total/anchor columns
    /// (<c>cx:subtotals</c>). Histogram bin settings are intentionally not emitted because current
    /// desktop Excel rejects otherwise valid chartEx packages containing <c>cx:binCount</c> or
    /// <c>cx:binSize</c>. Returns null when
    /// the chart carries no such settings so the element is omitted (Excel's defaults apply).
    /// </summary>
    private static XElement? BuildChartExSeriesLayoutPr(ChartModel chart, XNamespace chartExNs)
    {
        var subtotals = BuildChartExSubtotals(chart, chartExNs);
        return subtotals is null
            ? null
            : new XElement(chartExNs + "layoutPr", subtotals);
    }

    private static XElement? BuildChartExSubtotals(ChartModel chart, XNamespace chartExNs)
    {
        if (chart.WaterfallTotalPointIndices is not { Count: > 0 } totals)
            return null;

        return new XElement(chartExNs + "subtotals",
            totals.Where(index => index >= 0)
                .Distinct()
                .OrderBy(index => index)
                .Select(index => new XElement(chartExNs + "idx", new XAttribute("val", index))));
    }

    // cx:data/@id and cx:dataId/@val are xsd:unsignedInt — a bare numeric id, not "data{n}".
    private static string ToChartExDataId(int seriesIndex) =>
        seriesIndex.ToString(CultureInfo.InvariantCulture);

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
