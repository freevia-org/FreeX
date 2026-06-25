using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-trip coverage for roadmap item Z3: different odd/even page headers &amp; footers
/// (w:settings/w:evenAndOddHeaders + header2.xml/footer2.xml + w:sectPr type="even" references) and a
/// page background colour (w:document/w:background + w:settings/w:displayBackgroundShape). Defaults must
/// preserve the existing output exactly — a document with neither emits no settings part and no
/// w:background, and round-trips unchanged.
/// </summary>
public class OddEvenAndBackgroundRoundTripTests
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

    private static (XDocument document, XDocument? settings, XDocument? rels, bool hasHeader2, bool hasFooter2) WriteParts(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

        static XDocument? Load(ZipArchive zip, string path)
        {
            var entry = zip.GetEntry(path);
            if (entry is null)
                return null;
            using var s = entry.Open();
            return XDocument.Load(s);
        }

        return (
            Load(zip, "word/document.xml")!,
            Load(zip, "word/settings.xml"),
            Load(zip, "word/_rels/document.xml.rels"),
            zip.GetEntry("word/header2.xml") is not null,
            zip.GetEntry("word/footer2.xml") is not null);
    }

    private static TextDocument DocumentWith(string bodyText)
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph(bodyText));
        return doc;
    }

    // --- Task A: different odd/even headers and footers ---------------------------------------------

    [Fact]
    public void DifferentOddEvenPages_EmitsEvenAndOddHeadersToggleAndEvenParts()
    {
        var doc = DocumentWith("Body");
        doc.Page.DifferentOddEvenPages = true;
        doc.Header = new HeaderFooter("Odd header");
        doc.Footer = new HeaderFooter("Odd footer");
        doc.EvenHeader = new HeaderFooter("Even header");
        doc.EvenFooter = new HeaderFooter("Even footer");

        var (document, settings, rels, hasHeader2, hasFooter2) = WriteParts(doc);

        // The document-level toggle lives in settings.xml.
        settings.Should().NotBeNull();
        settings!.Root!.Element(W + "evenAndOddHeaders").Should().NotBeNull();

        // Even header/footer parts are emitted.
        hasHeader2.Should().BeTrue();
        hasFooter2.Should().BeTrue();

        // The sectPr carries the type="even" references in addition to the default ones.
        var sectPr = document.Root!.Element(W + "body")!.Element(W + "sectPr")!;
        sectPr.Elements(W + "headerReference").Should().Contain(r => r.Attribute(W + "type")!.Value == "even");
        sectPr.Elements(W + "footerReference").Should().Contain(r => r.Attribute(W + "type")!.Value == "even");
        sectPr.Elements(W + "headerReference").Should().Contain(r => r.Attribute(W + "type")!.Value == "default");

        // The even references resolve to header2.xml / footer2.xml via the document relationships.
        var evenHeaderId = sectPr.Elements(W + "headerReference").Single(r => r.Attribute(W + "type")!.Value == "even").Attribute(R + "id")!.Value;
        var target = rels!.Root!.Elements()
            .Single(rel => rel.Attribute("Id")!.Value == evenHeaderId)
            .Attribute("Target")!.Value;
        target.Should().Be("header2.xml");
    }

    [Fact]
    public void DifferentOddEvenPages_SurvivesRoundTrip()
    {
        var doc = DocumentWith("Body");
        doc.Page.DifferentOddEvenPages = true;
        doc.Header = new HeaderFooter("Odd header");
        doc.Footer = new HeaderFooter("Odd footer");
        doc.EvenHeader = new HeaderFooter("Even header");
        doc.EvenFooter = new HeaderFooter("Even footer");

        var read = RoundTrip(doc);

        read.Page.DifferentOddEvenPages.Should().BeTrue();
        read.Header!.PlainText.Should().Be("Odd header");
        read.Footer!.PlainText.Should().Be("Odd footer");
        read.EvenHeader.Should().NotBeNull();
        read.EvenHeader!.PlainText.Should().Be("Even header");
        read.EvenFooter.Should().NotBeNull();
        read.EvenFooter!.PlainText.Should().Be("Even footer");
    }

    // --- Task B: page background colour -------------------------------------------------------------

    [Fact]
    public void PageBackground_EmitsBackgroundAndDisplayBackgroundShape()
    {
        var doc = DocumentWith("Body");
        doc.Page.BackgroundColorHex = "#FFFFCC";

        var (document, settings, _, _, _) = WriteParts(doc);

        // w:background is the FIRST child of w:document, before w:body.
        var root = document.Root!;
        root.Elements().First().Name.Should().Be(W + "background");
        root.Element(W + "background")!.Attribute(W + "color")!.Value.Should().Be("FFFFCC");

        // w:displayBackgroundShape makes Word actually paint it.
        settings.Should().NotBeNull();
        settings!.Root!.Element(W + "displayBackgroundShape").Should().NotBeNull();
    }

    [Fact]
    public void PageBackground_SurvivesRoundTrip()
    {
        var doc = DocumentWith("Body");
        doc.Page.BackgroundColorHex = "#C0FFEE";

        var read = RoundTrip(doc);

        read.Page.BackgroundColorHex.Should().Be("#C0FFEE");
    }

    // --- Regression: neither feature set ------------------------------------------------------------

    [Fact]
    public void NeitherFeature_EmitsNoSettingsPartNoBackgroundAndRoundTripsUnchanged()
    {
        var doc = DocumentWith("Plain body");

        var (document, settings, _, hasHeader2, hasFooter2) = WriteParts(doc);

        // No settings part, no even parts, no background element.
        settings.Should().BeNull();
        hasHeader2.Should().BeFalse();
        hasFooter2.Should().BeFalse();
        document.Root!.Element(W + "background").Should().BeNull();

        var read = RoundTrip(doc);
        read.Page.DifferentOddEvenPages.Should().BeFalse();
        read.Page.BackgroundColorHex.Should().BeNull();
        read.EvenHeader.Should().BeNull();
        read.EvenFooter.Should().BeNull();
        read.PlainText.Should().Be("Plain body");
    }

    [Fact]
    public void DifferentOddEvenPages_ToggleWithoutEvenContent_StillEmitsToggleAndRoundTrips()
    {
        // The document-level toggle alone (no distinct even content) must still emit the settings part so
        // Word honours "different odd and even pages", even though no header2/footer2 part is written.
        var doc = DocumentWith("Body");
        doc.Page.DifferentOddEvenPages = true;

        var (_, settings, _, hasHeader2, hasFooter2) = WriteParts(doc);
        settings.Should().NotBeNull();
        settings!.Root!.Element(W + "evenAndOddHeaders").Should().NotBeNull();
        hasHeader2.Should().BeFalse();
        hasFooter2.Should().BeFalse();

        RoundTrip(doc).Page.DifferentOddEvenPages.Should().BeTrue();
    }

    // R17 regression — a non-final section with DifferentOddEvenPages=true but the final section OFF must
    // still emit w:evenAndOddHeaders in settings.xml (the global toggle), otherwise Word ignores the even
    // header/footer parts that were written for that section.
    [Fact]
    public void NonFinalSection_DifferentOddEvenPages_EmitsGlobalToggleEvenWhenFinalSectionIsOff()
    {
        var doc = new TextDocument();

        // Section 1 (non-final): different odd/even on, with distinct even header.
        var section1Page = new PageSettings { DifferentOddEvenPages = true };
        var section1 = new Section(section1Page, SectionBreakKind.NextPage);
        section1.HeadersFooters.Header = new HeaderFooter("Odd header s1");
        section1.HeadersFooters.EvenHeader = new HeaderFooter("Even header s1");
        doc.Blocks.Add(new Paragraph("Section 1 body") { SectionBreak = section1 });

        // Final section: different odd/even explicitly OFF.
        doc.Page.DifferentOddEvenPages = false;
        doc.Header = new HeaderFooter("Final section header");
        doc.Blocks.Add(new Paragraph("Final section body"));

        var (_, settings, _, hasHeader2, _) = WriteParts(doc);

        // The global toggle must be present because section1 has DifferentOddEvenPages=true.
        settings.Should().NotBeNull(because: "settings.xml must exist whenever any section has different-odd/even on");
        settings!.Root!.Element(W + "evenAndOddHeaders").Should().NotBeNull(
            because: "w:evenAndOddHeaders must be set even when only a non-final section enables it");

        // The even header part for section 1 must also have been written.
        hasHeader2.Should().BeTrue(because: "the even-header part for section 1 must be emitted");
    }

    // S5 regression — a non-final section's even header/footer must survive a full read→write→read cycle
    // when the global w:evenAndOddHeaders toggle is on. Before this fix the reader set DifferentOddEvenPages
    // only on document.Page (the final section), leaving non-final sections' PageSettings with the flag off;
    // the writer then skipped emitting the even header/footer part + reference for those sections.
    [Fact]
    public void NonFinalSection_EvenHeader_SurvivesReadWriteReadRoundTrip()
    {
        var doc = new TextDocument();

        // Section 1 (non-final): different odd/even on, with a DISTINCT even header.
        var section1Page = new PageSettings { DifferentOddEvenPages = true };
        var section1 = new Section(section1Page, SectionBreakKind.NextPage);
        section1.HeadersFooters.Header = new HeaderFooter("Section 1 odd header");
        section1.HeadersFooters.EvenHeader = new HeaderFooter("Section 1 even header");
        doc.Blocks.Add(new Paragraph("Section 1 body") { SectionBreak = section1 });

        // Final section: also different odd/even, with its own even header.
        doc.Page.DifferentOddEvenPages = true;
        doc.Header = new HeaderFooter("Final section odd header");
        doc.EvenHeader = new HeaderFooter("Final section even header");
        doc.Blocks.Add(new Paragraph("Final section body"));

        // Write → read back → write again → read back (two full round-trips).
        TextDocument Roundtrip(TextDocument d)
        {
            using var ms = new MemoryStream();
            DocxWriter.Write(d, ms);
            ms.Position = 0;
            return DocxReader.Read(ms);
        }

        var rt1 = Roundtrip(doc);
        var rt2 = Roundtrip(rt1);

        // After both round-trips the non-final section must still carry its distinct even header.
        var sections = rt2.Sections;
        sections.Should().HaveCount(2, "the document has one non-final + one final section");
        var s1 = sections[0];
        s1.HeadersFooters.EvenHeader.Should().NotBeNull(
            because: "the non-final section's even header must survive read→write→read");
        s1.HeadersFooters.EvenHeader!.PlainText.Should().Be("Section 1 even header",
            because: "the non-final section's even header content must be preserved");

        // The final section's even header must also be intact.
        rt2.EvenHeader.Should().NotBeNull();
        rt2.EvenHeader!.PlainText.Should().Be("Final section even header");
    }
}
