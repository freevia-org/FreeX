using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-trip tests for tracked paragraph-MARK changes (Word's <c>w:pPr/w:rPr/w:ins</c> and
/// <c>w:pPr/w:rPr/w:del</c>): a tracked Enter (paragraph split) or Backspace/Delete (paragraph merge) marks
/// the paragraph mark itself, independent of any run-level revisions on the paragraph's own runs. The
/// marker must serialise inside a <c>w:pPr/w:rPr</c> placed right before <c>w:sectPr</c>/<c>w:pPrChange</c>
/// (CT_PPr schema order) and survive a load -> save -> load cycle intact on
/// <see cref="Paragraph.MarkRevision"/>/<see cref="Paragraph.MarkRevisionAuthor"/>/<see cref="Paragraph.MarkRevisionDateXml"/>.
/// </summary>
public class ParagraphMarkRevisionRoundTripTests
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

    private static TextDocument BuildDocumentWithMarkedParagraph(RevisionKind kind)
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("plain paragraph, no mark revision"));
        doc.Blocks.Add(new Paragraph("split or merged here")
        {
            MarkRevision = kind,
            MarkRevisionAuthor = "Dana",
            MarkRevisionDateXml = "2026-07-30T14:00:00Z"
        });
        return doc;
    }

    [Theory]
    [InlineData(RevisionKind.Inserted, "ins")]
    [InlineData(RevisionKind.Deleted, "del")]
    public void MarkedParagraph_SerialisesAsRPrIns_OrDel_BeforeSectPrAndPPrChange(RevisionKind kind, string elementName)
    {
        var xml = WriteDocumentXml(BuildDocumentWithMarkedParagraph(kind));
        var paragraphs = xml.Descendants(W + "p").ToList();
        paragraphs.Should().HaveCount(2);

        // The first (ordinary) paragraph carries no pPr/rPr marker at all.
        paragraphs[0].Element(W + "pPr")?.Element(W + "rPr").Should().BeNull();

        // The second (marked) paragraph carries a pPr/rPr whose only child is the tracked-change marker.
        var pPr = paragraphs[1].Element(W + "pPr")!;
        var rPr = pPr.Element(W + "rPr")!;
        rPr.Elements().Single().Name.Should().Be(W + elementName);
        var marker = rPr.Element(W + elementName)!;
        marker.Attribute(W + "author")!.Value.Should().Be("Dana");
        marker.Attribute(W + "date")!.Value.Should().Be("2026-07-30T14:00:00Z");
        marker.Attribute(W + "id").Should().NotBeNull();
    }

    [Theory]
    [InlineData(RevisionKind.Inserted)]
    [InlineData(RevisionKind.Deleted)]
    public void MarkedParagraph_RoundTrips_PreservingKindAuthorAndDate(RevisionKind kind)
    {
        var reloaded = RoundTrip(BuildDocumentWithMarkedParagraph(kind));
        var paragraphs = reloaded.Paragraphs.ToList();

        paragraphs[0].MarkRevision.Should().Be(RevisionKind.None);
        paragraphs[0].MarkRevisionAuthor.Should().BeNull();

        paragraphs[1].MarkRevision.Should().Be(kind);
        paragraphs[1].MarkRevisionAuthor.Should().Be("Dana");
        paragraphs[1].MarkRevisionDateXml.Should().Be("2026-07-30T14:00:00Z");
        // The paragraph's own text/runs are unaffected by the mark-only revision.
        paragraphs[1].PlainText.Should().Be("split or merged here");
    }

    // Sibling no-regression: a paragraph whose RUNS carry ordinary insert/delete revisions but whose mark
    // is untouched must not gain a spurious pPr/rPr, and its run-level marks must be unaffected by the new
    // mark-revision code path.
    [Fact]
    public void ParagraphWithOnlyRunRevisions_HasNoMarkRevision_AndEmitsNoPPrRPr()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("inserted text") { Revision = RevisionKind.Inserted, RevisionAuthor = "Eve" });
        doc.Blocks.Add(paragraph);

        var xml = WriteDocumentXml(doc);
        xml.Descendants(W + "p").Single().Element(W + "pPr")?.Element(W + "rPr").Should().BeNull();
        // The run-level w:ins is still present and untouched.
        xml.Descendants(W + "ins").Should().HaveCount(1);

        var reloaded = RoundTrip(doc);
        var reloadedParagraph = reloaded.Paragraphs.Single();
        reloadedParagraph.MarkRevision.Should().Be(RevisionKind.None);
        reloadedParagraph.Runs.Single().Revision.Should().Be(RevisionKind.Inserted);
        reloadedParagraph.Runs.Single().RevisionAuthor.Should().Be("Eve");
    }

    [Fact]
    public void MarkedParagraph_WithSectionBreak_EmitsRPrBeforeSectPr()
    {
        // A non-final section's w:sectPr is the LAST child of w:pPr (CT_PPr order); the paragraph mark's
        // rPr must still precede it, per CT_PPr's rPr-then-sectPr-then-pPrChange sequence.
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("end of section")
        {
            MarkRevision = RevisionKind.Deleted,
            MarkRevisionAuthor = "Frank",
            SectionBreak = new Section(new PageSettings())
        });
        doc.Blocks.Add(new Paragraph("after"));

        var pPr = WriteDocumentXml(doc).Descendants(W + "pPr")
            .First(e => e.Element(W + "sectPr") is not null);

        var rPrIndex = pPr.Elements().ToList().FindIndex(e => e.Name == W + "rPr");
        var sectPrIndex = pPr.Elements().ToList().FindIndex(e => e.Name == W + "sectPr");
        rPrIndex.Should().BeGreaterThanOrEqualTo(0);
        sectPrIndex.Should().BeGreaterThanOrEqualTo(0);
        rPrIndex.Should().BeLessThan(sectPrIndex);

        var reloaded = RoundTrip(doc);
        var reloadedParagraph = reloaded.Paragraphs.First();
        reloadedParagraph.MarkRevision.Should().Be(RevisionKind.Deleted);
        reloadedParagraph.SectionBreak.Should().NotBeNull();
    }

    [Fact]
    public void OrdinaryParagraph_HasNoMarkRevision_AndEmitsNoPPrRPr()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("no revisions"));

        WriteDocumentXml(doc).Descendants(W + "p").Single().Element(W + "pPr")?.Element(W + "rPr").Should().BeNull();
        RoundTrip(doc).Paragraphs.First().MarkRevision.Should().Be(RevisionKind.None);
    }
}
