namespace FreeW.Core.Model.Tests;

public class TrackChangesTests
{
    private static TextDocument BuildDocument()
    {
        // "Keep " + [inserted "added "] + [deleted "removed "] + "tail"
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Keep "));
        paragraph.Runs.Add(new Run("added ") { Revision = RevisionKind.Inserted, RevisionAuthor = "Alice", RevisionDateXml = "2026-06-17T10:00:00Z" });
        paragraph.Runs.Add(new Run("removed ") { Revision = RevisionKind.Deleted, RevisionAuthor = "Bob", RevisionDateXml = "2026-06-17T11:00:00Z" });
        paragraph.Runs.Add(new Run("tail"));
        doc.Blocks.Add(paragraph);
        return doc;
    }

    [Fact]
    public void HasRevisions_DetectsTrackedChanges()
    {
        TrackChanges.HasRevisions(BuildDocument()).Should().BeTrue();

        var plain = new TextDocument();
        plain.Blocks.Add(new Paragraph("No changes here"));
        TrackChanges.HasRevisions(plain).Should().BeFalse();
    }

    [Fact]
    public void AcceptAll_NormalizesInsertions_AndRemovesDeletions()
    {
        var doc = BuildDocument();

        TrackChanges.AcceptAll(doc);

        var paragraph = doc.Paragraphs.First();
        // The deletion is gone; the insertion text stays.
        paragraph.PlainText.Should().Be("Keep added tail");
        // Every remaining run is ordinary (no revision marks left).
        paragraph.Runs.Should().OnlyContain(r => r.Revision == RevisionKind.None);
        paragraph.Runs.Should().OnlyContain(r => r.RevisionAuthor == null && r.RevisionDateXml == null);
        TrackChanges.HasRevisions(doc).Should().BeFalse();
    }

    [Fact]
    public void RejectAll_RemovesInsertions_AndNormalizesDeletions()
    {
        var doc = BuildDocument();

        TrackChanges.RejectAll(doc);

        var paragraph = doc.Paragraphs.First();
        // The insertion is gone; the deletion text is restored to ordinary text.
        paragraph.PlainText.Should().Be("Keep removed tail");
        paragraph.Runs.Should().OnlyContain(r => r.Revision == RevisionKind.None);
        paragraph.Runs.Should().OnlyContain(r => r.RevisionAuthor == null && r.RevisionDateXml == null);
        TrackChanges.HasRevisions(doc).Should().BeFalse();
    }

    [Fact]
    public void AcceptAll_ResolvesRevisionsInsideTableCells()
    {
        var doc = new TextDocument();
        var table = Table.Create(1, 1);
        var cellParagraph = table.Rows[0].Cells[0].Paragraphs[0];
        cellParagraph.Runs.Add(new Run("keep "));
        cellParagraph.Runs.Add(new Run("gone") { Revision = RevisionKind.Deleted, RevisionAuthor = "Eve" });
        doc.Blocks.Add(table);

        TrackChanges.AcceptAll(doc);

        cellParagraph.PlainText.Should().Be("keep ");
        cellParagraph.Runs.Should().OnlyContain(r => r.Revision == RevisionKind.None);
    }
}
