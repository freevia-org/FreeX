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

    // --- Tracked formatting changes (w:rPrChange) ---

    private static TextDocument BuildFormatRevisionDocument()
    {
        // Run is now bold (new formatting); it was previously plain, changed by Alice.
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("styled", new RunFormatting { Bold = true })
        {
            FormatRevision = new FormatRevision(RunFormatting.Default, "Alice", "2026-06-19T09:00:00Z")
        });
        doc.Blocks.Add(paragraph);
        return doc;
    }

    [Fact]
    public void HasRevisions_DetectsFormatOnlyRevision()
    {
        TrackChanges.HasRevisions(BuildFormatRevisionDocument()).Should().BeTrue();
    }

    [Fact]
    public void AcceptAll_KeepsNewFormatting_AndClearsFormatRevision()
    {
        var doc = BuildFormatRevisionDocument();

        TrackChanges.AcceptAll(doc);

        var run = doc.Paragraphs.First().Runs.First();
        // Accept keeps the current (new, bold) formatting and drops the mark.
        run.Formatting.Bold.Should().BeTrue();
        run.FormatRevision.Should().BeNull();
        TrackChanges.HasRevisions(doc).Should().BeFalse();
    }

    [Fact]
    public void RejectAll_RestoresPreviousFormatting_AndClearsFormatRevision()
    {
        var doc = BuildFormatRevisionDocument();

        TrackChanges.RejectAll(doc);

        var run = doc.Paragraphs.First().Runs.First();
        // Reject restores the previous (plain) formatting and drops the mark; the text is unchanged.
        run.Text.Should().Be("styled");
        run.Formatting.Bold.Should().BeFalse();
        run.FormatRevision.Should().BeNull();
        TrackChanges.HasRevisions(doc).Should().BeFalse();
    }

    [Fact]
    public void RejectAll_OnInsertedRunWithFormatRevision_RemovesRunEntirely()
    {
        // A run that is BOTH a tracked insertion and a tracked formatting change: rejecting the insertion
        // removes the run, so the formatting reject is moot (no run left to restore).
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("ins", new RunFormatting { Bold = true })
        {
            Revision = RevisionKind.Inserted,
            RevisionAuthor = "Alice",
            FormatRevision = new FormatRevision(RunFormatting.Default, "Alice", null)
        });
        doc.Blocks.Add(paragraph);

        TrackChanges.RejectAll(doc);

        doc.Paragraphs.First().Runs.Should().BeEmpty();
        TrackChanges.HasRevisions(doc).Should().BeFalse();
    }

    // --- Tracked row changes (w:trPr/w:ins, w:trPr/w:del) ---

    private static TextDocument BuildRowRevisionDocument(RevisionKind rowRevision)
    {
        var doc = new TextDocument();
        var table = Table.Create(2, 1);
        table.Rows[0].Cells[0].Paragraphs[0].Runs.Add(new Run("keep row"));
        table.Rows[1].Cells[0].Paragraphs[0].Runs.Add(new Run("tracked row"));
        table.Rows[1].RowRevision = rowRevision;
        table.Rows[1].RowRevisionAuthor = "Carol";
        table.Rows[1].RowRevisionDateXml = "2026-07-03T09:00:00Z";
        doc.Blocks.Add(table);
        return doc;
    }

    [Fact]
    public void HasRevisions_DetectsRowOnlyRevision()
    {
        TrackChanges.HasRevisions(BuildRowRevisionDocument(RevisionKind.Inserted)).Should().BeTrue();
        TrackChanges.HasRevisions(BuildRowRevisionDocument(RevisionKind.Deleted)).Should().BeTrue();
    }

    [Fact]
    public void AcceptAll_OnInsertedRow_KeepsTheRow_AndClearsTheRevision()
    {
        var doc = BuildRowRevisionDocument(RevisionKind.Inserted);

        TrackChanges.AcceptAll(doc);

        var table = doc.Blocks.OfType<Table>().Single();
        table.Rows.Should().HaveCount(2);
        table.Rows[1].RowRevision.Should().Be(RevisionKind.None);
        table.Rows[1].RowRevisionAuthor.Should().BeNull();
        table.Rows[1].RowRevisionDateXml.Should().BeNull();
        table.Rows[1].Cells[0].PlainText.Should().Be("tracked row");
        TrackChanges.HasRevisions(doc).Should().BeFalse();
    }

    [Fact]
    public void RejectAll_OnInsertedRow_RemovesTheRow()
    {
        var doc = BuildRowRevisionDocument(RevisionKind.Inserted);

        TrackChanges.RejectAll(doc);

        var table = doc.Blocks.OfType<Table>().Single();
        table.Rows.Should().ContainSingle();
        table.Rows[0].Cells[0].PlainText.Should().Be("keep row");
        TrackChanges.HasRevisions(doc).Should().BeFalse();
    }

    [Fact]
    public void AcceptAll_OnDeletedRow_RemovesTheRow()
    {
        var doc = BuildRowRevisionDocument(RevisionKind.Deleted);

        TrackChanges.AcceptAll(doc);

        var table = doc.Blocks.OfType<Table>().Single();
        table.Rows.Should().ContainSingle();
        table.Rows[0].Cells[0].PlainText.Should().Be("keep row");
        TrackChanges.HasRevisions(doc).Should().BeFalse();
    }

    [Fact]
    public void RejectAll_OnDeletedRow_KeepsTheRow_AndClearsTheRevision()
    {
        var doc = BuildRowRevisionDocument(RevisionKind.Deleted);

        TrackChanges.RejectAll(doc);

        var table = doc.Blocks.OfType<Table>().Single();
        table.Rows.Should().HaveCount(2);
        table.Rows[1].RowRevision.Should().Be(RevisionKind.None);
        table.Rows[1].RowRevisionAuthor.Should().BeNull();
        table.Rows[1].RowRevisionDateXml.Should().BeNull();
        table.Rows[1].Cells[0].PlainText.Should().Be("tracked row");
        TrackChanges.HasRevisions(doc).Should().BeFalse();
    }

    // --- Tracked paragraph-mark changes (w:pPr/w:rPr/w:ins, w:pPr/w:rPr/w:del) ---

    private static TextDocument BuildMarkRevisionDocument(RevisionKind markRevision)
    {
        var doc = new TextDocument();
        var first = new Paragraph("First half");
        first.MarkRevision = markRevision;
        first.MarkRevisionAuthor = "Dave";
        first.MarkRevisionDateXml = "2026-07-04T08:00:00Z";
        doc.Blocks.Add(first);
        doc.Blocks.Add(new Paragraph("Second half"));
        return doc;
    }

    [Fact]
    public void HasRevisions_DetectsParagraphMarkOnlyRevision()
    {
        TrackChanges.HasRevisions(BuildMarkRevisionDocument(RevisionKind.Inserted)).Should().BeTrue();
        TrackChanges.HasRevisions(BuildMarkRevisionDocument(RevisionKind.Deleted)).Should().BeTrue();
    }

    [Fact]
    public void AcceptAll_OnInsertedParagraphMark_KeepsTheSplit_AndClearsTheRevision()
    {
        // Accepting an inserted paragraph mark keeps the tracked Enter: the split stands as two paragraphs.
        var doc = BuildMarkRevisionDocument(RevisionKind.Inserted);

        TrackChanges.AcceptAll(doc);

        var paragraphs = doc.Blocks.OfType<Paragraph>().ToList();
        paragraphs.Should().HaveCount(2);
        paragraphs[0].PlainText.Should().Be("First half");
        paragraphs[1].PlainText.Should().Be("Second half");
        paragraphs[0].MarkRevision.Should().Be(RevisionKind.None);
        paragraphs[0].MarkRevisionAuthor.Should().BeNull();
        paragraphs[0].MarkRevisionDateXml.Should().BeNull();
        TrackChanges.HasRevisions(doc).Should().BeFalse();
    }

    [Fact]
    public void RejectAll_OnInsertedParagraphMark_UndoesTheSplit_AndMergesTheParagraphs()
    {
        // Rejecting an inserted paragraph mark undoes the tracked Enter: the two paragraphs merge back
        // into one (taking the surviving paragraph's identity).
        var doc = BuildMarkRevisionDocument(RevisionKind.Inserted);

        TrackChanges.RejectAll(doc);

        var paragraphs = doc.Blocks.OfType<Paragraph>().ToList();
        paragraphs.Should().ContainSingle();
        paragraphs[0].PlainText.Should().Be("First halfSecond half");
        paragraphs[0].MarkRevision.Should().Be(RevisionKind.None);
        TrackChanges.HasRevisions(doc).Should().BeFalse();
    }

    [Fact]
    public void AcceptAll_OnDeletedParagraphMark_MergesTheParagraphs()
    {
        // Accepting a deleted paragraph mark performs the tracked Backspace/Delete merge for real.
        var doc = BuildMarkRevisionDocument(RevisionKind.Deleted);

        TrackChanges.AcceptAll(doc);

        var paragraphs = doc.Blocks.OfType<Paragraph>().ToList();
        paragraphs.Should().ContainSingle();
        paragraphs[0].PlainText.Should().Be("First halfSecond half");
        paragraphs[0].MarkRevision.Should().Be(RevisionKind.None);
        TrackChanges.HasRevisions(doc).Should().BeFalse();
    }

    [Fact]
    public void RejectAll_OnDeletedParagraphMark_KeepsTheParagraphsSeparate_AndClearsTheRevision()
    {
        // Rejecting a deleted paragraph mark restores the pilcrow: the two paragraphs stay separate.
        var doc = BuildMarkRevisionDocument(RevisionKind.Deleted);

        TrackChanges.RejectAll(doc);

        var paragraphs = doc.Blocks.OfType<Paragraph>().ToList();
        paragraphs.Should().HaveCount(2);
        paragraphs[0].PlainText.Should().Be("First half");
        paragraphs[1].PlainText.Should().Be("Second half");
        paragraphs[0].MarkRevision.Should().Be(RevisionKind.None);
        TrackChanges.HasRevisions(doc).Should().BeFalse();
    }
}
