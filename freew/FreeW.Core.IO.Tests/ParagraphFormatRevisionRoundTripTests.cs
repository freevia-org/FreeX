using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-trip tests for tracked paragraph-formatting changes (Word's w:pPrChange): a paragraph whose
/// formatting (e.g. alignment) was changed under Track Changes carries the previous formatting plus
/// author/date in a w:pPrChange element, which must serialise as the last child of w:pPr and survive
/// a load → save → load cycle intact on <see cref="Paragraph.ParagraphFormatRevision"/>.
/// </summary>
public class ParagraphFormatRevisionRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static TextDocument RoundTrip(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    private static XDocument WriteDocumentXml(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/document.xml")!.Open();
        return XDocument.Load(entry);
    }

    // The paragraph is now centre-aligned (the new formatting); it was previously left-aligned (the
    // previous formatting), and Alice made the change under Track Changes.
    private static TextDocument BuildDocumentWithParagraphFormatRevision()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph("hello");
        paragraph.Formatting = new ParagraphFormatting { Alignment = TextAlignment.Center };
        paragraph.ParagraphFormatRevision = new ParagraphFormatRevision(
            ParagraphFormatting.Default, // previous: left-aligned (the default)
            "Alice",
            "2026-06-26T10:00:00Z");
        doc.Blocks.Add(paragraph);
        return doc;
    }

    [Fact]
    public void ParagraphFormatRevision_SerialisesAsPPrChange_AsLastChildOfPPr()
    {
        var pPr = WriteDocumentXml(BuildDocumentWithParagraphFormatRevision())
            .Descendants(W + "pPr")
            // The paragraph's own pPr (the one that directly hosts pPrChange), not the nested previous-pPr.
            .First(e => e.Element(W + "pPrChange") is not null);

        // pPrChange is the LAST child of w:pPr (CT_PPr schema requirement).
        pPr.Elements().Last().Name.Should().Be(W + "pPrChange");

        var change = pPr.Element(W + "pPrChange")!;
        change.Attribute(W + "author")!.Value.Should().Be("Alice");
        change.Attribute(W + "date")!.Value.Should().Be("2026-06-26T10:00:00Z");
        change.Attribute(W + "id").Should().NotBeNull();
        // The nested w:pPr carries the previous (default/left) formatting: no w:jc element.
        change.Element(W + "pPr").Should().NotBeNull();
        change.Element(W + "pPr")!.Element(W + "jc").Should().BeNull();
    }

    [Fact]
    public void ParagraphFormatRevision_RoundTrips_PreservingPreviousFormattingAndAuthor()
    {
        var reloaded = RoundTrip(BuildDocumentWithParagraphFormatRevision());

        var paragraph = reloaded.Paragraphs.First();
        // Current formatting is the new (centre) alignment.
        paragraph.Formatting.Alignment.Should().Be(TextAlignment.Center);
        // The paragraph format revision survived with its previous (left/default) formatting and metadata.
        paragraph.ParagraphFormatRevision.Should().NotBeNull();
        paragraph.ParagraphFormatRevision!.PreviousParagraphFormatting.Alignment.Should().Be(TextAlignment.Left);
        paragraph.ParagraphFormatRevision.Author.Should().Be("Alice");
        paragraph.ParagraphFormatRevision.DateXml.Should().Be("2026-06-26T10:00:00Z");
    }

    [Fact]
    public void ParagraphFormatRevision_RoundTrips_WhenPreviousFormattingIsDefault()
    {
        // Previous formatting is fully default — the nested pPr should be empty (but present).
        var doc = new TextDocument();
        var paragraph = new Paragraph("text");
        paragraph.Formatting = new ParagraphFormatting { Alignment = TextAlignment.Right, IndentLeftPt = 36 };
        paragraph.ParagraphFormatRevision = new ParagraphFormatRevision(
            ParagraphFormatting.Default, "Bob", null);
        doc.Blocks.Add(paragraph);

        var xml = WriteDocumentXml(doc);
        var change = xml.Descendants(W + "pPrChange").First();
        // Even with default previous formatting, the nested pPr is present (possibly empty).
        change.Element(W + "pPr").Should().NotBeNull();
        change.Attribute(W + "author")!.Value.Should().Be("Bob");

        var reloaded = RoundTrip(doc);
        var reloadedPara = reloaded.Paragraphs.First();
        reloadedPara.Formatting.Alignment.Should().Be(TextAlignment.Right);
        reloadedPara.ParagraphFormatRevision.Should().NotBeNull();
        reloadedPara.ParagraphFormatRevision!.PreviousParagraphFormatting.Alignment.Should().Be(TextAlignment.Left);
        reloadedPara.ParagraphFormatRevision.Author.Should().Be("Bob");
    }

    [Fact]
    public void ParagraphFormatRevision_RoundTrips_WithIndentAndSpacingChange()
    {
        // Paragraph whose indent was tracked: previously indented, now at default.
        var doc = new TextDocument();
        var paragraph = new Paragraph("indented before");
        paragraph.Formatting = new ParagraphFormatting { Alignment = TextAlignment.Left };
        var previousFormatting = new ParagraphFormatting
        {
            Alignment = TextAlignment.Justify,
            IndentLeftPt = 36,
            SpaceBeforePt = 12,
            SpaceAfterPt = 6
        };
        paragraph.ParagraphFormatRevision = new ParagraphFormatRevision(
            previousFormatting, "Carol", "2026-06-26T11:00:00Z");
        doc.Blocks.Add(paragraph);

        var reloaded = RoundTrip(doc);
        var rev = reloaded.Paragraphs.First().ParagraphFormatRevision!;
        rev.PreviousParagraphFormatting.Alignment.Should().Be(TextAlignment.Justify);
        rev.PreviousParagraphFormatting.IndentLeftPt.Should().BeApproximately(36, 0.5);
        rev.PreviousParagraphFormatting.SpaceBeforePt.Should().BeApproximately(12, 0.5);
        rev.PreviousParagraphFormatting.SpaceAfterPt.Should().BeApproximately(6, 0.5);
        rev.Author.Should().Be("Carol");
        rev.DateXml.Should().Be("2026-06-26T11:00:00Z");
    }

    [Fact]
    public void OrdinaryParagraph_HasNoParagraphFormatRevision_AndEmitsNoPPrChange()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("no revisions"));

        WriteDocumentXml(doc).Descendants(W + "pPrChange").Should().BeEmpty();
        RoundTrip(doc).Paragraphs.First().ParagraphFormatRevision.Should().BeNull();
    }

    [Fact]
    public void TrackChanges_HasRevisions_DetectsParagraphFormatRevision()
    {
        var doc = BuildDocumentWithParagraphFormatRevision();
        TrackChanges.HasRevisions(doc).Should().BeTrue();

        var docNoRevisions = new TextDocument();
        docNoRevisions.Blocks.Add(new Paragraph("clean"));
        TrackChanges.HasRevisions(docNoRevisions).Should().BeFalse();
    }

    [Fact]
    public void TrackChanges_AcceptAll_KeepsNewParagraphFormattingAndClearsMark()
    {
        var doc = BuildDocumentWithParagraphFormatRevision();
        TrackChanges.AcceptAll(doc);

        var paragraph = doc.Paragraphs.First();
        paragraph.Formatting.Alignment.Should().Be(TextAlignment.Center); // new (current) formatting kept
        paragraph.ParagraphFormatRevision.Should().BeNull();               // mark cleared
    }

    [Fact]
    public void TrackChanges_RejectAll_RestoresPreviousParagraphFormattingAndClearsMark()
    {
        var doc = BuildDocumentWithParagraphFormatRevision();
        TrackChanges.RejectAll(doc);

        var paragraph = doc.Paragraphs.First();
        paragraph.Formatting.Alignment.Should().Be(TextAlignment.Left);  // previous (left) formatting restored
        paragraph.ParagraphFormatRevision.Should().BeNull();              // mark cleared
    }
}
