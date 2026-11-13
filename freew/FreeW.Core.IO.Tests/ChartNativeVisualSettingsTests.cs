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

    [Fact]
    public void AuthoredTextSizes_SurviveAFreeWSaveInTheirSchemaSlots()
    {
        // FreeW regenerates every modelled chart part on save, so the sizes the source part stated must be
        // written back where Word reads them — and remain schema valid.
        var chart = Chart.Create(ChartKind.Column, ["Q1", "Q2"], [1.0, 2.0], "Revenue", "Quarterly revenue");
        chart.ShowLegend = true;
        chart.CategoryAxisTitle = "Quarter";
        chart.ValueAxisTitle = "USD";
        chart.NativeVisualSettings = new ChartNativeVisualSettings(true, false, ShowDataLabels: true, false);
        var document = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromChart(chart));
        document.Blocks.Add(paragraph);

        var source = PatchChart(Write(document), chartSpace =>
        {
            chartSpace.Element(C + "externalData")!.AddBeforeSelf(TextProperties(1000));
            chartSpace.Descendants(C + "legend").Single().Add(TextProperties(800));
            foreach (var crossAx in chartSpace.Descendants(C + "crossAx").ToList())
                crossAx.AddBeforeSelf(TextProperties(1100));
            chartSpace.Descendants(C + "dLbls").Single().AddFirst(TextProperties(700));
            chartSpace.Element(C + "chart")!.Element(C + "title")!.Descendants(A + "r").Single()
                .AddFirst(new XElement(A + "rPr", new XAttribute("sz", "2000")));
        });
        var imported = ReadChart(source);
        imported.AuthoredTextSizes.Should().Be(new ChartAuthoredTextSizes(10, 20, null, 11, 8, 7));

        var resaved = WriteChart(imported);
        var chartSpace = ReadEntry(resaved, "word/charts/chart1.xml").Root!;
        static string? Size(XElement? textProperties) =>
            textProperties?.Descendants(A + "defRPr").Single().Attribute("sz")?.Value;
        Size(chartSpace.Element(C + "txPr")).Should().Be("1000");
        Size(chartSpace.Descendants(C + "legend").Single().Element(C + "txPr")).Should().Be("800");
        chartSpace.Descendants(C + "catAx").Concat(chartSpace.Descendants(C + "valAx"))
            .Select(axis => Size(axis.Element(C + "txPr"))).Should().OnlyContain(size => size == "1100");
        Size(chartSpace.Descendants(C + "dLbls").Single().Element(C + "txPr")).Should().Be("700");
        chartSpace.Element(C + "chart")!.Element(C + "title")!.Descendants(A + "rPr").Single()
            .Attribute("sz")!.Value.Should().Be("2000");
        chartSpace.Descendants(C + "catAx").Concat(chartSpace.Descendants(C + "valAx"))
            .SelectMany(axis => axis.Descendants(A + "rPr")).Should().BeEmpty("the axis titles stated no size");

        var roundTripped = ReadChart(resaved);
        roundTripped.AuthoredTextSizes.Should().Be(imported.AuthoredTextSizes);
        roundTripped.NativeTextSizes.Should().Be(imported.NativeTextSizes);
        ChartPartSchemaErrors(resaved).Should().BeEmpty();
    }

    [Fact]
    public void AuthoredTextSizes_AreNotStampedIntoAPartThatStatedNone_AndEditedSizesAreWritten()
    {
        var chart = Chart.Create(ChartKind.Column, ["Q1", "Q2"], [1.0, 2.0], "Revenue", "Quarterly revenue");
        chart.ShowLegend = true;
        chart.ValueAxisTitle = "USD";
        var document = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromChart(chart));
        document.Blocks.Add(paragraph);

        // No stated sizes: Word's rendering defaults stay out of the saved part.
        var imported = ReadChart(Write(document));
        imported.AuthoredTextSizes.Should().BeNull();
        imported.NativeTextSizes.Should().Be(ChartTextSizes.WordDefault);
        var resaved = ReadEntry(WriteChart(imported), "word/charts/chart1.xml").Root!;
        resaved.Descendants(C + "txPr").Should().BeEmpty();
        resaved.Descendants().Where(element => element.Attribute("sz") is not null).Should().BeEmpty();

        // An edited size is written, and only that one.
        imported.AuthoredTextSizes = new ChartAuthoredTextSizes(LegendPt: 12);
        var edited = WriteChart(imported);
        var editedSpace = ReadEntry(edited, "word/charts/chart1.xml").Root!;
        editedSpace.Descendants(C + "txPr").Should().ContainSingle()
            .Which.Parent!.Name.Should().Be(C + "legend");
        ReadChart(edited).AuthoredTextSizes.Should().Be(new ChartAuthoredTextSizes(LegendPt: 12));
        ChartPartSchemaErrors(edited).Should().BeEmpty();
    }

    private static byte[] WriteChart(Chart chart)
    {
        var document = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromChart(chart));
        document.Blocks.Add(paragraph);
        return Write(document);
    }

    private static List<string> ChartPartSchemaErrors(byte[] docx)
    {
        using var copy = new MemoryStream(docx);
        using var package = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(copy, false);
        return new DocumentFormat.OpenXml.Validation.OpenXmlValidator(DocumentFormat.OpenXml.FileFormatVersions.Office2013)
            .Validate(package)
            .Where(error => error.Part?.Uri.OriginalString.Contains("/charts/", StringComparison.Ordinal) == true)
            .Select(error => $"{error.Id}: {error.Description}")
            .ToList();
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
