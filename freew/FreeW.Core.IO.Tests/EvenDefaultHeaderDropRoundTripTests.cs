using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Regression coverage for the "headers dropped" round-trip bug seen against the real-world
/// <c>PageSpecificHeadFoot.docx</c> corpus document. That document has a single w:sectPr whose
/// header/footer references appear in <c>even</c>-before-<c>default</c> order (the even reference's
/// r:id is numerically lower than the default's) plus <c>w:evenAndOddHeaders</c> in settings.xml.
///
/// The bug: after Read → Write → re-read the round-tripped package kept both footer parts but dropped
/// BOTH header parts entirely. These tests build the same shape (both directly as a model and as a
/// hand-built package matching the corpus) and assert all four header/footer slots survive.
/// </summary>
public class EvenDefaultHeaderDropRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private static (TextDocument read, ZipArchive zip, MemoryStream stream) WriteAndReopen(TextDocument document)
    {
        var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        var read = DocxReader.Read(stream);
        stream.Position = 0;
        var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        return (read, zip, stream);
    }

    [Fact]
    public void DistinctDefaultAndEvenHeadersAndFooters_AllFourSurviveRoundTrip()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body"));
        doc.Page.DifferentOddEvenPages = true;
        doc.Header = new HeaderFooter("ODD Page Header text");
        doc.EvenHeader = new HeaderFooter("This is an Even Page, with a Header");
        doc.Footer = new HeaderFooter("Footer Middle");
        doc.EvenFooter = new HeaderFooter("This is a simple footer on the second page");

        var (read, zip, stream) = WriteAndReopen(doc);
        using (stream)
        using (zip)
        {
            // The package must keep BOTH header parts AND BOTH footer parts.
            zip.GetEntry("word/header1.xml").Should().NotBeNull("the default header part must be written");
            zip.GetEntry("word/header2.xml").Should().NotBeNull("the even header part must be written");
            zip.GetEntry("word/footer1.xml").Should().NotBeNull("the default footer part must be written");
            zip.GetEntry("word/footer2.xml").Should().NotBeNull("the even footer part must be written");

            var sectPr = LoadDocument(zip).Root!.Element(W + "body")!.Element(W + "sectPr")!;
            sectPr.Elements(W + "headerReference").Should().Contain(r => r.Attribute(W + "type")!.Value == "default");
            sectPr.Elements(W + "headerReference").Should().Contain(r => r.Attribute(W + "type")!.Value == "even");
            sectPr.Elements(W + "footerReference").Should().Contain(r => r.Attribute(W + "type")!.Value == "default");
            sectPr.Elements(W + "footerReference").Should().Contain(r => r.Attribute(W + "type")!.Value == "even");
        }

        read.Header!.PlainText.Should().Be("ODD Page Header text");
        read.EvenHeader!.PlainText.Should().Be("This is an Even Page, with a Header");
        read.Footer!.PlainText.Should().Be("Footer Middle");
        read.EvenFooter!.PlainText.Should().Be("This is a simple footer on the second page");
    }

    [Fact]
    public void CorpusShapedPackage_EvenBeforeDefault_ReadsAllFourAndWriteKeepsThem()
    {
        // Hand-build a package matching PageSpecificHeadFoot.docx: even-before-default header/footer
        // references with the even ref's r:id numerically lower than the default ref's, plus the
        // w:evenAndOddHeaders toggle in settings.xml.
        var source = BuildCorpusShapedPackage();
        source.Position = 0;
        var model = DocxReader.Read(source);

        // The READ path must recover all four slots from the even-before-default sectPr. The headers wrap
        // their text in a table, so the recovered (flattened) header carries the cell text plus the trailing
        // empty direct-child paragraph — assert the text is present (Contain) and the part is non-empty.
        model.Header.Should().NotBeNull();
        model.Header!.IsEmpty.Should().BeFalse();
        model.Header!.PlainText.Should().Contain("ODD Page Header text");
        model.EvenHeader.Should().NotBeNull();
        model.EvenHeader!.IsEmpty.Should().BeFalse();
        model.EvenHeader!.PlainText.Should().Contain("Even Page Header text");
        model.Footer.Should().NotBeNull();
        model.Footer!.PlainText.Should().Be("Default footer text");
        model.EvenFooter.Should().NotBeNull();
        model.EvenFooter!.PlainText.Should().Be("Even footer text");
        model.Page.DifferentOddEvenPages.Should().BeTrue();

        // And the WRITE path must keep all four header/footer parts.
        var (read, zip, stream) = WriteAndReopen(model);
        using (stream)
        using (zip)
        {
            zip.Entries.Count(e => e.FullName.StartsWith("word/header") && e.FullName.EndsWith(".xml"))
                .Should().Be(2, "both default and even header parts must be written");
            zip.Entries.Count(e => e.FullName.StartsWith("word/footer") && e.FullName.EndsWith(".xml"))
                .Should().Be(2, "both default and even footer parts must be written");
        }

        read.Header!.PlainText.Should().Contain("ODD Page Header text");
        read.EvenHeader!.PlainText.Should().Contain("Even Page Header text");
        read.Footer!.PlainText.Should().Be("Default footer text");
        read.EvenFooter!.PlainText.Should().Be("Even footer text");
    }

    private static XDocument LoadDocument(ZipArchive zip)
    {
        using var s = zip.GetEntry("word/document.xml")!.Open();
        return XDocument.Load(s);
    }

    private static MemoryStream BuildCorpusShapedPackage()
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, string xml)
            {
                var entry = zip.CreateEntry(path);
                using var w = new StreamWriter(entry.Open());
                w.Write(xml);
            }

            Add("[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                  <Override PartName="/word/settings.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml"/>
                  <Override PartName="/word/header1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml"/>
                  <Override PartName="/word/header2.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml"/>
                  <Override PartName="/word/footer1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml"/>
                  <Override PartName="/word/footer2.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml"/>
                </Types>
                """);

            Add("_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """);

            // Even refs (rId7/rId9) precede default refs (rId8/rId10); even r:id numerically lower.
            Add("word/_rels/document.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings" Target="settings.xml"/>
                  <Relationship Id="rId8" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/header" Target="header2.xml"/>
                  <Relationship Id="rId7" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/header" Target="header1.xml"/>
                  <Relationship Id="rId10" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer" Target="footer2.xml"/>
                  <Relationship Id="rId9" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer" Target="footer1.xml"/>
                </Relationships>
                """);

            // header1.xml = even, header2.xml = default; footer1.xml = even, footer2.xml = default.
            // Headers wrap their text inside a w:tbl (exactly as the real corpus does) so the visible text
            // lives in a paragraph NESTED in a table cell, with only a trailing empty paragraph as a direct
            // child of w:hdr. This is what made the old reader (direct-child w:p only) see the headers as
            // empty and the writer drop them. Footers keep their text in a direct-child paragraph (the corpus
            // footers do too), so they survived even on the buggy code.
            Add("word/header1.xml", TableWrappedHeaderXml("Even Page Header text"));
            Add("word/header2.xml", TableWrappedHeaderXml("ODD Page Header text"));
            Add("word/footer1.xml", HeaderFooterXml("ftr", "Even footer text"));
            Add("word/footer2.xml", HeaderFooterXml("ftr", "Default footer text"));

            Add("word/settings.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:settings xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:evenAndOddHeaders/>
                </w:settings>
                """);

            Add("word/document.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                            xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <w:body>
                    <w:p><w:r><w:t>Body</w:t></w:r></w:p>
                    <w:sectPr>
                      <w:headerReference w:type="even" r:id="rId7"/>
                      <w:headerReference w:type="default" r:id="rId8"/>
                      <w:footerReference w:type="even" r:id="rId9"/>
                      <w:footerReference w:type="default" r:id="rId10"/>
                      <w:pgSz w:w="12240" w:h="15840"/>
                      <w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440" w:header="708" w:footer="708" w:gutter="0"/>
                      <w:cols w:space="708"/>
                    </w:sectPr>
                  </w:body>
                </w:document>
                """);
        }
        return stream;
    }

    private static string HeaderFooterXml(string root, string text) =>
        $"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:{root} xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
          <w:p><w:r><w:t>{text}</w:t></w:r></w:p>
        </w:{root}>
        """;

    // A header whose only text lives in a paragraph nested inside a table cell, with a trailing empty
    // direct-child paragraph — the shape of the real PageSpecificHeadFoot.docx headers.
    private static string TableWrappedHeaderXml(string text) =>
        $"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:hdr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
          <w:tbl>
            <w:tblPr><w:tblW w:w="5000" w:type="pct"/></w:tblPr>
            <w:tblGrid><w:gridCol w:w="9590"/></w:tblGrid>
            <w:tr>
              <w:tc>
                <w:tcPr><w:tcW w:w="5000" w:type="pct"/></w:tcPr>
                <w:p><w:r><w:t>{text}</w:t></w:r></w:p>
              </w:tc>
            </w:tr>
          </w:tbl>
          <w:p><w:pPr><w:pStyle w:val="Header"/></w:pPr></w:p>
        </w:hdr>
        """;
}
