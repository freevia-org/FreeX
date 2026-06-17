using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-trip coverage for inline images inside headers/footers (Task B): a picture in a header/footer run
/// writes its media part and a PART-LOCAL relationship (word/_rels/headerN.xml.rels), and re-reads back into
/// the header/footer model. The header image r:embed must resolve against the header part's own rels, not
/// document.xml.rels.
/// </summary>
public class HeaderImageRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private static byte[] WriteBytes(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        return stream.ToArray();
    }

    private static TextDocument RoundTrip(TextDocument document)
    {
        var bytes = WriteBytes(document);
        return DocxReader.Read(new MemoryStream(bytes));
    }

    private static HeaderFooter HeaderWithImage(byte[] png, double w, double h)
    {
        var header = new HeaderFooter();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Logo: "));
        paragraph.Runs.Add(Run.FromImage(new InlineImage(png, w, h)));
        header.Paragraphs.Add(paragraph);
        return header;
    }

    [Fact]
    public void ImageInsideHeader_RoundTrips()
    {
        var png = MinimalPng();
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body"));
        doc.Header = HeaderWithImage(png, 64, 48);

        var read = RoundTrip(doc);

        read.Header.Should().NotBeNull();
        var imageRun = read.Header!.Paragraphs.SelectMany(p => p.Runs).Single(r => r.Image is not null);
        imageRun.Image!.PngBytes.Should().Equal(png);
        imageRun.Image!.WidthPt.Should().BeApproximately(64, 0.5);
        imageRun.Image!.HeightPt.Should().BeApproximately(48, 0.5);
        // The surrounding header text survives alongside the image.
        read.Header!.PlainText.Should().Contain("Logo:");
    }

    [Fact]
    public void ImageInsideHeader_WritesPartLocalRelsAndMedia()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body"));
        doc.Header = HeaderWithImage(MinimalPng(), 64, 48);

        var bytes = WriteBytes(doc);
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var names = zip.Entries.Select(e => e.FullName).ToList();

        // The header part carries its OWN _rels with the image relationship.
        names.Should().Contain("word/_rels/header1.xml.rels");
        // A media part for the header image exists.
        names.Should().Contain(n => n.StartsWith("word/media/") && n.EndsWith(".png"));

        // The header part's _rels declares the image relationship that its drawing references.
        XNamespace rel = "http://schemas.openxmlformats.org/package/2006/relationships";
        XDocument Load(string path)
        {
            using var s = zip.GetEntry(path)!.Open();
            return XDocument.Load(s);
        }

        var headerRels = Load("word/_rels/header1.xml.rels");
        var imageRel = headerRels.Root!.Elements(rel + "Relationship")
            .Single(r => r.Attribute("Type")!.Value.EndsWith("/image"));
        var relId = imageRel.Attribute("Id")!.Value;

        // The header XML's blip r:embed references the PART-LOCAL relationship id (not a document rel id).
        var headerXml = Load("word/header1.xml");
        var embedId = headerXml.Descendants(A + "blip").Single().Attribute(R + "embed")!.Value;
        embedId.Should().Be(relId);

        // The document-level rels must NOT carry that header image relationship (it is part-local).
        var docRels = Load("word/_rels/document.xml.rels");
        docRels.Root!.Elements(rel + "Relationship")
            .Should().NotContain(r => r.Attribute("Id")!.Value == relId
                && r.Attribute("Type")!.Value.EndsWith("/image"));
    }

    [Fact]
    public void ImageInsideNonFinalSectionHeader_RoundTrips()
    {
        // An image in a NON-final section's header must round-trip too (its own part + part-local rels).
        var png = MinimalPng();
        var doc = new TextDocument();
        var section1 = new Section(new PageSettings(), SectionBreakKind.NextPage);
        section1.HeadersFooters.Header = HeaderWithImage(png, 30, 30);
        doc.Blocks.Add(new Paragraph("Body 1") { SectionBreak = section1 });
        doc.Blocks.Add(new Paragraph("Body 2"));
        doc.Header = new HeaderFooter("Final header");

        var read = RoundTrip(doc);

        var s1Header = read.Sections[0].HeadersFooters.Header;
        s1Header.Should().NotBeNull();
        var imageRun = s1Header!.Paragraphs.SelectMany(p => p.Runs).Single(r => r.Image is not null);
        imageRun.Image!.PngBytes.Should().Equal(png);

        // The final section's header has no image (its content is unaffected).
        read.Header!.Paragraphs.SelectMany(p => p.Runs).Where(r => r.Image is not null).Should().BeEmpty();
    }

    private static byte[] MinimalPng() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x62, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82,
    ];
}
