using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-trip coverage for the last DOCX media-fidelity gap: inline images that live in COMMENT parts or in
/// UNMODELLED CHART (chartex) parts — neither of which travels through the body/header/footer run flow.
///
/// <para>Two preservation paths are exercised:</para>
/// <list type="number">
/// <item>An unmodelled chart (chartex) referenced by a body <c>w:drawing</c> is captured VERBATIM — the drawing
/// XML re-emits inside the run and the chartex part + its <c>_rels</c> + the media it references survive as
/// preserved parts, with the document→chart relationship + content-types re-emitted.</item>
/// <item>A comment carrying an image is fully MODELLED — the image becomes a <see cref="Run.Image"/> in the
/// comment, and the writer re-emits <c>word/_rels/comments.xml.rels</c> + the media so the <c>r:embed</c>
/// resolves.</item>
/// </list>
/// </summary>
public class CommentAndChartMediaRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Ct = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace C = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace Cx = "http://schemas.microsoft.com/office/drawing/2014/chartex";

    private const string ChartExRelType = "http://schemas.microsoft.com/office/2014/relationships/chartEx";
    private const string ChartExContentType = "application/vnd.ms-office.chartex+xml";
    private const string ChartRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart";
    private const string ChartContentType = "application/vnd.openxmlformats-officedocument.drawingml.chart+xml";

    // A 1x1 PNG, used as the comment / chart media so the bytes are something concrete to compare.
    private static readonly byte[] PngBytes =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82,
    ];

    private static byte[] WriteBytes(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        return stream.ToArray();
    }

    private static TextDocument ReadDoc(byte[] docx)
    {
        using var stream = new MemoryStream(docx);
        return DocxReader.Read(stream);
    }

    private static byte[] EntryBytes(byte[] docx, string entryPath)
    {
        using var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        using var entry = zip.GetEntry(entryPath)!.Open();
        using var buffer = new MemoryStream();
        entry.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static XDocument EntryXml(byte[] docx, string entryPath) =>
        XDocument.Load(new MemoryStream(EntryBytes(docx, entryPath)));

    private static bool HasEntry(byte[] docx, string entryPath)
    {
        using var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        return zip.GetEntry(entryPath) is not null;
    }

    // --- Case 1: unmodelled chart (chartex) part with media -----------------------------------------

    /// <summary>
    /// Hand-authors a docx whose body has a <c>w:drawing</c> referencing a CHARTEX part (uri/r:id FreeW's chart
    /// reader does not recognise as a <see cref="Chart"/>). The chartex part's own <c>_rels</c> references a
    /// media image. Both are wired through [Content_Types].xml + document.xml.rels exactly as Word emits them.
    /// </summary>
    private static byte[] AuthorChartExPackage()
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            void AddText(string path, string content)
            {
                var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
                using var s = entry.Open();
                var bytes = Encoding.UTF8.GetBytes(content);
                s.Write(bytes, 0, bytes.Length);
            }

            void AddBinary(string path, byte[] content)
            {
                var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
                using var s = entry.Open();
                s.Write(content, 0, content.Length);
            }

            AddText("[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Default Extension="png" ContentType="image/png"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                  <Override PartName="/word/charts/chartEx1.xml" ContentType="application/vnd.ms-office.chartex+xml"/>
                </Types>
                """);

            AddText("_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """);

            AddText("word/_rels/document.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId7" Type="http://schemas.microsoft.com/office/2014/relationships/chartEx" Target="charts/chartEx1.xml"/>
                </Relationships>
                """);

            // A body paragraph whose run carries a w:drawing referencing the chartex part by r:id="rId7".
            AddText("word/document.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                            xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
                            xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"
                            xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                            xmlns:cx="http://schemas.microsoft.com/office/drawing/2014/chartex">
                  <w:body>
                    <w:p><w:r><w:t>Before chart</w:t></w:r></w:p>
                    <w:p>
                      <w:r>
                        <w:drawing>
                          <wp:inline distT="0" distB="0" distL="0" distR="0">
                            <wp:extent cx="5274310" cy="3076575"/>
                            <wp:docPr id="1" name="Chart 1"/>
                            <a:graphic>
                              <a:graphicData uri="http://schemas.microsoft.com/office/drawing/2014/chartex">
                                <cx:chart xmlns:cx="http://schemas.microsoft.com/office/drawing/2014/chartex" r:id="rId7"/>
                              </a:graphicData>
                            </a:graphic>
                          </wp:inline>
                        </w:drawing>
                      </w:r>
                    </w:p>
                    <w:sectPr/>
                  </w:body>
                </w:document>
                """);

            // The chartex part references a media image via its OWN _rels.
            AddText("word/charts/chartEx1.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <cx:chartSpace xmlns:cx="http://schemas.microsoft.com/office/drawing/2014/chartex"
                               xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <cx:chartData><cx:data id="0"/></cx:chartData>
                  <cx:chart>
                    <cx:plotArea>
                      <cx:plotAreaRegion>
                        <cx:series layoutId="waterfall"><cx:dataPt idx="0" r:embed="rId1"/></cx:series>
                      </cx:plotAreaRegion>
                    </cx:plotArea>
                  </cx:chart>
                </cx:chartSpace>
                """);

            AddText("word/charts/_rels/chartEx1.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="../media/chartImage1.png"/>
                </Relationships>
                """);

            AddBinary("word/media/chartImage1.png", PngBytes);
        }
        return stream.ToArray();
    }

    /// <summary>
    /// Moves the unmodelled ChartEx drawing into a header. Unlike the body fixture, the drawing's relationship
    /// belongs to <c>word/_rels/header1.xml.rels</c>, so a round-trip must not recreate it in document.xml.rels.
    /// </summary>
    private static byte[] AuthorHeaderChartExPackage()
    {
        var bodySource = AuthorChartExPackage();
        using var sourceStream = new MemoryStream(bodySource);
        using var source = new ZipArchive(sourceStream, ZipArchiveMode.Read);
        using var output = new MemoryStream();
        using (var destination = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            void AddText(string path, string content)
            {
                var entry = destination.CreateEntry(path, CompressionLevel.Optimal);
                using var stream = entry.Open();
                var bytes = Encoding.UTF8.GetBytes(content);
                stream.Write(bytes, 0, bytes.Length);
            }

            foreach (var sourceEntry in source.Entries)
            {
                if (sourceEntry.FullName is "[Content_Types].xml" or "word/document.xml" or "word/_rels/document.xml.rels")
                    continue;
                var entry = destination.CreateEntry(sourceEntry.FullName, CompressionLevel.Optimal);
                using var input = sourceEntry.Open();
                using var outputEntry = entry.Open();
                input.CopyTo(outputEntry);
            }

            AddText("[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Default Extension="png" ContentType="image/png"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                  <Override PartName="/word/header1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml"/>
                  <Override PartName="/word/charts/chartEx1.xml" ContentType="application/vnd.ms-office.chartex+xml"/>
                </Types>
                """);
            AddText("word/_rels/document.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdHeader1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/header" Target="header1.xml"/>
                </Relationships>
                """);
            AddText("word/document.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                            xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <w:body>
                    <w:p><w:r><w:t>Body text</w:t></w:r></w:p>
                    <w:sectPr><w:headerReference w:type="default" r:id="rIdHeader1"/></w:sectPr>
                  </w:body>
                </w:document>
                """);
            AddText("word/header1.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:hdr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                       xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
                       xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"
                       xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                       xmlns:cx="http://schemas.microsoft.com/office/drawing/2014/chartex">
                  <w:p>
                    <w:r>
                      <w:drawing>
                        <wp:inline distT="0" distB="0" distL="0" distR="0">
                          <wp:extent cx="5274310" cy="3076575"/>
                          <wp:docPr id="1" name="Header Chart"/>
                          <a:graphic>
                            <a:graphicData uri="http://schemas.microsoft.com/office/drawing/2014/chartex">
                              <cx:chart r:id="rId7"/>
                            </a:graphicData>
                          </a:graphic>
                        </wp:inline>
                      </w:drawing>
                    </w:r>
                  </w:p>
                </w:hdr>
                """);
            AddText("word/_rels/header1.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId7" Type="http://schemas.microsoft.com/office/2014/relationships/chartEx" Target="charts/chartEx1.xml"/>
                </Relationships>
                """);
        }
        return output.ToArray();
    }

    [Fact]
    public void UnmodelledChartEx_PreservesPartMediaRelationshipAndContentTypes_Verbatim()
    {
        var source = AuthorChartExPackage();
        var read = ReadDoc(source);

        // The body text either side of the chart is intact, and the chart run survives as a preserved drawing.
        read.PlainText.Should().Contain("Before chart");
        var drawingRuns = read.Blocks.OfType<Paragraph>().SelectMany(p => p.Runs)
            .Where(r => r.PreservedDrawing is not null).ToList();
        drawingRuns.Should().HaveCount(1);

        // The chartex part, its _rels and the media it references were captured as preserved parts.
        read.Preserved.Parts.Select(p => p.PartName).Should().Contain(new[]
        {
            "/word/charts/chartEx1.xml",
            "/word/charts/_rels/chartEx1.xml.rels",
            "/word/media/chartImage1.png",
        });

        var rewritten = WriteBytes(read);

        // The chartex part, its _rels and the media survive byte-for-byte.
        EntryBytes(rewritten, "word/charts/chartEx1.xml").Should().Equal(EntryBytes(source, "word/charts/chartEx1.xml"));
        EntryBytes(rewritten, "word/charts/_rels/chartEx1.xml.rels").Should().Equal(EntryBytes(source, "word/charts/_rels/chartEx1.xml.rels"));
        EntryBytes(rewritten, "word/media/chartImage1.png").Should().Equal(PngBytes);

        // The chartex content-type Override is re-emitted.
        var contentTypes = EntryXml(rewritten, "[Content_Types].xml").Root!;
        var overrides = contentTypes.Elements(Ct + "Override")
            .ToDictionary(o => o.Attribute("PartName")!.Value, o => o.Attribute("ContentType")!.Value);
        overrides["/word/charts/chartEx1.xml"].Should().Be(ChartExContentType);

        // The chart media's png Default is re-emitted (the document has no other png), so the part stays typed.
        var defaults = contentTypes.Elements(Ct + "Default")
            .ToDictionary(d => d.Attribute("Extension")!.Value, d => d.Attribute("ContentType")!.Value);
        defaults.Should().ContainKey("png");
        defaults["png"].Should().Be("image/png");

        // A document→chart relationship is re-emitted (chartEx type, targeting the chart part).
        var rels = EntryXml(rewritten, "word/_rels/document.xml.rels").Root!.Elements(Rel + "Relationship").ToList();
        var chartRel = rels.SingleOrDefault(r => r.Attribute("Type")!.Value == ChartExRelType);
        chartRel.Should().NotBeNull();
        chartRel!.Attribute("Target")!.Value.Should().Be("charts/chartEx1.xml");

        // The inline drawing in the body re-emits verbatim and its cx:chart/@r:id points at that relationship.
        var bodyDrawing = EntryXml(rewritten, "word/document.xml").Descendants(W + "drawing").Single();
        var cxChart = bodyDrawing.Descendants(Cx + "chart").Single();
        cxChart.Attribute(R + "id")!.Value.Should().Be(chartRel.Attribute("Id")!.Value);
    }

    [Fact]
    public void UnmodelledChartEx_SurvivesASecondRoundTrip()
    {
        var once = WriteBytes(ReadDoc(AuthorChartExPackage()));
        var twice = WriteBytes(ReadDoc(once));

        EntryBytes(twice, "word/charts/chartEx1.xml").Should().Equal(EntryBytes(once, "word/charts/chartEx1.xml"));
        EntryBytes(twice, "word/media/chartImage1.png").Should().Equal(PngBytes);
        HasEntry(twice, "word/charts/_rels/chartEx1.xml.rels").Should().BeTrue();
    }

    [Fact]
    public void UnmodelledHeaderChartEx_PreservesPartLocalRelationshipAndContentTypes_Verbatim()
    {
        var source = AuthorHeaderChartExPackage();
        var read = ReadDoc(source);

        var header = read.FinalSectionHeadersFooters.Header;
        header.Should().NotBeNull();
        header!.Paragraphs.SelectMany(paragraph => paragraph.Runs)
            .Should().ContainSingle(run => run.PreservedDrawing != null);
        read.Preserved.Parts.Select(part => part.PartName).Should().Contain(new[]
        {
            "/word/charts/chartEx1.xml",
            "/word/charts/_rels/chartEx1.xml.rels",
            "/word/media/chartImage1.png",
        });

        var rewritten = WriteBytes(read);
        EntryBytes(rewritten, "word/charts/chartEx1.xml").Should().Equal(EntryBytes(source, "word/charts/chartEx1.xml"));
        EntryBytes(rewritten, "word/charts/_rels/chartEx1.xml.rels").Should().Equal(EntryBytes(source, "word/charts/_rels/chartEx1.xml.rels"));
        EntryBytes(rewritten, "word/media/chartImage1.png").Should().Equal(PngBytes);

        var contentTypes = EntryXml(rewritten, "[Content_Types].xml").Root!;
        contentTypes.Elements(Ct + "Override")
            .Single(overrideElement => overrideElement.Attribute("PartName")!.Value == "/word/charts/chartEx1.xml")
            .Attribute("ContentType")!.Value.Should().Be(ChartExContentType);

        var headerRels = EntryXml(rewritten, "word/_rels/header1.xml.rels").Root!.Elements(Rel + "Relationship").ToList();
        var chartRel = headerRels.Single(relationship => relationship.Attribute("Type")!.Value == ChartExRelType);
        chartRel.Attribute("Target")!.Value.Should().Be("charts/chartEx1.xml");

        var headerChart = EntryXml(rewritten, "word/header1.xml").Descendants(Cx + "chart").Single();
        headerChart.Attribute(R + "id")!.Value.Should().Be(chartRel.Attribute("Id")!.Value);
        EntryXml(rewritten, "word/_rels/document.xml.rels").Root!.Elements(Rel + "Relationship")
            .Should().NotContain(relationship => relationship.Attribute("Type")!.Value == ChartExRelType);

        var twice = WriteBytes(ReadDoc(rewritten));
        EntryBytes(twice, "word/charts/chartEx1.xml").Should().Equal(EntryBytes(rewritten, "word/charts/chartEx1.xml"));
        EntryBytes(twice, "word/media/chartImage1.png").Should().Equal(PngBytes);
        HasEntry(twice, "word/_rels/header1.xml.rels").Should().BeTrue();
    }

    private static byte[] AuthorHeaderClassicChartPackage()
    {
        var chartDocument = new TextDocument();
        var chart = new Chart { Kind = ChartKind.Column, Title = "Story chart" };
        chart.Categories.Add("A");
        chart.Series.Add(new ChartSeries { Name = "S", Values = { 1, 2 } });
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromChart(chart));
        chartDocument.Blocks.Add(paragraph);
        var bodyChartSource = WriteBytes(chartDocument);
        var drawing = new XElement(EntryXml(bodyChartSource, "word/document.xml").Descendants(W + "drawing").Single());
        drawing.Descendants(C + "chart").Single().SetAttributeValue(R + "id", "rId7");

        using var sourceStream = new MemoryStream(bodyChartSource);
        using var source = new ZipArchive(sourceStream, ZipArchiveMode.Read);
        using var output = new MemoryStream();
        using (var destination = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            void AddText(string path, string content)
            {
                var entry = destination.CreateEntry(path, CompressionLevel.Optimal);
                using var stream = entry.Open();
                var bytes = Encoding.UTF8.GetBytes(content);
                stream.Write(bytes, 0, bytes.Length);
            }

            foreach (var sourceEntry in source.Entries.Where(entry =>
                         entry.FullName.StartsWith("word/charts/", StringComparison.Ordinal)
                         || entry.FullName.StartsWith("word/embeddings/", StringComparison.Ordinal)))
            {
                var entry = destination.CreateEntry(sourceEntry.FullName, CompressionLevel.Optimal);
                using var input = sourceEntry.Open();
                using var outputEntry = entry.Open();
                input.CopyTo(outputEntry);
            }

            AddText("[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Default Extension="xlsx" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                  <Override PartName="/word/header1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml"/>
                  <Override PartName="/word/charts/chart1.xml" ContentType="application/vnd.openxmlformats-officedocument.drawingml.chart+xml"/>
                </Types>
                """);
            AddText("_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """);
            AddText("word/_rels/document.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdHeader1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/header" Target="header1.xml"/>
                </Relationships>
                """);
            AddText("word/document.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                            xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <w:body><w:p/><w:sectPr><w:headerReference w:type="default" r:id="rIdHeader1"/></w:sectPr></w:body>
                </w:document>
                """);
            AddText("word/header1.xml",
                $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:hdr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                       xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <w:p><w:r>{drawing.ToString(SaveOptions.DisableFormatting)}</w:r></w:p>
                </w:hdr>
                """);
            AddText("word/_rels/header1.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId7" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart" Target="charts/chart1.xml"/>
                </Relationships>
                """);
        }
        return output.ToArray();
    }

    [Fact]
    public void ModelledHeaderChart_PreservesItsPartLocalRelationshipInsteadOfBecomingABodyChart()
    {
        var source = AuthorHeaderClassicChartPackage();
        var read = ReadDoc(source);
        var runs = read.FinalSectionHeadersFooters.Header!.Paragraphs.SelectMany(paragraph => paragraph.Runs).ToList();
        runs.Should().ContainSingle(run => run.PreservedDrawing != null);
        runs.Should().NotContain(run => run.Chart != null);

        var rewritten = WriteBytes(read);
        EntryBytes(rewritten, "word/charts/chart1.xml").Should().Equal(EntryBytes(source, "word/charts/chart1.xml"));
        EntryBytes(rewritten, "word/charts/_rels/chart1.xml.rels").Should().Equal(EntryBytes(source, "word/charts/_rels/chart1.xml.rels"));
        var headerRels = EntryXml(rewritten, "word/_rels/header1.xml.rels").Root!.Elements(Rel + "Relationship").ToList();
        var chartRel = headerRels.Single(relationship => relationship.Attribute("Type")!.Value == ChartRelType);
        chartRel.Attribute("Target")!.Value.Should().Be("charts/chart1.xml");
        EntryXml(rewritten, "word/header1.xml").Descendants(C + "chart").Single()
            .Attribute(R + "id")!.Value.Should().Be(chartRel.Attribute("Id")!.Value);
        EntryXml(rewritten, "word/_rels/document.xml.rels").Root!.Elements(Rel + "Relationship")
            .Should().NotContain(relationship => relationship.Attribute("Type")!.Value == ChartRelType);
    }

    private static byte[] AuthorNoteChartExPackage(string partName, string noteName, string referenceName)
    {
        var bodySource = AuthorChartExPackage();
        using var sourceStream = new MemoryStream(bodySource);
        using var source = new ZipArchive(sourceStream, ZipArchiveMode.Read);
        using var output = new MemoryStream();
        using (var destination = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            void AddText(string path, string content)
            {
                var entry = destination.CreateEntry(path, CompressionLevel.Optimal);
                using var stream = entry.Open();
                var bytes = Encoding.UTF8.GetBytes(content);
                stream.Write(bytes, 0, bytes.Length);
            }

            foreach (var sourceEntry in source.Entries)
            {
                if (sourceEntry.FullName is "[Content_Types].xml" or "word/document.xml" or "word/_rels/document.xml.rels")
                    continue;
                var entry = destination.CreateEntry(sourceEntry.FullName, CompressionLevel.Optimal);
                using var input = sourceEntry.Open();
                using var outputEntry = entry.Open();
                input.CopyTo(outputEntry);
            }

            var contentType = partName == "footnotes"
                ? "application/vnd.openxmlformats-officedocument.wordprocessingml.footnotes+xml"
                : "application/vnd.openxmlformats-officedocument.wordprocessingml.endnotes+xml";
            var relationshipType = partName == "footnotes"
                ? "http://schemas.openxmlformats.org/officeDocument/2006/relationships/footnotes"
                : "http://schemas.openxmlformats.org/officeDocument/2006/relationships/endnotes";
            AddText("[Content_Types].xml",
                $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Default Extension="png" ContentType="image/png"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                  <Override PartName="/word/{partName}.xml" ContentType="{contentType}"/>
                  <Override PartName="/word/charts/chartEx1.xml" ContentType="application/vnd.ms-office.chartex+xml"/>
                </Types>
                """);
            AddText("word/_rels/document.xml.rels",
                $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdNotes" Type="{relationshipType}" Target="{partName}.xml"/>
                </Relationships>
                """);
            AddText("word/document.xml",
                $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                            xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <w:body>
                    <w:p><w:r><w:{referenceName} w:id="1"/></w:r></w:p>
                    <w:sectPr/>
                  </w:body>
                </w:document>
                """);
            AddText($"word/{partName}.xml",
                $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:{partName} xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                             xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
                             xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"
                             xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                             xmlns:cx="http://schemas.microsoft.com/office/drawing/2014/chartex">
                  <w:{noteName} w:id="1">
                    <w:p>
                      <w:r><w:{referenceName}/></w:r>
                      <w:r>
                        <w:drawing>
                          <wp:inline distT="0" distB="0" distL="0" distR="0">
                            <wp:extent cx="5274310" cy="3076575"/>
                            <wp:docPr id="1" name="Note Chart"/>
                            <a:graphic><a:graphicData uri="http://schemas.microsoft.com/office/drawing/2014/chartex"><cx:chart r:id="rId7"/></a:graphicData></a:graphic>
                          </wp:inline>
                        </w:drawing>
                      </w:r>
                    </w:p>
                  </w:{noteName}>
                </w:{partName}>
                """);
            AddText($"word/_rels/{partName}.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId7" Type="http://schemas.microsoft.com/office/2014/relationships/chartEx" Target="charts/chartEx1.xml"/>
                </Relationships>
                """);
        }
        return output.ToArray();
    }

    [Theory]
    [InlineData("footnotes", "footnote", "footnoteRef")]
    [InlineData("endnotes", "endnote", "endnoteRef")]
    public void UnmodelledNoteChartEx_PreservesPartLocalRelationshipAndContentTypes_Verbatim(
        string partName,
        string noteName,
        string referenceName)
    {
        var source = AuthorNoteChartExPackage(partName, noteName, referenceName);
        var read = ReadDoc(source);
        var content = partName == "footnotes" ? read.Footnotes[1].Content : read.Endnotes[1].Content;
        content.SelectMany(paragraph => paragraph.Runs)
            .Should().ContainSingle(run => run.PreservedDrawing != null);

        var rewritten = WriteBytes(read);
        EntryBytes(rewritten, "word/charts/chartEx1.xml").Should().Equal(EntryBytes(source, "word/charts/chartEx1.xml"));
        EntryBytes(rewritten, "word/charts/_rels/chartEx1.xml.rels").Should().Equal(EntryBytes(source, "word/charts/_rels/chartEx1.xml.rels"));
        EntryBytes(rewritten, "word/media/chartImage1.png").Should().Equal(PngBytes);

        var noteRels = EntryXml(rewritten, $"word/_rels/{partName}.xml.rels").Root!.Elements(Rel + "Relationship").ToList();
        var chartRel = noteRels.Single(relationship => relationship.Attribute("Type")!.Value == ChartExRelType);
        chartRel.Attribute("Target")!.Value.Should().Be("charts/chartEx1.xml");
        EntryXml(rewritten, $"word/{partName}.xml").Descendants(Cx + "chart").Single()
            .Attribute(R + "id")!.Value.Should().Be(chartRel.Attribute("Id")!.Value);
        EntryXml(rewritten, "word/_rels/document.xml.rels").Root!.Elements(Rel + "Relationship")
            .Should().NotContain(relationship => relationship.Attribute("Type")!.Value == ChartExRelType);

        var twice = WriteBytes(ReadDoc(rewritten));
        EntryBytes(twice, "word/charts/chartEx1.xml").Should().Equal(EntryBytes(rewritten, "word/charts/chartEx1.xml"));
        HasEntry(twice, $"word/_rels/{partName}.xml.rels").Should().BeTrue();
    }

    // --- Case 2: comment-part image -----------------------------------------------------------------

    /// <summary>
    /// Hand-authors a docx with a single comment whose paragraph carries an inline image referenced from
    /// word/_rels/comments.xml.rels (the comment part's own relationships, NOT document.xml.rels).
    /// </summary>
    private static byte[] AuthorCommentImagePackage()
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            void AddText(string path, string content)
            {
                var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
                using var s = entry.Open();
                var bytes = Encoding.UTF8.GetBytes(content);
                s.Write(bytes, 0, bytes.Length);
            }

            void AddBinary(string path, byte[] content)
            {
                var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
                using var s = entry.Open();
                s.Write(content, 0, content.Length);
            }

            AddText("[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Default Extension="png" ContentType="image/png"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                  <Override PartName="/word/comments.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.comments+xml"/>
                </Types>
                """);

            AddText("_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """);

            AddText("word/_rels/document.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdC" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments" Target="comments.xml"/>
                </Relationships>
                """);

            // A body paragraph the comment brackets, plus its anchor reference.
            AddText("word/document.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                            xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <w:body>
                    <w:p>
                      <w:commentRangeStart w:id="0"/>
                      <w:r><w:t>Reviewed text</w:t></w:r>
                      <w:commentRangeEnd w:id="0"/>
                      <w:r><w:commentReference w:id="0"/></w:r>
                    </w:p>
                    <w:sectPr/>
                  </w:body>
                </w:document>
                """);

            // The comment carries a paragraph with text AND an inline image (r:embed="rId1" → comments rels).
            AddText("word/comments.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:comments xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                            xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
                            xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"
                            xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                            xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture">
                  <w:comment w:id="0" w:author="Reviewer" w:initials="RV" w:date="2026-01-01T00:00:00Z">
                    <w:p>
                      <w:r><w:t>See image:</w:t></w:r>
                      <w:r>
                        <w:drawing>
                          <wp:inline distT="0" distB="0" distL="0" distR="0">
                            <wp:extent cx="190500" cy="190500"/>
                            <wp:docPr id="1" name="CommentPic"/>
                            <a:graphic>
                              <a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture">
                                <pic:pic>
                                  <pic:nvPicPr>
                                    <pic:cNvPr id="1" name="CommentPic"/>
                                    <pic:cNvPicPr/>
                                  </pic:nvPicPr>
                                  <pic:blipFill><a:blip r:embed="rId1"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>
                                  <pic:spPr>
                                    <a:xfrm><a:off x="0" y="0"/><a:ext cx="190500" cy="190500"/></a:xfrm>
                                    <a:prstGeom prst="rect"><a:avLst/></a:prstGeom>
                                  </pic:spPr>
                                </pic:pic>
                              </a:graphicData>
                            </a:graphic>
                          </wp:inline>
                        </w:drawing>
                      </w:r>
                    </w:p>
                  </w:comment>
                </w:comments>
                """);

            AddText("word/_rels/comments.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/commentMedia1.png"/>
                </Relationships>
                """);

            AddBinary("word/media/commentMedia1.png", PngBytes);
        }
        return stream.ToArray();
    }

    [Fact]
    public void CommentImage_IsModelledAsRunImage_AndReEmittedWithRelAndMedia()
    {
        var read = ReadDoc(AuthorCommentImagePackage());

        // The comment was read with its text AND a real Run.Image.
        read.Comments.Should().ContainKey(0);
        var commentRuns = read.Comments[0].Content.SelectMany(p => p.Runs).ToList();
        commentRuns.Should().Contain(r => r.Text == "See image:");
        var imageRun = commentRuns.SingleOrDefault(r => r.Image is not null);
        imageRun.Should().NotBeNull();
        imageRun!.Image!.Bytes.Should().Equal(PngBytes);

        var rewritten = WriteBytes(read);

        // comments.xml.rels is re-emitted with an image relationship, and the media part exists.
        HasEntry(rewritten, "word/_rels/comments.xml.rels").Should().BeTrue();
        var commentRels = EntryXml(rewritten, "word/_rels/comments.xml.rels").Root!.Elements(Rel + "Relationship").ToList();
        commentRels.Should().ContainSingle(r => r.Attribute("Type")!.Value.EndsWith("/image"));
        var mediaTarget = commentRels.Single().Attribute("Target")!.Value; // e.g. media/comment_image1.png
        EntryBytes(rewritten, "word/" + mediaTarget).Should().Equal(PngBytes);

        // The re-authored comments.xml references that media via a w:drawing whose blip r:embed matches the rel.
        var blip = EntryXml(rewritten, "word/comments.xml")
            .Descendants(XName.Get("blip", "http://schemas.openxmlformats.org/drawingml/2006/main")).Single();
        blip.Attribute(R + "embed")!.Value.Should().Be(commentRels.Single().Attribute("Id")!.Value);

        // The image survives a re-read of our own output.
        var reread = ReadDoc(rewritten);
        reread.Comments[0].Content.SelectMany(p => p.Runs)
            .Single(r => r.Image is not null).Image!.Bytes.Should().Equal(PngBytes);
    }

    private static byte[] AuthorCommentChartExPackage()
    {
        var bodySource = AuthorChartExPackage();
        using var sourceStream = new MemoryStream(bodySource);
        using var source = new ZipArchive(sourceStream, ZipArchiveMode.Read);
        using var output = new MemoryStream();
        using (var destination = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            void AddText(string path, string content)
            {
                var entry = destination.CreateEntry(path, CompressionLevel.Optimal);
                using var stream = entry.Open();
                var bytes = Encoding.UTF8.GetBytes(content);
                stream.Write(bytes, 0, bytes.Length);
            }

            foreach (var sourceEntry in source.Entries)
            {
                if (sourceEntry.FullName is "[Content_Types].xml" or "word/document.xml" or "word/_rels/document.xml.rels")
                    continue;
                var entry = destination.CreateEntry(sourceEntry.FullName, CompressionLevel.Optimal);
                using var input = sourceEntry.Open();
                using var outputEntry = entry.Open();
                input.CopyTo(outputEntry);
            }

            AddText("[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Default Extension="png" ContentType="image/png"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                  <Override PartName="/word/comments.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.comments+xml"/>
                  <Override PartName="/word/charts/chartEx1.xml" ContentType="application/vnd.ms-office.chartex+xml"/>
                </Types>
                """);
            AddText("word/_rels/document.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdC" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments" Target="comments.xml"/>
                </Relationships>
                """);
            AddText("word/document.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                            xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <w:body>
                    <w:p><w:commentRangeStart w:id="0"/><w:r><w:t>Reviewed text</w:t></w:r><w:commentRangeEnd w:id="0"/><w:r><w:commentReference w:id="0"/></w:r></w:p>
                    <w:sectPr/>
                  </w:body>
                </w:document>
                """);
            AddText("word/comments.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:comments xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                            xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
                            xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"
                            xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                            xmlns:cx="http://schemas.microsoft.com/office/drawing/2014/chartex">
                  <w:comment w:id="0" w:author="Reviewer" w:initials="RV">
                    <w:p><w:r><w:drawing><wp:inline distT="0" distB="0" distL="0" distR="0"><wp:extent cx="5274310" cy="3076575"/><wp:docPr id="1" name="Comment Chart"/><a:graphic><a:graphicData uri="http://schemas.microsoft.com/office/drawing/2014/chartex"><cx:chart r:id="rId7"/></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>
                  </w:comment>
                </w:comments>
                """);
            AddText("word/_rels/comments.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId7" Type="http://schemas.microsoft.com/office/2014/relationships/chartEx" Target="charts/chartEx1.xml"/>
                </Relationships>
                """);
        }
        return output.ToArray();
    }

    [Fact]
    public void UnmodelledCommentChartEx_PreservesPartLocalRelationshipAndContentTypes_Verbatim()
    {
        var source = AuthorCommentChartExPackage();
        var read = ReadDoc(source);
        read.Comments[0].Content.SelectMany(paragraph => paragraph.Runs)
            .Should().ContainSingle(run => run.PreservedDrawing != null);

        var rewritten = WriteBytes(read);
        EntryBytes(rewritten, "word/charts/chartEx1.xml").Should().Equal(EntryBytes(source, "word/charts/chartEx1.xml"));
        EntryBytes(rewritten, "word/charts/_rels/chartEx1.xml.rels").Should().Equal(EntryBytes(source, "word/charts/_rels/chartEx1.xml.rels"));
        EntryBytes(rewritten, "word/media/chartImage1.png").Should().Equal(PngBytes);

        var commentRels = EntryXml(rewritten, "word/_rels/comments.xml.rels").Root!.Elements(Rel + "Relationship").ToList();
        var chartRel = commentRels.Single(relationship => relationship.Attribute("Type")!.Value == ChartExRelType);
        chartRel.Attribute("Target")!.Value.Should().Be("charts/chartEx1.xml");
        EntryXml(rewritten, "word/comments.xml").Descendants(Cx + "chart").Single()
            .Attribute(R + "id")!.Value.Should().Be(chartRel.Attribute("Id")!.Value);
        EntryXml(rewritten, "word/_rels/document.xml.rels").Root!.Elements(Rel + "Relationship")
            .Should().NotContain(relationship => relationship.Attribute("Type")!.Value == ChartExRelType);

        var twice = WriteBytes(ReadDoc(rewritten));
        EntryBytes(twice, "word/charts/chartEx1.xml").Should().Equal(EntryBytes(rewritten, "word/charts/chartEx1.xml"));
        HasEntry(twice, "word/_rels/comments.xml.rels").Should().BeTrue();
    }

    // --- Regression: normal media + a FreeW chart + a text-only comment round-trip as today ----------

    [Fact]
    public void NormalImageChartAndTextComment_RoundTripWithNoSpuriousPreservedParts()
    {
        var doc = new TextDocument();

        // A normal body image.
        var bodyPara = new Paragraph("Body with image: ");
        bodyPara.Runs.Add(Run.FromImage(new InlineImage(PngBytes, 50, 50, ImageFormat.Png)));
        doc.Blocks.Add(bodyPara);

        // A FreeW-modelled chart.
        var chart = new Chart { Kind = ChartKind.Column, Title = "Q" };
        chart.Categories.Add("A");
        chart.Series.Add(new ChartSeries { Name = "S", Values = { 1, 2 } });
        var chartPara = new Paragraph();
        chartPara.Runs.Add(Run.FromChart(chart));
        doc.Blocks.Add(chartPara);

        // A text-only comment.
        doc.Comments[0] = new Comment(0, "Looks good", "Rev", "RV");

        var bytes = WriteBytes(doc);

        // No comment rels/media are emitted for a text-only comment.
        HasEntry(bytes, "word/_rels/comments.xml.rels").Should().BeFalse();
        // The FreeW chart part is emitted (NOT preserved-as-chartex).
        HasEntry(bytes, "word/charts/chart1.xml").Should().BeTrue();

        var read = ReadDoc(bytes);

        // No spurious preserved parts: an authored document captures nothing.
        read.Preserved.Parts.Should().BeEmpty();
        read.Preserved.IsEmpty.Should().BeTrue();

        // Modelled content survives: body image, FreeW chart, the comment.
        read.Blocks.OfType<Paragraph>().SelectMany(p => p.Runs).Count(r => r.Image is not null).Should().Be(1);
        read.Blocks.OfType<Paragraph>().SelectMany(p => p.Runs).Count(r => r.Chart is not null).Should().Be(1);
        read.Comments.Should().ContainKey(0);
        read.Comments[0].PlainText.Should().Be("Looks good");

        // No run in the document was misclassified as a preserved drawing.
        read.Blocks.OfType<Paragraph>().SelectMany(p => p.Runs)
            .Any(r => r.PreservedDrawing is not null).Should().BeFalse();
    }

    [Fact]
    public void ModelledImageAndChart_AvoidPreservedPackagePartNameCollisions()
    {
        var doc = new TextDocument();
        doc.Preserved.ContentTypeDefaults["png"] = "image/png";
        doc.Preserved.Parts.Add(new PreservedPart("/word/media/image1.png", PngBytes));
        doc.Preserved.Parts.Add(new PreservedPart("/word/embeddings/Microsoft_Excel_Worksheet1.xlsx", Encoding.UTF8.GetBytes("preserved workbook")));
        doc.Preserved.Parts.Add(new PreservedPart(
            "/word/charts/chart1.xml",
            Encoding.UTF8.GetBytes("<c:chartSpace xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\"/>"),
            ChartContentType,
            ChartRelType));
        doc.Preserved.Parts.Add(new PreservedPart(
            "/word/charts/_rels/chart1.xml.rels",
            Encoding.UTF8.GetBytes("""
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"/>
                """)));

        var imagePara = new Paragraph("Image ");
        imagePara.Runs.Add(Run.FromImage(new InlineImage(PngBytes, 50, 50, ImageFormat.Png)));
        doc.Blocks.Add(imagePara);
        var chart = new Chart { Kind = ChartKind.Column, Title = "Modelled" };
        chart.Categories.Add("A");
        chart.Series.Add(new ChartSeries { Name = "S", Values = { 1 } });
        var chartPara = new Paragraph();
        chartPara.Runs.Add(Run.FromChart(chart));
        doc.Blocks.Add(chartPara);

        var bytes = WriteBytes(doc);

        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var names = zip.Entries.Select(entry => entry.FullName).ToList();
        names.Count(name => name == "word/media/image1.png").Should().Be(1);
        names.Count(name => name == "word/media/image2.png").Should().Be(1);
        names.Count(name => name == "word/charts/chart1.xml").Should().Be(1);
        names.Count(name => name == "word/charts/chart2.xml").Should().Be(1);
        names.Count(name => name == "word/charts/_rels/chart1.xml.rels").Should().Be(1);
        names.Count(name => name == "word/charts/_rels/chart2.xml.rels").Should().Be(1);
        names.Count(name => name == "word/embeddings/Microsoft_Excel_Worksheet1.xlsx").Should().Be(1);
        names.Count(name => name == "word/embeddings/Microsoft_Excel_Worksheet2.xlsx").Should().Be(1);

        var relTargets = EntryXml(bytes, "word/_rels/document.xml.rels")
            .Root!.Elements(Rel + "Relationship")
            .Select(rel => rel.Attribute("Target")!.Value)
            .ToList();
        relTargets.Should().Contain("media/image2.png");
        relTargets.Should().Contain("charts/chart1.xml");
        relTargets.Should().Contain("charts/chart2.xml");

        var chartRelTargets = EntryXml(bytes, "word/charts/_rels/chart2.xml.rels")
            .Root!.Elements(Rel + "Relationship")
            .Select(rel => rel.Attribute("Target")!.Value)
            .ToList();
        chartRelTargets.Should().Contain("../embeddings/Microsoft_Excel_Worksheet2.xlsx");

        var overrides = EntryXml(bytes, "[Content_Types].xml")
            .Root!.Elements(Ct + "Override")
            .Select(overrideElement => overrideElement.Attribute("PartName")!.Value)
            .ToList();
        overrides.Should().Contain("/word/charts/chart1.xml");
        overrides.Should().Contain("/word/charts/chart2.xml");
    }

    [Fact]
    public void ModelledOleHeaderAndCommentMedia_AvoidPreservedPackagePartNameCollisions()
    {
        var doc = new TextDocument();
        doc.Preserved.ContentTypeDefaults["png"] = "image/png";
        doc.Preserved.Parts.Add(new PreservedPart("/word/embeddings/oleObject1.bin", Encoding.UTF8.GetBytes("preserved ole")));
        doc.Preserved.Parts.Add(new PreservedPart("/word/media/image1.png", PngBytes));
        doc.Preserved.Parts.Add(new PreservedPart("/word/media/header1_image1.png", PngBytes));
        doc.Preserved.Parts.Add(new PreservedPart("/word/media/comment_image1.png", PngBytes));

        var oleParagraph = new Paragraph();
        oleParagraph.Runs.Add(Run.FromEmbeddedObject(EmbeddedObject.Create(
            Encoding.UTF8.GetBytes("modelled ole"),
            "Excel.Sheet.12",
            new InlineImage(PngBytes, 24, 24, ImageFormat.Png))));
        doc.Blocks.Add(oleParagraph);

        doc.Header = new HeaderFooter();
        doc.Header.Paragraphs.Add(new Paragraph());
        doc.Header.Paragraphs[0].Runs.Add(Run.FromImage(new InlineImage(PngBytes, 18, 18, ImageFormat.Png)));

        var comment = new Comment(0, "Image comment", "Rev", "RV");
        comment.Content[0].Runs.Add(Run.FromImage(new InlineImage(PngBytes, 16, 16, ImageFormat.Png)));
        doc.Comments[0] = comment;

        var bytes = WriteBytes(doc);

        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var names = zip.Entries.Select(entry => entry.FullName).ToList();
        names.Count(name => name == "word/embeddings/oleObject1.bin").Should().Be(1);
        names.Count(name => name == "word/embeddings/oleObject2.bin").Should().Be(1);
        names.Count(name => name == "word/media/image1.png").Should().Be(1);
        names.Count(name => name == "word/media/image2.png").Should().Be(1);
        names.Count(name => name == "word/media/header1_image1.png").Should().Be(1);
        names.Count(name => name == "word/media/header1_image2.png").Should().Be(1);
        names.Count(name => name == "word/media/comment_image1.png").Should().Be(1);
        names.Count(name => name == "word/media/comment_image2.png").Should().Be(1);

        var documentRelTargets = EntryXml(bytes, "word/_rels/document.xml.rels")
            .Root!.Elements(Rel + "Relationship")
            .Select(rel => rel.Attribute("Target")!.Value)
            .ToList();
        documentRelTargets.Should().Contain("embeddings/oleObject2.bin");
        documentRelTargets.Should().Contain("media/image2.png");

        var headerRelTargets = EntryXml(bytes, "word/_rels/header1.xml.rels")
            .Root!.Elements(Rel + "Relationship")
            .Select(rel => rel.Attribute("Target")!.Value)
            .ToList();
        headerRelTargets.Should().Contain("media/header1_image2.png");

        var commentRelTargets = EntryXml(bytes, "word/_rels/comments.xml.rels")
            .Root!.Elements(Rel + "Relationship")
            .Select(rel => rel.Attribute("Target")!.Value)
            .ToList();
        commentRelTargets.Should().Contain("media/comment_image2.png");
    }
}
