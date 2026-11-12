using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// "Link to Previous" is expressed in WordprocessingML purely by absence: a section whose w:sectPr has no
/// w:headerReference/w:footerReference of a type shows the previous section's header/footer of that type.
/// These tests pin that the writer neither invents references for linked sections nor drops the ones an
/// unlinked section needs.
/// </summary>
public sealed class SectionHeaderFooterLinkRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    [Fact]
    public void LinkedSections_WriteNoReference_AndReloadLinked()
    {
        var document = ThreeSections();
        document.Sections[0].HeadersFooters.Header = new HeaderFooter("one");
        document.Sections[0].HeadersFooters.Footer = new HeaderFooter("foot one");
        document.Sections[2].HeadersFooters.Footer = new HeaderFooter("foot three");

        var bytes = Write(document);
        var sectPrs = SectionProperties(bytes);

        References(sectPrs[0], "headerReference").Should().Equal("default");
        References(sectPrs[0], "footerReference").Should().Equal("default");
        References(sectPrs[1], "headerReference").Should().BeEmpty("section 2's header is linked to previous");
        References(sectPrs[1], "footerReference").Should().BeEmpty("section 2's footer is linked to previous");
        References(sectPrs[2], "headerReference").Should().BeEmpty("section 3's header is linked to previous");
        References(sectPrs[2], "footerReference").Should().Equal("default");

        var reopened = DocxReader.Read(new MemoryStream(bytes));
        reopened.Sections[0].HeadersFooters.Header!.PlainText.Should().Be("one");
        reopened.Sections[1].HeadersFooters.Header.Should().BeNull();
        reopened.Sections[1].HeadersFooters.Footer.Should().BeNull();
        reopened.Sections[2].HeadersFooters.Header.Should().BeNull();
        reopened.Sections[2].HeadersFooters.Footer!.PlainText.Should().Be("foot three");
    }

    [Fact]
    public void UnlinkedSectionWithItsOwnCopy_KeepsItsOwnReferenceAndContent()
    {
        var document = ThreeSections();
        document.Sections[0].HeadersFooters.Header = new HeaderFooter("one");
        document.Sections[1].HeadersFooters.Header = new HeaderFooter("two");

        var reopened = DocxReader.Read(new MemoryStream(Write(document)));

        reopened.Sections[0].HeadersFooters.Header!.PlainText.Should().Be("one");
        reopened.Sections[1].HeadersFooters.Header!.PlainText.Should().Be("two");
        reopened.Sections[2].HeadersFooters.Header.Should().BeNull("section 3 stays linked to section 2");
    }

    [Fact]
    public void UnlinkedBlankSection_StillWritesAReference_SoItDoesNotReloadLinked()
    {
        var document = ThreeSections();
        document.Sections[0].HeadersFooters.Header = new HeaderFooter("one");
        document.Sections[1].HeadersFooters.Header = new HeaderFooter(); // unlinked, deliberately blank

        var bytes = Write(document);
        var sectPrs = SectionProperties(bytes);

        References(sectPrs[1], "headerReference").Should().Equal("default");
        References(sectPrs[2], "headerReference").Should().BeEmpty();

        var reopened = DocxReader.Read(new MemoryStream(bytes));
        reopened.Sections[1].HeadersFooters.Header.Should().NotBeNull("an unlinked blank header must not relink");
        reopened.Sections[1].HeadersFooters.Header!.IsEmpty.Should().BeTrue();
        reopened.Sections[2].HeadersFooters.Header.Should().BeNull();

        // Stable across a second save.
        var again = DocxReader.Read(new MemoryStream(Write(reopened)));
        again.Sections[1].HeadersFooters.Header.Should().NotBeNull();
        again.Sections[0].HeadersFooters.Header!.PlainText.Should().Be("one");
    }

    [Fact]
    public void BlankSlotWithNothingToInherit_WritesNoPart()
    {
        var document = ThreeSections();
        document.Sections[1].HeadersFooters.Footer = new HeaderFooter();
        document.FinalSectionHeadersFooters.Header = new HeaderFooter();

        var bytes = Write(document);

        SectionProperties(bytes).Should().OnlyContain(sectPr =>
            !sectPr.Elements(W + "headerReference").Any() && !sectPr.Elements(W + "footerReference").Any());
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        zip.Entries.Should().NotContain(entry =>
            entry.FullName.StartsWith("word/header", StringComparison.Ordinal)
            || entry.FullName.StartsWith("word/footer", StringComparison.Ordinal));
    }

    [Fact]
    public void FirstPageHeaderOfASectionWithoutTitlePg_SurvivesForALaterLinkedSection()
    {
        var document = ThreeSections();
        document.Sections[0].HeadersFooters.FirstHeader = new HeaderFooter("first-page header");
        document.Sections[1].Page.DifferentFirstPage = true; // linked: inherits section 1's first-page header

        var bytes = Write(document);
        var sectPrs = SectionProperties(bytes);

        References(sectPrs[0], "headerReference").Should().Equal("first");
        sectPrs[0].Element(W + "titlePg").Should().BeNull();
        References(sectPrs[1], "headerReference").Should().BeEmpty();
        sectPrs[1].Element(W + "titlePg").Should().NotBeNull();

        var reopened = DocxReader.Read(new MemoryStream(bytes));
        reopened.Sections[0].HeadersFooters.FirstHeader!.PlainText.Should().Be("first-page header");
        reopened.Sections[0].Page.DifferentFirstPage.Should().BeFalse();
        reopened.Sections[1].HeadersFooters.FirstHeader.Should().BeNull();
        reopened.Sections[1].Page.DifferentFirstPage.Should().BeTrue();
    }

    [Fact]
    public void SingleSectionDocument_KeepsItsLegacyPartNames()
    {
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph("body"));
        document.FinalSectionHeadersFooters.Header = new HeaderFooter("header");
        document.FinalSectionHeadersFooters.Footer = new HeaderFooter("footer");

        using var zip = new ZipArchive(new MemoryStream(Write(document)), ZipArchiveMode.Read);

        zip.Entries.Select(entry => entry.FullName)
            .Where(name => name.StartsWith("word/header", StringComparison.Ordinal)
                || name.StartsWith("word/footer", StringComparison.Ordinal))
            .Should().BeEquivalentTo("word/header1.xml", "word/footer1.xml");
    }

    private static TextDocument ThreeSections()
    {
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph("section one") { SectionBreak = new Section(new PageSettings()) });
        document.Blocks.Add(new Paragraph("section two") { SectionBreak = new Section(new PageSettings()) });
        document.Blocks.Add(new Paragraph("section three"));
        return document;
    }

    private static byte[] Write(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        return stream.ToArray();
    }

    private static List<XElement> SectionProperties(byte[] bytes)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/document.xml")!.Open();
        return XDocument.Load(entry).Descendants(W + "sectPr").ToList();
    }

    private static IEnumerable<string> References(XElement sectPr, string name) =>
        sectPr.Elements(W + name).Select(reference => reference.Attribute(W + "type")?.Value ?? "default");
}
