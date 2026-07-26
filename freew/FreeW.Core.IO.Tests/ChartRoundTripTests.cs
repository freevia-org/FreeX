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
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
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

    private static byte[] ReplaceEntryXml(byte[] docx, string entryPath, XDocument replacement)
    {
        using var source = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        using var output = new MemoryStream();
        using (var destination = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in source.Entries)
            {
                var target = destination.CreateEntry(entry.FullName);
                using var targetStream = target.Open();
                if (entry.FullName == entryPath)
                {
                    replacement.Save(targetStream);
                }
                else
                {
                    using var sourceStream = entry.Open();
                    sourceStream.CopyTo(targetStream);
                }
            }
        }

        return output.ToArray();
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
        barChart.Element(C + "barDir")!.Attribute("val")!.Value.Should().Be("col");

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

    // --- F1: editable chart data (embedded companion workbook + c:externalData) ---

    [Fact]
    public void Chart_EmbeddedWorkbookExternalDataAndChartPartRels_ArePresentInZip()
    {
        var docx = WriteBytes(SingleColumnChartDocument());

        using (var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read))
        {
            // The embedded companion workbook part exists.
            zip.GetEntry("word/embeddings/Microsoft_Excel_Worksheet1.xlsx")
                .Should().NotBeNull("the chart must carry an editable companion workbook");
            // The chart part has its own _rels referencing that workbook via a "package" relationship.
            zip.GetEntry("word/charts/_rels/chart1.xml.rels")
                .Should().NotBeNull("the chart part must own a _rels pointing at its workbook");
        }

        // [Content_Types].xml declares the xlsx Default so the workbook part is typed.
        var types = EntryXml(docx, "[Content_Types].xml");
        types.Root!.Elements(Ct + "Default")
            .Should().Contain(d =>
                d.Attribute("Extension")!.Value == "xlsx"
                && d.Attribute("ContentType")!.Value == "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

        // The chart-part relationship targets the workbook with the "package" relationship type.
        var chartRels = EntryXml(docx, "word/charts/_rels/chart1.xml.rels");
        var pkgRel = chartRels.Root!.Elements(Rel + "Relationship")
            .Single(r => r.Attribute("Type")!.Value.EndsWith("/package", System.StringComparison.Ordinal));
        pkgRel.Attribute("Target")!.Value.Should().Be("../embeddings/Microsoft_Excel_Worksheet1.xlsx");

        // The chart XML carries c:externalData wired to that relationship id.
        var chartXml = EntryXml(docx, "word/charts/chart1.xml");
        var externalData = chartXml.Descendants(C + "externalData").Should().ContainSingle().Subject;
        externalData.Attribute(R + "id")!.Value.Should().Be(pkgRel.Attribute("Id")!.Value);
    }

    [Fact]
    public void EmbeddedWorkbook_ContainsCategoriesAndSeriesData()
    {
        var docx = WriteBytes(SingleColumnChartDocument());

        // The embedded part is itself a ZIP (OPC); open it and read the worksheet's inline-string cells.
        byte[] xlsxBytes;
        using (var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read))
        using (var entry = zip.GetEntry("word/embeddings/Microsoft_Excel_Worksheet1.xlsx")!.Open())
        using (var buffer = new MemoryStream())
        {
            entry.CopyTo(buffer);
            xlsxBytes = buffer.ToArray();
        }

        XNamespace s = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XDocument sheet;
        using (var xlsx = new ZipArchive(new MemoryStream(xlsxBytes), ZipArchiveMode.Read))
        using (var sheetStream = xlsx.GetEntry("xl/worksheets/sheet1.xml")!.Open())
            sheet = XDocument.Load(sheetStream);

        var texts = sheet.Descendants(s + "t").Select(t => t.Value).ToList();
        var numbers = sheet.Descendants(s + "v").Select(v => v.Value).ToList();

        // Series name header + category labels are inline strings; the values are numeric cells.
        texts.Should().Contain("Revenue");
        texts.Should().Contain(new[] { "Q1", "Q2", "Q3" });
        numbers.Should().Contain(new[] { "10", "25.5", "17" });
    }

    [Theory]
    [InlineData(ChartKind.Scatter)]
    [InlineData(ChartKind.Area)]
    [InlineData(ChartKind.Doughnut)]
    public void RicherChartKinds_RoundTripTheirKindAndData(ChartKind kind)
    {
        var chart = Chart.Create(kind, ["1", "2", "3"], [4.0, 5.0, 6.0], seriesName: "S", title: "T");
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromChart(chart));
        doc.Blocks.Add(paragraph);

        var read = RoundTrip(doc);

        var roundTripped = read.Paragraphs.Single().Runs.Single(r => r.Chart is not null).Chart!;
        roundTripped.Kind.Should().Be(kind);
        roundTripped.Title.Should().Be("T");
        roundTripped.Categories.Should().Equal("1", "2", "3");
        roundTripped.Series.Single().Values.Should().Equal(4.0, 5.0, 6.0);
    }

    [Fact]
    public void ScatterChart_EmitsScatterChartWithXValAndYVal()
    {
        var chart = Chart.Create(ChartKind.Scatter, ["1", "2"], [3.0, 4.0], seriesName: "S");
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromChart(chart));
        doc.Blocks.Add(paragraph);

        var docx = WriteBytes(doc);
        var chartXml = EntryXml(docx, "word/charts/chart1.xml");

        var scatter = chartXml.Descendants(C + "scatterChart").Should().ContainSingle().Subject;
        scatter.Element(C + "scatterStyle")!.Attribute("val")!.Value.Should().Be("marker");
        var ser = scatter.Elements(C + "ser").Should().ContainSingle().Subject;
        ser.Element(C + "xVal")!.Descendants(C + "numCache").Should().ContainSingle();
        ser.Element(C + "yVal")!.Descendants(C + "numCache").Should().ContainSingle();
    }

    [Fact]
    public void LegendAndAxisTitles_RoundTrip()
    {
        var chart = Chart.Create(ChartKind.Column, ["A", "B"], [1.0, 2.0], title: "T");
        chart.ShowLegend = true;
        chart.CategoryAxisTitle = "Quarter";
        chart.ValueAxisTitle = "USD";
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromChart(chart));
        doc.Blocks.Add(paragraph);

        var read = RoundTrip(doc);

        var roundTripped = read.Paragraphs.Single().Runs.Single(r => r.Chart is not null).Chart!;
        roundTripped.ShowLegend.Should().BeTrue();
        roundTripped.CategoryAxisTitle.Should().Be("Quarter");
        roundTripped.ValueAxisTitle.Should().Be("USD");
    }

    [Fact]
    public void ChartWithoutLegendOrAxisTitles_RoundTripsWithDefaultsOff()
    {
        var read = RoundTrip(SingleColumnChartDocument());

        var chart = read.Paragraphs.Single().Runs.Single(r => r.Chart is not null).Chart!;
        chart.ShowLegend.Should().BeFalse();
        chart.CategoryAxisTitle.Should().BeNull();
        chart.ValueAxisTitle.Should().BeNull();
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
            // Each chart gets its own companion workbook + part rels (F1).
            zip.GetEntry("word/embeddings/Microsoft_Excel_Worksheet1.xlsx").Should().NotBeNull();
            zip.GetEntry("word/embeddings/Microsoft_Excel_Worksheet2.xlsx").Should().NotBeNull();
            zip.GetEntry("word/charts/_rels/chart1.xml.rels").Should().NotBeNull();
            zip.GetEntry("word/charts/_rels/chart2.xml.rels").Should().NotBeNull();
        }

        var read = DocxReader.Read(new MemoryStream(docx));
        var charts = read.Paragraphs.SelectMany(p => p.Runs).Where(r => r.Chart is not null).Select(r => r.Chart!).ToList();
        charts.Should().HaveCount(2);
        charts[0].Title.Should().Be("First");
        charts[0].Kind.Should().Be(ChartKind.Column);
        charts[1].Title.Should().Be("Second");
        charts[1].Kind.Should().Be(ChartKind.Bar);
    }

    // ── Chart Design galleries round-trip (StyleId / ColorSchemeId / QuickLayoutId) ──

    [Fact]
    public void ChartStyleId_RoundTripsViaC_StyleElement()
    {
        var chart = Chart.Create(ChartKind.Column, ["A", "B"], [1.0, 2.0]);
        chart.StyleId = 3; // Style 3
        var doc = new TextDocument();
        var p = new Paragraph();
        p.Runs.Add(Run.FromChart(chart));
        doc.Blocks.Add(p);

        var docx = WriteBytes(doc);

        // The chart part must carry a c:style element with val="3".
        var chartXml = EntryXml(docx, "word/charts/chart1.xml");
        var styleElem = chartXml.Descendants(C + "style").Should().ContainSingle().Subject;
        styleElem.Attribute("val")!.Value.Should().Be("3");

        // And the reader must recover it.
        var read = RoundTrip(doc);
        var roundTripped = read.Paragraphs.Single().Runs.Single(r => r.Chart is not null).Chart!;
        roundTripped.StyleId.Should().Be(3);
    }

    [Fact]
    public void ChartColorSchemeId_RoundTripsViaFreewExtension()
    {
        var chart = Chart.Create(ChartKind.Column, ["A", "B"], [1.0, 2.0]);
        chart.ColorSchemeId = "mono-blue";
        var doc = new TextDocument();
        var p = new Paragraph();
        p.Runs.Add(Run.FromChart(chart));
        doc.Blocks.Add(p);

        var docx = WriteBytes(doc);

        // The chart part must carry a schema-valid private c:ext URI.
        var chartXml = EntryXml(docx, "word/charts/chart1.xml");
        var extensionUri = chartXml.Descendants(C + "ext").Should().ContainSingle()
            .Which.Attribute("uri")!.Value;
        extensionUri.Should().Be("urn:freew:chart-design:2026#colorScheme=mono-blue");
        var series = chartXml.Descendants(C + "ser").Should().ContainSingle().Subject;
        series.Elements(C + "dPt").Should().HaveCount(2);
        series
            .Element(C + "dPt")!
            .Descendants(A + "srgbClr")
            .Should().ContainSingle()
            .Which.Attribute("val")!.Value.Should().Be("214A82");

        // And the reader must recover it.
        var read = RoundTrip(doc);
        var roundTripped = read.Paragraphs.Single().Runs.Single(r => r.Chart is not null).Chart!;
        roundTripped.ColorSchemeId.Should().Be("mono-blue");
    }

    [Fact]
    public void ChartQuickLayoutId_RoundTripsViaFreewExtension()
    {
        var chart = Chart.Create(ChartKind.Column, ["A", "B"], [1.0, 2.0]);
        chart.QuickLayoutId = 5;
        var doc = new TextDocument();
        var p = new Paragraph();
        p.Runs.Add(Run.FromChart(chart));
        doc.Blocks.Add(p);

        var docx = WriteBytes(doc);

        // The chart part must carry a schema-valid private c:ext URI.
        var chartXml = EntryXml(docx, "word/charts/chart1.xml");
        var extensionUri = chartXml.Descendants(C + "ext").Should().ContainSingle()
            .Which.Attribute("uri")!.Value;
        extensionUri.Should().Be("urn:freew:chart-design:2026#quickLayout=5");

        // And the reader must recover it.
        var read = RoundTrip(doc);
        var roundTripped = read.Paragraphs.Single().Runs.Single(r => r.Chart is not null).Chart!;
        roundTripped.QuickLayoutId.Should().Be(5);
    }

    [Fact]
    public void LegacyChartDesignExtension_IsStillRead()
    {
        var docx = WriteBytes(SingleColumnChartDocument());
        var chartXml = EntryXml(docx, "word/charts/chart1.xml");
        XNamespace freew = "http://schemas.freew.dev/chart-design/2026";
        chartXml.Root!.Add(new XElement(C + "extLst",
            new XElement(C + "ext",
                new XAttribute("uri", "{FW-ChartDesign-2026}"),
                new XElement(freew + "colorScheme", new XAttribute("id", "colorful3")),
                new XElement(freew + "quickLayout", new XAttribute("id", "9")))));

        docx = ReplaceEntryXml(docx, "word/charts/chart1.xml", chartXml);
        using var stream = new MemoryStream(docx);
        var read = DocxReader.Read(stream);
        var chart = read.Paragraphs.Single().Runs.Single(run => run.Chart is not null).Chart!;

        chart.ColorSchemeId.Should().Be("colorful3");
        chart.QuickLayoutId.Should().Be(9);
    }

    [Fact]
    public void ChartDataLabels_EmitsStandardShowValWhenStyleRequestsThem()
    {
        var chart = Chart.Create(ChartKind.Column, ["A", "B"], [1.0, 2.0]);
        chart.StyleId = 7;
        chart.QuickLayoutId = 9;
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromChart(chart));
        doc.Blocks.Add(paragraph);

        var chartXml = EntryXml(WriteBytes(doc), "word/charts/chart1.xml");
        var labels = chartXml.Descendants(C + "dLbls").Should().ContainSingle().Subject;
        labels.Element(C + "showVal")!.Attribute("val")!.Value.Should().Be("1");
        labels.Element(C + "showLegendKey")!.Attribute("val")!.Value.Should().Be("0");
        labels.Element(C + "showCatName")!.Attribute("val")!.Value.Should().Be("0");
        labels.Element(C + "showSerName")!.Attribute("val")!.Value.Should().Be("0");
        var plotArea = chartXml.Descendants(C + "plotArea").Should().ContainSingle().Subject;
        plotArea.Element(C + "spPr").Should().NotBeNull();
        var plotProperties = plotArea.Element(C + "spPr")!;
        plotProperties.Descendants(A + "solidFill").Should().ContainSingle();
    }

    [Fact]
    public void AllThreeGalleryIds_RoundTripTogether()
    {
        var chart = Chart.Create(ChartKind.Line, ["X", "Y"], [3.0, 4.0]);
        chart.StyleId = 7;
        chart.ColorSchemeId = "colorful3";
        chart.QuickLayoutId = 9;
        var doc = new TextDocument();
        var p = new Paragraph();
        p.Runs.Add(Run.FromChart(chart));
        doc.Blocks.Add(p);

        var read = RoundTrip(doc);
        var roundTripped = read.Paragraphs.Single().Runs.Single(r => r.Chart is not null).Chart!;
        roundTripped.StyleId.Should().Be(7);
        roundTripped.ColorSchemeId.Should().Be("colorful3");
        roundTripped.QuickLayoutId.Should().Be(9);
    }

    [Fact]
    public void DefaultStyleIdZero_DoesNotEmitC_StyleElement()
    {
        // When StyleId == 0 (default) the writer must omit c:style so existing docx output stays clean.
        var chart = Chart.Create(ChartKind.Column, ["A"], [1.0]);
        // chart.StyleId is 0 by default
        var doc = new TextDocument();
        var p = new Paragraph();
        p.Runs.Add(Run.FromChart(chart));
        doc.Blocks.Add(p);

        var docx = WriteBytes(doc);
        var chartXml = EntryXml(docx, "word/charts/chart1.xml");
        chartXml.Descendants(C + "style").Should().BeEmpty("StyleId 0 (default) must not emit c:style");
    }
}
