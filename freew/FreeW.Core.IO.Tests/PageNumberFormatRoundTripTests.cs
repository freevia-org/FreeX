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
        doc.Blocks.Add(new Paragraph("Chapter") { StyleId = "Heading1" });
        doc.Blocks.Add(new Paragraph("Body"));
        doc.Page.PageNumberChapterStyleLevel = 1;
        doc.Page.PageNumberChapterSeparator = PageNumberChapterSeparator.Colon;
        doc.Footer = FooterWithPageNumber();

        var read = RoundTrip(doc);
        var xml = DocumentXml(doc);
        var pgNumType = xml.Descendants(W + "sectPr").Last().Element(W + "pgNumType");

        read.Page.PageNumberChapterStyleLevel.Should().Be(1);
        read.Page.PageNumberChapterSeparator.Should().Be(PageNumberChapterSeparator.Colon);
        pgNumType.Should().NotBeNull();
        pgNumType!.Attribute(W + "chapStyle")!.Value.Should().Be("1");
        pgNumType.Attribute(W + "chapSep")!.Value.Should().Be("colon");
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

    private static HeaderFooter FooterWithPageNumber()
    {
        var footer = new HeaderFooter();
        var para = new Paragraph();
        para.Runs.Add(Run.PageNumberField());
        footer.Paragraphs.Add(para);
        return footer;
    }
}
