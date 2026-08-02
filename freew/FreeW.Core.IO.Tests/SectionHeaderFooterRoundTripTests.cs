using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FreeW.Core.Model;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-trip coverage for per-section headers/footers (Task A): each WordprocessingML section can carry
/// its own default/even/first header &amp; footer parts, so multi-section documents and page-specific
/// (first-page) headers/footers survive a write→read cycle instead of collapsing onto one document-level
/// header. A single-section, header-less document must still round-trip byte/structure-equivalently.
/// </summary>
public class SectionHeaderFooterRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

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

    /// <summary>Returns the set of entry paths in a written package, for structure comparison.</summary>
    private static List<string> EntryNames(byte[] docx)
    {
        using var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        return zip.Entries.Select(e => e.FullName).OrderBy(n => n).ToList();
    }

    private static XDocument LoadEntry(byte[] docx, string path)
    {
        using var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        var entry = zip.GetEntry(path)!;
        using var s = entry.Open();
        return XDocument.Load(s);
    }

    // --- A 2-section document with distinct default headers/footers per section ---------------------

    [Fact]
    public void TwoSections_EachHaveDistinctHeadersAndFooters()
    {
        var doc = new TextDocument();
        // Section 1 ends on this paragraph (carries its own page setup + header/footer set).
        var section1 = new Section(new PageSettings(), SectionBreakKind.NextPage);
        section1.HeadersFooters.Header = new HeaderFooter("Section 1 header");
        section1.HeadersFooters.Footer = new HeaderFooter("Section 1 footer");
        doc.Blocks.Add(new Paragraph("Body of section 1") { SectionBreak = section1 });

        // Section 2 is the final (body-level) section: its headers/footers are the document-level views.
        doc.Blocks.Add(new Paragraph("Body of section 2"));
        doc.Header = new HeaderFooter("Section 2 header");
        doc.Footer = new HeaderFooter("Section 2 footer");

        var read = RoundTrip(doc);

        // The final section's headers/footers survive on the document-level views.
        read.Header!.PlainText.Should().Be("Section 2 header");
        read.Footer!.PlainText.Should().Be("Section 2 footer");

        // Section 1's header/footer survive on its own section, distinct from section 2's.
        var sections = read.Sections;
        sections.Should().HaveCount(2);
        var s1 = sections[0];
        s1.HeadersFooters.Header!.PlainText.Should().Be("Section 1 header");
        s1.HeadersFooters.Footer!.PlainText.Should().Be("Section 1 footer");

        // Section 1's header is NOT applied to section 2 (the final section).
        var s2 = sections[1];
        s2.HeadersFooters.Header!.PlainText.Should().Be("Section 2 header");
        s2.HeadersFooters.Header!.PlainText.Should().NotBe(s1.HeadersFooters.Header!.PlainText);
    }

    [Fact]
    public void MailMergeLetters_RecordSectionsAndRecipientHeadersSurvivePackageRoundTrip()
    {
        var template = new TextDocument { Blocks = { new Paragraph("Dear «Name»") } };
        template.Header = new HeaderFooter("Recipient «Name»");
        var records = MailMerge.MergeAll(
            template,
            new MergeData(["Name"], [["Ada"], ["Grace"]]));

        var combined = MailMerge.CombineMergedRecords(records, MailMergeOutputMode.Letters);
        var read = RoundTrip(combined);

        read.Sections.Should().HaveCount(2);
        read.Sections[0].BreakKind.Should().Be(SectionBreakKind.NextPage);
        read.Sections[0].HeadersFooters.Header!.PlainText.Should().Be("Recipient Ada");
        read.Sections[1].HeadersFooters.Header!.PlainText.Should().Be("Recipient Grace");
        read.PlainText.Should().Contain("Dear Ada").And.Contain("Dear Grace");
    }

    [Fact]
    public void TwoSections_EmitDistinctHeaderFooterPartsAndReferences()
    {
        var doc = new TextDocument();
        var section1 = new Section(new PageSettings(), SectionBreakKind.NextPage);
        section1.HeadersFooters.Header = new HeaderFooter("Section 1 header");
        section1.HeadersFooters.Footer = new HeaderFooter("Section 1 footer");
        doc.Blocks.Add(new Paragraph("Body of section 1") { SectionBreak = section1 });
        doc.Blocks.Add(new Paragraph("Body of section 2"));
        doc.Header = new HeaderFooter("Section 2 header");
        doc.Footer = new HeaderFooter("Section 2 footer");

        var bytes = WriteBytes(doc);
        var names = EntryNames(bytes);

        // Four distinct header/footer parts (two per section) exist in the package.
        names.Count(n => n.StartsWith("word/header")).Should().Be(2);
        names.Count(n => n.StartsWith("word/footer")).Should().Be(2);

        // The body-level (final section) sectPr references the final section's header/footer.
        var document = LoadEntry(bytes, "word/document.xml");
        var bodySectPr = document.Root!.Element(W + "body")!.Element(W + "sectPr")!;
        var bodyHeaderId = bodySectPr.Elements(W + "headerReference")
            .Single(r => (r.Attribute(W + "type")?.Value ?? "default") == "default")
            .Attribute(R + "id")!.Value;

        // The non-final section's sectPr lives in the last paragraph of section 1's pPr.
        var paraSectPr = document.Root!.Element(W + "body")!
            .Elements(W + "p")
            .Select(p => p.Element(W + "pPr")?.Element(W + "sectPr"))
            .First(s => s is not null)!;
        var paraHeaderId = paraSectPr.Elements(W + "headerReference")
            .Single(r => (r.Attribute(W + "type")?.Value ?? "default") == "default")
            .Attribute(R + "id")!.Value;

        // The two sections reference DIFFERENT header parts (so section 1's header isn't reused).
        bodyHeaderId.Should().NotBe(paraHeaderId);

        // Each referenced relationship resolves to a distinct header part.
        XNamespace rel = "http://schemas.openxmlformats.org/package/2006/relationships";
        var rels = LoadEntry(bytes, "word/_rels/document.xml.rels");
        string Target(string id) => rels.Root!
            .Elements(rel + "Relationship")
            .Single(r => r.Attribute("Id")!.Value == id)
            .Attribute("Target")!.Value;
        Target(bodyHeaderId).Should().NotBe(Target(paraHeaderId));
    }

    // --- First-page header/footer (different first page) --------------------------------------------

    [Fact]
    public void FirstPageHeaderFooter_RoundTrips()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body"));
        doc.Page.DifferentFirstPage = true;
        doc.Header = new HeaderFooter("Default header");
        doc.Footer = new HeaderFooter("Default footer");
        doc.FirstHeader = new HeaderFooter("First page header");
        doc.FirstFooter = new HeaderFooter("First page footer");

        var read = RoundTrip(doc);

        read.Page.DifferentFirstPage.Should().BeTrue();
        read.Header!.PlainText.Should().Be("Default header");
        read.Footer!.PlainText.Should().Be("Default footer");
        read.FirstHeader.Should().NotBeNull();
        read.FirstHeader!.PlainText.Should().Be("First page header");
        read.FirstFooter.Should().NotBeNull();
        read.FirstFooter!.PlainText.Should().Be("First page footer");
    }

    [Fact]
    public void FirstPageHeader_EmitsFirstTypeReference()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body"));
        doc.Page.DifferentFirstPage = true;
        doc.Header = new HeaderFooter("Default header");
        doc.FirstHeader = new HeaderFooter("First page header");

        var bytes = WriteBytes(doc);
        var document = LoadEntry(bytes, "word/document.xml");
        var sectPr = document.Root!.Element(W + "body")!.Element(W + "sectPr")!;

        sectPr.Elements(W + "headerReference")
            .Should().Contain(r => r.Attribute(W + "type")!.Value == "first");
        sectPr.Element(W + "titlePg").Should().NotBeNull();
    }

    [Fact]
    public void FirstHeader_NotEmitted_WhenDifferentFirstPageOff()
    {
        // A FirstHeader set but DifferentFirstPage off must NOT emit a first-type part/reference.
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body"));
        doc.Header = new HeaderFooter("Default header");
        doc.FirstHeader = new HeaderFooter("First page header");

        var bytes = WriteBytes(doc);
        var document = LoadEntry(bytes, "word/document.xml");
        var sectPr = document.Root!.Element(W + "body")!.Element(W + "sectPr")!;

        sectPr.Elements(W + "headerReference")
            .Select(r => r.Attribute(W + "type")?.Value)
            .Should().NotContain("first");
    }

    // --- Backward compatibility: single-section, header-less document is byte-equivalent -------------

    [Fact]
    public void SingleSection_NoHeaders_IsByteEquivalentToBaseline()
    {
        // Two independent writes of the same simple document must be byte-identical (deterministic) and must
        // contain no header/footer parts — proving the per-section machinery adds nothing when unused.
        TextDocument Build()
        {
            var d = new TextDocument();
            d.Blocks.Add(new Paragraph("Hello world"));
            return d;
        }

        var first = WriteBytes(Build());
        var second = WriteBytes(Build());

        first.Should().Equal(second);
        EntryNames(first).Should().NotContain(n => n.StartsWith("word/header") || n.StartsWith("word/footer"));
    }

    [Fact]
    public void SingleSection_DefaultHeaderFooter_UsesLegacyPartNames()
    {
        // The final section's default header/footer must still be header1.xml / footer1.xml with the legacy
        // rIdHeader1 / rIdFooter1 relationship ids, so existing documents/tests are unaffected.
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body"));
        doc.Header = new HeaderFooter("H");
        doc.Footer = new HeaderFooter("F");

        var bytes = WriteBytes(doc);
        var names = EntryNames(bytes);
        names.Should().Contain("word/header1.xml");
        names.Should().Contain("word/footer1.xml");

        var document = LoadEntry(bytes, "word/document.xml");
        var sectPr = document.Root!.Element(W + "body")!.Element(W + "sectPr")!;
        sectPr.Elements(W + "headerReference").Single().Attribute(R + "id")!.Value.Should().Be("rIdHeader1");
        sectPr.Elements(W + "footerReference").Single().Attribute(R + "id")!.Value.Should().Be("rIdFooter1");
    }

    // --- Section break kind round-trips -----------------------------------------------------------

    [Theory]
    [InlineData(SectionBreakKind.NextPage)]
    [InlineData(SectionBreakKind.Continuous)]
    [InlineData(SectionBreakKind.EvenPage)]
    [InlineData(SectionBreakKind.OddPage)]
    public void SectionBreak_RoundTrips_ThroughDocxWriterReader(SectionBreakKind kind)
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("first") { SectionBreak = new Section(new PageSettings(), kind) });
        doc.Blocks.Add(new Paragraph("second"));

        var result = RoundTrip(doc);

        result.Sections.Should().HaveCount(2);
        result.Sections[0].BreakKind.Should().Be(kind);
    }

    [Fact]
    public void CreateSectionBreak_DocumentOps_RoundTrips()
    {
        var doc = new TextDocument();
        var inherited = new PageSettings { MarginLeftPt = 55 };
        doc.Blocks.Add(DocumentOps.CreateSectionBreak(SectionBreakKind.NextPage, inherited));
        doc.Blocks.Add(new Paragraph("second"));

        var result = RoundTrip(doc);

        result.Sections.Should().HaveCount(2);
        result.Sections[0].BreakKind.Should().Be(SectionBreakKind.NextPage);
        result.Sections[0].Page.MarginLeftPt.Should().Be(55);
    }
}
