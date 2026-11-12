using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

public sealed class ChartNativeVisualSettingsTests
{
    private static readonly XNamespace C = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    [Fact]
    public void NativeVisualSettings_RoundTripConcreteChartElements()
    {
        var chart = Chart.Create(ChartKind.Scatter, ["1", "2"], [3.0, 4.0]);
        chart.StyleId = 4;
        chart.NativeVisualSettings = new ChartNativeVisualSettings(false, false, false, true);
        var document = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromChart(chart));
        document.Blocks.Add(paragraph);

        var docx = Write(document);
        var chartXml = ReadEntry(docx, "word/charts/chart1.xml");
        var scatter = chartXml.Descendants(C + "scatterChart").Single();
        scatter.Element(C + "scatterStyle")!.Attribute("val")!.Value.Should().Be("lineMarker");
        scatter.Element(C + "dLbls").Should().BeNull();
        chartXml.Descendants(C + "majorGridlines").Should().BeEmpty();
        chartXml.Descendants(C + "plotArea").Single().Element(C + "spPr").Should().BeNull();

        using var input = new MemoryStream(docx);
        var roundTripped = DocxReader.Read(input).Paragraphs.Single().Runs.Single().Chart!;
        roundTripped.NativeVisualSettings.Should().Be(new ChartNativeVisualSettings(false, false, false, true));
    }

    [Fact]
    public void NativeTextSizes_ResolveFromTextPropertiesThenChartSpaceThenWordDefaults()
    {
        var chart = Chart.Create(ChartKind.Column, ["Q1", "Q2"], [1.0, 2.0], "Revenue", "Quarterly revenue");
        chart.ShowLegend = true;
        chart.CategoryAxisTitle = "Quarter";
        chart.ValueAxisTitle = "USD";
        var document = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromChart(chart));
        document.Blocks.Add(paragraph);
        var docx = Write(document);

        // A part without any text properties renders with Word's defaults (drawing-objects-complex).
        var bare = PatchChart(docx, chartSpace =>
        {
            chartSpace.Descendants(C + "txPr").Remove();
            foreach (var sized in chartSpace.Descendants().Where(element => element.Attribute("sz") is not null).ToList())
                sized.Attribute("sz")!.Remove();
        });
        ReadChart(bare).NativeTextSizes.Should().Be(ChartTextSizes.WordDefault);

        // Element text properties win; rich text without a size falls back to the chart-space default.
        var sized = PatchChart(bare, chartSpace =>
        {
            chartSpace.Add(TextProperties(1000));
            chartSpace.Descendants(C + "legend").Single().Add(TextProperties(800));
            chartSpace.Descendants(C + "catAx").Single().Add(TextProperties(1100));
            var titleRun = chartSpace.Element(C + "chart")!.Element(C + "title")!.Descendants(A + "r").First();
            titleRun.AddFirst(new XElement(A + "rPr", new XAttribute("sz", "2000")));
        });
        ReadChart(sized).NativeTextSizes.Should().Be(new ChartTextSizes(20, 10, 11, 8, 10));
    }

    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";

    private static XElement TextProperties(int hundredths) =>
        new(C + "txPr",
            new XElement(A + "bodyPr"),
            new XElement(A + "lstStyle"),
            new XElement(A + "p",
                new XElement(A + "pPr", new XElement(A + "defRPr", new XAttribute("sz", hundredths))),
                new XElement(A + "endParaRPr")));

    private static Chart ReadChart(byte[] docx)
    {
        using var input = new MemoryStream(docx);
        return DocxReader.Read(input).Paragraphs.Single().Runs.Single().Chart!;
    }

    private static byte[] PatchChart(byte[] docx, Action<XElement> patch)
    {
        using var buffer = new MemoryStream();
        buffer.Write(docx);
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.GetEntry("word/charts/chart1.xml")!;
            XDocument xml;
            using (var stream = entry.Open())
                xml = XDocument.Load(stream);
            patch(xml.Root!);
            entry.Delete();
            using var output = zip.CreateEntry("word/charts/chart1.xml").Open();
            xml.Save(output);
        }

        return buffer.ToArray();
    }

    private static byte[] Write(TextDocument document)
    {
        using var output = new MemoryStream();
        DocxWriter.Write(document, output);
        return output.ToArray();
    }

    private static XDocument ReadEntry(byte[] docx, string partPath)
    {
        using var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        using var entry = zip.GetEntry(partPath)!.Open();
        return XDocument.Load(entry);
    }
}
