using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-trip coverage for inline DrawingML charts (roadmap item W3): a <see cref="Run.Chart"/> must
/// survive write→read with its kind/title/categories/series values, materialise a real chart PART with a
/// content-type Override and a document relationship, and reference that part from an inline w:drawing.
/// </summary>
public class ChartRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace C = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace Ct = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";

    private const string ChartContentType = "application/vnd.openxmlformats-officedocument.drawingml.chart+xml";

    private static TextDocument RoundTrip(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    private static byte[] WriteBytes(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        return stream.ToArray();
    }

    private static XDocument EntryXml(byte[] docx, string entryPath)
    {
        using var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        using var entry = zip.GetEntry(entryPath)!.Open();
        return XDocument.Load(entry);
    }

    private static TextDocument SingleColumnChartDocument()
    {
        var chart = Chart.Create(
            ChartKind.Column,
            categories: ["Q1", "Q2", "Q3"],
            values: [10.0, 25.5, 17.0],
            seriesName: "Revenue",
            title: "Quarterly Revenue");
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromChart(chart));
        doc.Blocks.Add(paragraph);
        return doc;
    }

    [Fact]
    public void ColumnChart_KindTitleCategoriesAndValues_SurviveRoundTrip()
    {
        var read = RoundTrip(SingleColumnChartDocument());

        var chartRun = read.Paragraphs.Single().Runs.Single(r => r.Chart is not null);
        var chart = chartRun.Chart!;

        chart.Kind.Should().Be(ChartKind.Column);
        chart.Title.Should().Be("Quarterly Revenue");
        chart.Categories.Should().Equal("Q1", "Q2", "Q3");

        var series = chart.Series.Should().ContainSingle().Subject;
        series.Name.Should().Be("Revenue");
        series.Values.Should().Equal(10.0, 25.5, 17.0);
    }

    [Fact]
    public void Chart_PartContentTypeOverrideAndRelationship_ArePresentInZip()
    {
        var docx = WriteBytes(SingleColumnChartDocument());

        // The chart part itself exists in the package.
        using (var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read))
        {
            zip.GetEntry("word/charts/chart1.xml").Should().NotBeNull("the chart part must be a real OPC part");
        }

        // [Content_Types].xml declares an Override for the chart part with the chart content type.
        var types = EntryXml(docx, "[Content_Types].xml");
        types.Root!.Elements(Ct + "Override")
            .Should().ContainSingle(o =>
                o.Attribute("PartName")!.Value == "/word/charts/chart1.xml"
                && o.Attribute("ContentType")!.Value == ChartContentType);

        // document.xml.rels carries a chart relationship pointing at the part.
        var rels = EntryXml(docx, "word/_rels/document.xml.rels");
        var chartRel = rels.Root!.Elements(Rel + "Relationship")
            .Single(r => r.Attribute("Type")!.Value.EndsWith("/chart", System.StringComparison.Ordinal));
        chartRel.Attribute("Target")!.Value.Should().Be("charts/chart1.xml");

        // document.xml references that relationship from an inline c:chart drawing.
        var documentXml = EntryXml(docx, "word/document.xml");
        var cChart = documentXml.Descendants(C + "chart").Single();
        cChart.Attribute(R + "id")!.Value.Should().Be(chartRel.Attribute("Id")!.Value);
    }

    [Fact]
    public void ChartPart_EmitsBarChartWithStringAndNumberCaches()
    {
        var docx = WriteBytes(SingleColumnChartDocument());
        var chartXml = EntryXml(docx, "word/charts/chart1.xml");

        var barChart = chartXml.Descendants(C + "barChart").Should().ContainSingle().Subject;
        // Column charts use barDir "col".
        barChart.Element(C + "barDir")!.Attribute(C + "val")!.Value.Should().Be("col");

        var ser = barChart.Elements(C + "ser").Should().ContainSingle().Subject;
        // Category labels live in a c:cat string cache; values in a c:val number cache.
        ser.Element(C + "cat")!.Descendants(C + "strCache").Should().ContainSingle();
        ser.Element(C + "val")!.Descendants(C + "numCache").Should().ContainSingle();
    }

    [Fact]
    public void BarChart_RoundTripsAsHorizontalKind()
    {
        var chart = Chart.Create(ChartKind.Bar, ["A", "B"], [1.0, 2.0]);
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromChart(chart));
        doc.Blocks.Add(paragraph);

        var read = RoundTrip(doc);

        read.Paragraphs.Single().Runs.Single(r => r.Chart is not null).Chart!.Kind.Should().Be(ChartKind.Bar);
    }

    [Fact]
    public void LineAndPieCharts_RoundTripTheirKinds()
    {
        foreach (var kind in new[] { ChartKind.Line, ChartKind.Pie })
        {
            var chart = Chart.Create(kind, ["X", "Y"], [3.0, 4.0]);
            var doc = new TextDocument();
            var paragraph = new Paragraph();
            paragraph.Runs.Add(Run.FromChart(chart));
            doc.Blocks.Add(paragraph);

            var read = RoundTrip(doc);

            read.Paragraphs.Single().Runs.Single(r => r.Chart is not null).Chart!.Kind.Should().Be(kind);
        }
    }

    [Fact]
    public void TitlelessChart_RoundTripsWithNullTitle()
    {
        var chart = Chart.Create(ChartKind.Column, ["A"], [1.0]);
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromChart(chart));
        doc.Blocks.Add(paragraph);

        var read = RoundTrip(doc);

        read.Paragraphs.Single().Runs.Single(r => r.Chart is not null).Chart!.Title.Should().BeNull();
    }

    [Fact]
    public void MultiSeriesColumnChart_AllSeriesSurviveRoundTrip()
    {
        var chart = new Chart { Kind = ChartKind.Column, Title = "Sales" };
        chart.Categories.AddRange(["Jan", "Feb"]);
        chart.Series.Add(new ChartSeries("North", [5.0, 6.0]));
        chart.Series.Add(new ChartSeries("South", [7.0, 8.0]));
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromChart(chart));
        doc.Blocks.Add(paragraph);

        var read = RoundTrip(doc);

        var roundTripped = read.Paragraphs.Single().Runs.Single(r => r.Chart is not null).Chart!;
        roundTripped.Series.Should().HaveCount(2);
        roundTripped.Series[0].Name.Should().Be("North");
        roundTripped.Series[0].Values.Should().Equal(5.0, 6.0);
        roundTripped.Series[1].Name.Should().Be("South");
        roundTripped.Series[1].Values.Should().Equal(7.0, 8.0);
    }

    [Fact]
    public void Chart_RoundTripsInsideTableCell()
    {
        // Charts are an inline run mark, so they must flow through table cells like any other run.
        var table = Table.Create(1, 1);
        var chart = Chart.Create(ChartKind.Column, ["A", "B"], [9.0, 12.0], title: "Cell Chart");
        table.Rows[0].Cells[0].Paragraphs[0].Runs.Add(Run.FromChart(chart));
        var doc = new TextDocument();
        doc.Blocks.Add(table);

        var read = RoundTrip(doc);

        var cellParagraph = ((Table)read.Blocks.Single()).Rows[0].Cells[0].Paragraphs.Single();
        var roundTripped = cellParagraph.Runs.Single(r => r.Chart is not null).Chart!;
        roundTripped.Title.Should().Be("Cell Chart");
        roundTripped.Series.Single().Values.Should().Equal(9.0, 12.0);
    }

    [Fact]
    public void TwoCharts_GetDistinctPartsAndRelationships()
    {
        var doc = new TextDocument();
        var p1 = new Paragraph();
        p1.Runs.Add(Run.FromChart(Chart.Create(ChartKind.Column, ["A"], [1.0], title: "First")));
        var p2 = new Paragraph();
        p2.Runs.Add(Run.FromChart(Chart.Create(ChartKind.Bar, ["B"], [2.0], title: "Second")));
        doc.Blocks.Add(p1);
        doc.Blocks.Add(p2);

        var docx = WriteBytes(doc);
        using (var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read))
        {
            zip.GetEntry("word/charts/chart1.xml").Should().NotBeNull();
            zip.GetEntry("word/charts/chart2.xml").Should().NotBeNull();
        }

        var read = DocxReader.Read(new MemoryStream(docx));
        var charts = read.Paragraphs.SelectMany(p => p.Runs).Where(r => r.Chart is not null).Select(r => r.Chart!).ToList();
        charts.Should().HaveCount(2);
        charts[0].Title.Should().Be("First");
        charts[0].Kind.Should().Be(ChartKind.Column);
        charts[1].Title.Should().Be("Second");
        charts[1].Kind.Should().Be(ChartKind.Bar);
    }
}
