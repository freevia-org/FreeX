using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

public sealed class ChartQuickLayoutNativePackageTests
{
    private static readonly XNamespace C = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    [Fact]
    public void QuickLayout_EmitsNativeTitleLegendAxisAndGridlineSemantics()
    {
        var chart = Chart.Create(ChartKind.Column, ["A", "B"], [1.0, 2.0], title: "Revenue");
        chart.QuickLayoutId = 1;
        chart.ShowLegend = true;
        chart.CategoryAxisTitle = "Quarter";
        chart.ValueAxisTitle = "USD";

        var chartXml = WriteChartXml(chart);
        var chartElement = chartXml.Descendants(C + "chart").Should().ContainSingle().Subject;
        chartElement.Element(C + "title").Should().BeNull();
        chartElement.Element(C + "autoTitleDeleted")!.Attribute("val")!.Value.Should().Be("1");
        chartElement.Element(C + "legend").Should().BeNull();

        var axes = chartXml.Descendants(C + "plotArea").Should().ContainSingle().Subject;
        axes.Descendants(C + "title").Should().BeEmpty();
        axes.Descendants(C + "valAx").Should().ContainSingle().Which
            .Element(C + "majorGridlines").Should().NotBeNull();

        chartXml.Descendants(C + "ext").Should().ContainSingle()
            .Which.Attribute("uri")!.Value.Should().Be("urn:freew:chart-design:2026#quickLayout=1");
    }

    private static XDocument WriteChartXml(Chart chart)
    {
        var document = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromChart(chart));
        document.Blocks.Add(paragraph);

        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        using var zip = new ZipArchive(new MemoryStream(stream.ToArray()), ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/charts/chart1.xml")!.Open();
        return XDocument.Load(entry);
    }
}
