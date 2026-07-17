using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

public sealed class PageNumberFormatRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    [Fact]
    public void FinalSection_PageNumberFormatAndStartAt_RoundTripThroughPgNumType()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Body"));
        doc.Page.PageNumberFormat = PageNumberFormat.UpperRoman;
        doc.Page.PageNumberStartAt = 4;
        doc.Footer = FooterWithPageNumber();

        var read = RoundTrip(doc);
        var xml = DocumentXml(doc);
        var pgNumType = xml.Descendants(W + "sectPr").Last().Element(W + "pgNumType");

        read.Page.PageNumberFormat.Should().Be(PageNumberFormat.UpperRoman);
        read.Page.PageNumberStartAt.Should().Be(4);
        read.Footer!.Paragraphs.SelectMany(p => p.Runs)
            .Should().Contain(r => r.FieldKind == RunFieldKind.PageNumber);
        pgNumType.Should().NotBeNull();
        pgNumType!.Attribute(W + "fmt")!.Value.Should().Be("upperRoman");
        pgNumType.Attribute(W + "start")!.Value.Should().Be("4");
    }

    [Fact]
    public void SectionPageNumbering_RoundTripsStartAtAndContinue()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var firstPage = doc.Page.Clone();
        firstPage.PageNumberFormat = PageNumberFormat.LowerLetter;
        firstPage.PageNumberStartAt = 3;
        doc.Blocks.Add(new Paragraph("Section 1")
        {
            SectionBreak = new Section(firstPage, SectionBreakKind.NextPage)
        });
        doc.Blocks.Add(new Paragraph("Section 2"));
        doc.Page.PageNumberFormat = PageNumberFormat.UpperRoman;
        doc.Page.PageNumberStartAt = null;

        var read = RoundTrip(doc);
        var pgNumTypes = DocumentXml(doc).Descendants(W + "pgNumType").ToList();

        read.Sections[0].Page.PageNumberFormat.Should().Be(PageNumberFormat.LowerLetter);
        read.Sections[0].Page.PageNumberStartAt.Should().Be(3);
        read.Page.PageNumberFormat.Should().Be(PageNumberFormat.UpperRoman);
        read.Page.PageNumberStartAt.Should().BeNull("missing w:start means continue from the previous section");
        pgNumTypes.Should().HaveCount(2);
        pgNumTypes[0].Attribute(W + "fmt")!.Value.Should().Be("lowerLetter");
        pgNumTypes[0].Attribute(W + "start")!.Value.Should().Be("3");
        pgNumTypes[1].Attribute(W + "fmt")!.Value.Should().Be("upperRoman");
        pgNumTypes[1].Attribute(W + "start").Should().BeNull();
    }

    [Fact]
    public void FinalSection_PageNumberChapterNumbering_RoundTripsThroughPgNumType()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Chapter")
        {
            StyleId = "Heading1",
            Formatting = ParagraphFormatting.Default with { ListKind = ListKind.MultiLevel }
        });
        doc.Blocks.Add(new Paragraph("Body"));
        doc.Page.PageNumberChapterStyleLevel = 1;
        doc.Page.PageNumberChapterSeparator = PageNumberChapterSeparator.Colon;
        doc.Footer = FooterWithPageNumber();

        var read = RoundTrip(doc);
        var xml = DocumentXml(doc);
        var styles = StylesXml(doc);
        var numbering = NumberingXml(doc);
        var pgNumType = xml.Descendants(W + "sectPr").Last().Element(W + "pgNumType");
        var chapterLevel = numbering.Descendants(W + "abstractNum")
            .Single(numbering => numbering.Attribute(W + "abstractNumId")?.Value == "2")
            .Elements(W + "lvl")
            .Single(level => level.Attribute(W + "ilvl")?.Value == "0");
        var headingStyle = styles.Descendants(W + "style")
            .Single(style => style.Attribute(W + "styleId")?.Value == "Heading1");

        read.Page.PageNumberChapterStyleLevel.Should().Be(1);
        read.Page.PageNumberChapterSeparator.Should().Be(PageNumberChapterSeparator.Colon);
        pgNumType.Should().NotBeNull();
        pgNumType!.Attribute(W + "chapStyle")!.Value.Should().Be("1");
        pgNumType.Attribute(W + "chapSep")!.Value.Should().Be("colon");
        chapterLevel.Element(W + "pStyle")!.Attribute(W + "val")!.Value.Should().Be("Heading1");
        headingStyle.Element(W + "pPr")!.Element(W + "numPr")!
            .Element(W + "numId")!.Attribute(W + "val")!.Value.Should().Be("3");
        headingStyle.Element(W + "pPr")!.Element(W + "outlineLvl")!
            .Attribute(W + "val")!.Value.Should().Be("0");
    }

    private static TextDocument RoundTrip(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    private static XDocument DocumentXml(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        using var zip = new ZipArchive(new MemoryStream(stream.ToArray()), ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/document.xml")!.Open();
        return XDocument.Load(entry);
    }

    private static XDocument StylesXml(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        using var zip = new ZipArchive(new MemoryStream(stream.ToArray()), ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/styles.xml")!.Open();
        return XDocument.Load(entry);
    }

    private static XDocument NumberingXml(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        using var zip = new ZipArchive(new MemoryStream(stream.ToArray()), ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/numbering.xml")!.Open();
        return XDocument.Load(entry);
    }

    private static HeaderFooter FooterWithPageNumber()
    {
        var footer = new HeaderFooter();
        var para = new Paragraph();
        para.Runs.Add(Run.PageNumberField());
        footer.Paragraphs.Add(para);
        return footer;
    }
}
