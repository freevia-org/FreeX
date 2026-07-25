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
