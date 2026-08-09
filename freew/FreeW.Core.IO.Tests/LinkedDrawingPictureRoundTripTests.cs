using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

public sealed class LinkedDrawingPictureRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";

    private const string LinkedTarget = "file:///C:/Word%20Assets/linked-photo.png";

    [Fact]
    public void LinkedOnlyDrawingPicture_ReadFromPathLoadsLocalPreviewWithoutEmbeddingItOnSave()
    {
        using var temporaryDirectory = new TestTemporaryDirectory("FreeW.LinkedDrawingPictureTests-");
        var directory = temporaryDirectory.Path;
        {
            var preview = PngBytes();
            File.WriteAllBytes(Path.Combine(directory, "linked-photo.png"), preview);
            var documentPath = Path.Combine(directory, "linked.docx");
            File.WriteAllBytes(documentPath, BuildSourcePackage(
                includeEmbeddedPreview: false,
                linkedTarget: "linked-photo.png"));

            var loaded = DocxReader.Read(documentPath);
            var image = SingleBodyImage(loaded);

            image.Bytes.Should().BeEmpty();
            image.ResolvedLinkedImageBytes.Should().Equal(preview);
            image.DisplayBytes.Should().Equal(preview);

            var saved = WriteBytes(loaded);
            using var zip = new ZipArchive(new MemoryStream(saved), ZipArchiveMode.Read);
            zip.Entries.Should().NotContain(entry => entry.FullName.StartsWith("word/media/", StringComparison.Ordinal));
            var linkId = LoadXml(zip, "word/document.xml").Descendants(A + "blip").Single()
                .Attribute(R + "link")!.Value;
            var relationship = LoadXml(zip, "word/_rels/document.xml.rels").Root!
                .Elements(Rel + "Relationship").Single(element => element.Attribute("Id")?.Value == linkId);
            relationship.Attribute("Target")!.Value.Should().Be("linked-photo.png");
            relationship.Attribute("TargetMode")!.Value.Should().Be("External");
        }
    }

    [Fact]
    public void LinkedOnlyDrawingPicture_ReadFromPathDoesNotFetchRemotePreview()
    {
        using var temporaryDirectory = new TestTemporaryDirectory("FreeW.LinkedDrawingPictureTests-");
        var directory = temporaryDirectory.Path;
        {
            var documentPath = Path.Combine(directory, "remote-linked.docx");
            File.WriteAllBytes(documentPath, BuildSourcePackage(
                includeEmbeddedPreview: false,
                linkedTarget: "https://example.invalid/linked-photo.png"));

            var image = SingleBodyImage(DocxReader.Read(documentPath));

            image.Bytes.Should().BeEmpty();
            image.ResolvedLinkedImageBytes.Should().BeNull();
            image.LinkedImageTarget.Should().Be("https://example.invalid/linked-photo.png");
        }
    }

    [Fact]
    public void LinkedOnlyDrawingPicture_PreservesExternalRelationshipAndReopenedModel()
    {
        var source = BuildSourcePackage(includeEmbeddedPreview: false);
        var loaded = DocxReader.Read(new MemoryStream(source));
        var image = SingleBodyImage(loaded);

        image.Bytes.Should().BeEmpty();
        image.LinkedImageTarget.Should().Be(LinkedTarget);
        image.WidthPt.Should().BeApproximately(96, 0.01);
        image.HeightPt.Should().BeApproximately(48, 0.01);

        var saved = WriteBytes(loaded);
        using (var zip = new ZipArchive(new MemoryStream(saved), ZipArchiveMode.Read))
        {
            var document = LoadXml(zip, "word/document.xml");
            var blip = document.Descendants(A + "blip").Single();
            blip.Attribute(R + "embed").Should().BeNull();
            var linkId = blip.Attribute(R + "link")?.Value;
            linkId.Should().NotBeNullOrWhiteSpace();

            var relationships = LoadXml(zip, "word/_rels/document.xml.rels");
            var relationship = relationships.Root!.Elements(Rel + "Relationship")
                .Single(element => element.Attribute("Id")?.Value == linkId);
            relationship.Attribute("Type")?.Value.Should().EndWith("/image");
            relationship.Attribute("Target")?.Value.Should().Be(LinkedTarget);
            relationship.Attribute("TargetMode")?.Value.Should().Be("External");
            zip.Entries.Should().NotContain(entry => entry.FullName.StartsWith("word/media/", StringComparison.Ordinal));
        }

        var reopened = DocxReader.Read(new MemoryStream(saved));
        SingleBodyImage(reopened).LinkedImageTarget.Should().Be(LinkedTarget);
        SingleBodyImage(reopened).Bytes.Should().BeEmpty();
    }

    [Fact]
    public void LinkedDrawingPictureWithEmbeddedPreview_PreservesBothReferences()
    {
        var loaded = DocxReader.Read(new MemoryStream(BuildSourcePackage(includeEmbeddedPreview: true)));
        var image = SingleBodyImage(loaded);
        image.Bytes.Should().Equal(PngBytes());
        image.LinkedImageTarget.Should().Be(LinkedTarget);

        var saved = WriteBytes(loaded);
        using var zip = new ZipArchive(new MemoryStream(saved), ZipArchiveMode.Read);
        var blip = LoadXml(zip, "word/document.xml").Descendants(A + "blip").Single();
        var embedId = blip.Attribute(R + "embed")?.Value;
        var linkId = blip.Attribute(R + "link")?.Value;
        embedId.Should().NotBeNullOrWhiteSpace();
        linkId.Should().NotBeNullOrWhiteSpace();

        var relationships = LoadXml(zip, "word/_rels/document.xml.rels");
        var imageRelationships = relationships.Root!.Elements(Rel + "Relationship")
            .Where(element => element.Attribute("Type")?.Value.EndsWith("/image", StringComparison.Ordinal) == true)
            .ToDictionary(element => element.Attribute("Id")!.Value);
        imageRelationships[embedId!].Attribute("TargetMode").Should().BeNull();
        imageRelationships[linkId!].Attribute("TargetMode")?.Value.Should().Be("External");
        imageRelationships[linkId!].Attribute("Target")?.Value.Should().Be(LinkedTarget);
        zip.GetEntry("word/media/image1.png").Should().NotBeNull();
    }

    [Fact]
    public void LinkedOnlyHeaderPicture_UsesPartLocalExternalRelationshipAndReopens()
    {
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph("Body"));
        document.Header = new HeaderFooter();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromImage(new InlineImage([], 72, 36)
        {
            LinkedImageTarget = LinkedTarget,
            AltText = "Linked logo"
        }));
        document.Header.Paragraphs.Add(paragraph);

        var saved = WriteBytes(document);
        using (var zip = new ZipArchive(new MemoryStream(saved), ZipArchiveMode.Read))
        {
            var header = LoadXml(zip, "word/header1.xml");
            var linkId = header.Descendants(A + "blip").Single().Attribute(R + "link")?.Value;
            var headerRelationships = LoadXml(zip, "word/_rels/header1.xml.rels");
            var relationship = headerRelationships.Root!.Elements(Rel + "Relationship")
                .Single(element => element.Attribute("Id")?.Value == linkId);
            relationship.Attribute("Target")?.Value.Should().Be(LinkedTarget);
            relationship.Attribute("TargetMode")?.Value.Should().Be("External");
        }

        var reopened = DocxReader.Read(new MemoryStream(saved));
        var reopenedImage = reopened.Header!.Paragraphs.SelectMany(item => item.Runs)
            .Single(run => run.Image is not null).Image!;
        reopenedImage.LinkedImageTarget.Should().Be(LinkedTarget);
        reopenedImage.Bytes.Should().BeEmpty();
        reopenedImage.AltText.Should().Be("Linked logo");
    }

    [Fact]
    public void LinkedOnlyPictures_RoundTripInCommentFootnoteAndEndnoteStories()
    {
        static Paragraph LinkedParagraph()
        {
            var paragraph = new Paragraph();
            paragraph.Runs.Add(Run.FromImage(new InlineImage([], 36, 18)
            {
                LinkedImageTarget = LinkedTarget
            }));
            return paragraph;
        }

        var document = new TextDocument();
        document.Blocks.Add(new Paragraph("Body"));

        var comment = new Comment(0, string.Empty, "Reviewer", "R");
        comment.Content.Clear();
        comment.Content.Add(LinkedParagraph());
        document.Comments[0] = comment;

        var footnote = new Footnote(1);
        footnote.Content.Add(LinkedParagraph());
        document.Footnotes[1] = footnote;

        var endnote = new Endnote(1);
        endnote.Content.Add(LinkedParagraph());
        document.Endnotes[1] = endnote;

        var saved = WriteBytes(document);
        using (var zip = new ZipArchive(new MemoryStream(saved), ZipArchiveMode.Read))
        {
            AssertLinkedRelationship(zip, "word/_rels/comments.xml.rels");
            AssertLinkedRelationship(zip, "word/_rels/footnotes.xml.rels");
            AssertLinkedRelationship(zip, "word/_rels/endnotes.xml.rels");
            zip.Entries.Should().NotContain(entry => entry.FullName.StartsWith("word/media/", StringComparison.Ordinal));
        }

        var reopened = DocxReader.Read(new MemoryStream(saved));
        reopened.Comments[0].Content.SelectMany(item => item.Runs).Single(run => run.Image is not null)
            .Image!.LinkedImageTarget.Should().Be(LinkedTarget);
        reopened.Footnotes[1].Content.SelectMany(item => item.Runs).Single(run => run.Image is not null)
            .Image!.LinkedImageTarget.Should().Be(LinkedTarget);
        reopened.Endnotes[1].Content.SelectMany(item => item.Runs).Single(run => run.Image is not null)
            .Image!.LinkedImageTarget.Should().Be(LinkedTarget);
    }

    private static InlineImage SingleBodyImage(TextDocument document) =>
        document.Paragraphs.SelectMany(paragraph => paragraph.Runs)
            .Single(run => run.Image is not null).Image!;

    private static byte[] WriteBytes(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        return stream.ToArray();
    }

    private static XDocument LoadXml(ZipArchive zip, string path)
    {
        using var stream = zip.GetEntry(path)!.Open();
        return XDocument.Load(stream);
    }

    private static void AssertLinkedRelationship(ZipArchive zip, string path)
    {
        var relationship = LoadXml(zip, path).Root!.Elements(Rel + "Relationship")
            .Single(element => element.Attribute("Type")?.Value.EndsWith("/image", StringComparison.Ordinal) == true);
        relationship.Attribute("Target")?.Value.Should().Be(LinkedTarget);
        relationship.Attribute("TargetMode")?.Value.Should().Be("External");
    }

    private static byte[] BuildSourcePackage(bool includeEmbeddedPreview, string linkedTarget = LinkedTarget)
    {
        const string wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
        const string pic = "http://schemas.openxmlformats.org/drawingml/2006/picture";
        const string graphicUri = "http://schemas.openxmlformats.org/drawingml/2006/picture";
        var embedAttribute = includeEmbeddedPreview ? " r:embed=\"rIdEmbedded\"" : string.Empty;
        var documentXml = $"""
            <w:document xmlns:w="{W}" xmlns:r="{R}" xmlns:wp="{wp}" xmlns:a="{A}" xmlns:pic="{pic}">
              <w:body><w:p><w:r><w:drawing><wp:inline>
                <wp:extent cx="1219200" cy="609600"/><wp:docPr id="1" name="Linked picture"/>
                <a:graphic><a:graphicData uri="{graphicUri}"><pic:pic>
                  <pic:nvPicPr><pic:cNvPr id="0" name="Linked picture"/><pic:cNvPicPr/></pic:nvPicPr>
                  <pic:blipFill><a:blip r:link="rIdLinked"{embedAttribute}/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>
                  <pic:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="1219200" cy="609600"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></pic:spPr>
                </pic:pic></a:graphicData></a:graphic>
              </wp:inline></w:drawing></w:r></w:p></w:body>
            </w:document>
            """;
        var embeddedRelationship = includeEmbeddedPreview
            ? "<Relationship Id=\"rIdEmbedded\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"media/image1.png\"/>"
            : string.Empty;
        var relationshipsXml = $"""
            <Relationships xmlns="{Rel}">
              {embeddedRelationship}
              <Relationship Id="rIdLinked" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="{linkedTarget}" TargetMode="External"/>
            </Relationships>
            """;

        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(zip, "word/document.xml", Encoding.UTF8.GetBytes(documentXml));
            AddEntry(zip, "word/_rels/document.xml.rels", Encoding.UTF8.GetBytes(relationshipsXml));
            if (includeEmbeddedPreview)
                AddEntry(zip, "word/media/image1.png", PngBytes());
        }
        return stream.ToArray();
    }

    private static void AddEntry(ZipArchive zip, string path, byte[] bytes)
    {
        var entry = zip.CreateEntry(path);
        using var stream = entry.Open();
        stream.Write(bytes);
    }

    private static byte[] PngBytes() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
    ];
}
