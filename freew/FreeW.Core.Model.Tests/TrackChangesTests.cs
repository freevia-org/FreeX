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

    // --- Tracked changes inside a table nested in a table cell (tc/w:tbl) ---

    [Fact]
    public void HasRevisions_DetectsRevisionsInsideNestedTable()
    {
        var doc = new TextDocument();
        var outerTable = Table.Create(1, 1);
        var nestedTable = Table.Create(1, 1);
        nestedTable.Rows[0].Cells[0].Paragraphs[0].Runs.Add(
            new Run("gone") { Revision = RevisionKind.Deleted, RevisionAuthor = "Eve" });
        outerTable.Rows[0].Cells[0].NestedTables.Add(nestedTable);
        doc.Blocks.Add(outerTable);

        TrackChanges.HasRevisions(doc).Should().BeTrue();
    }

    [Fact]
    public void AcceptAll_ResolvesRunRevisionsInsideNestedTable()
    {
        var doc = new TextDocument();
        var outerTable = Table.Create(1, 1);
        var nestedTable = Table.Create(1, 1);
        var nestedParagraph = nestedTable.Rows[0].Cells[0].Paragraphs[0];
        nestedParagraph.Runs.Add(new Run("keep "));
        nestedParagraph.Runs.Add(new Run("gone") { Revision = RevisionKind.Deleted, RevisionAuthor = "Eve" });
        outerTable.Rows[0].Cells[0].NestedTables.Add(nestedTable);
        doc.Blocks.Add(outerTable);

        TrackChanges.AcceptAll(doc);

        nestedParagraph.PlainText.Should().Be("keep ");
        nestedParagraph.Runs.Should().OnlyContain(r => r.Revision == RevisionKind.None);
        TrackChanges.HasRevisions(doc).Should().BeFalse();
    }

    [Fact]
    public void RejectAll_ResolvesRunRevisionsInsideNestedTable()
    {
        var doc = new TextDocument();
        var outerTable = Table.Create(1, 1);
        var nestedTable = Table.Create(1, 1);
        var nestedParagraph = nestedTable.Rows[0].Cells[0].Paragraphs[0];
        nestedParagraph.Runs.Add(new Run("keep "));
        nestedParagraph.Runs.Add(new Run("added") { Revision = RevisionKind.Inserted, RevisionAuthor = "Eve" });
        outerTable.Rows[0].Cells[0].NestedTables.Add(nestedTable);
        doc.Blocks.Add(outerTable);

        TrackChanges.RejectAll(doc);

        nestedParagraph.PlainText.Should().Be("keep ");
        nestedParagraph.Runs.Should().OnlyContain(r => r.Revision == RevisionKind.None);
        TrackChanges.HasRevisions(doc).Should().BeFalse();
    }

    [Fact]
    public void AcceptAll_OnInsertedRowInsideNestedTable_KeepsTheRow_AndClearsTheRevision()
    {
        var doc = new TextDocument();
        var outerTable = Table.Create(1, 1);
        var nestedTable = Table.Create(2, 1);
        nestedTable.Rows[0].Cells[0].Paragraphs[0].Runs.Add(new Run("keep row"));
        nestedTable.Rows[1].Cells[0].Paragraphs[0].Runs.Add(new Run("tracked row"));
        nestedTable.Rows[1].RowRevision = RevisionKind.Inserted;
        nestedTable.Rows[1].RowRevisionAuthor = "Carol";
        outerTable.Rows[0].Cells[0].NestedTables.Add(nestedTable);
        doc.Blocks.Add(outerTable);

        TrackChanges.AcceptAll(doc);

        nestedTable.Rows.Should().HaveCount(2);
        nestedTable.Rows[1].RowRevision.Should().Be(RevisionKind.None);
        nestedTable.Rows[1].RowRevisionAuthor.Should().BeNull();
        TrackChanges.HasRevisions(doc).Should().BeFalse();
    }

    [Fact]
    public void RejectAll_OnInsertedRowInsideNestedTable_RemovesTheRow()
    {
        var doc = new TextDocument();
        var outerTable = Table.Create(1, 1);
        var nestedTable = Table.Create(2, 1);
        nestedTable.Rows[0].Cells[0].Paragraphs[0].Runs.Add(new Run("keep row"));
        nestedTable.Rows[1].Cells[0].Paragraphs[0].Runs.Add(new Run("tracked row"));
        nestedTable.Rows[1].RowRevision = RevisionKind.Inserted;
        outerTable.Rows[0].Cells[0].NestedTables.Add(nestedTable);
        doc.Blocks.Add(outerTable);

        TrackChanges.RejectAll(doc);

        nestedTable.Rows.Should().ContainSingle();
        nestedTable.Rows[0].Cells[0].PlainText.Should().Be("keep row");
        TrackChanges.HasRevisions(doc).Should().BeFalse();
    }

    [Fact]
    public void AcceptAll_ResolvesRevisionsInsideDoublyNestedTable()
    {
        // A table nested inside a table nested inside a table cell: recursion must not stop at one level.
        var doc = new TextDocument();
        var outerTable = Table.Create(1, 1);
        var middleTable = Table.Create(1, 1);
        var innerTable = Table.Create(1, 1);
        var innerParagraph = innerTable.Rows[0].Cells[0].Paragraphs[0];
        innerParagraph.Runs.Add(new Run("deep gone") { Revision = RevisionKind.Deleted, RevisionAuthor = "Eve" });
        middleTable.Rows[0].Cells[0].NestedTables.Add(innerTable);
        outerTable.Rows[0].Cells[0].NestedTables.Add(middleTable);
        doc.Blocks.Add(outerTable);

        TrackChanges.HasRevisions(doc).Should().BeTrue();

        TrackChanges.AcceptAll(doc);

        innerParagraph.Runs.Should().BeEmpty();
        TrackChanges.HasRevisions(doc).Should().BeFalse();
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

    // --- Paragraph-mark drop with no next Paragraph to merge into (last block / followed by a table) ---
    //
    // A "drop this pilcrow" resolution (accept a deletion, or reject an insertion) normally merges the
    // marked paragraph into the one that follows. When nothing mergeable follows, the merge cannot happen;
    // an empty, unanchored paragraph should vanish outright instead (the same outcome merging into an
    // — absent — next paragraph would have produced), while a paragraph with real content is kept, not
    // silently discarded.

    [Fact]
    public void AcceptAll_OnDeletedParagraphMark_LastBlockInDocument_RemovesTheEmptyParagraph()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("First half"));
        var trailing = new Paragraph("") { MarkRevision = RevisionKind.Deleted, MarkRevisionAuthor = "Dave" };
        doc.Blocks.Add(trailing); // last block: nothing follows to merge into

        TrackChanges.AcceptAll(doc);

        var paragraphs = doc.Blocks.OfType<Paragraph>().ToList();
        paragraphs.Should().ContainSingle();
        paragraphs[0].PlainText.Should().Be("First half");
        TrackChanges.HasRevisions(doc).Should().BeFalse();
    }

    [Fact]
    public void RejectAll_OnInsertedParagraphMark_LastBlockInDocument_RemovesTheEmptyParagraph()
    {
        // Symmetric case: rejecting an inserted pilcrow with nothing to merge into (e.g. an accidental
        // trailing Enter at the very end of the document) undoes the insert by dropping the empty paragraph.
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body text"));
        var trailing = new Paragraph("") { MarkRevision = RevisionKind.Inserted, MarkRevisionAuthor = "Dave" };
        doc.Blocks.Add(trailing);

        TrackChanges.RejectAll(doc);

        var paragraphs = doc.Blocks.OfType<Paragraph>().ToList();
        paragraphs.Should().ContainSingle();
        paragraphs[0].PlainText.Should().Be("Body text");
        TrackChanges.HasRevisions(doc).Should().BeFalse();
    }

    [Fact]
    public void AcceptAll_OnDeletedParagraphMark_FollowedByTable_RemovesTheEmptyParagraph()
    {
        // The literal "followed by a table" case: a pilcrow cannot merge text into a table cell, so an
        // empty caption-style paragraph immediately before a table is removed outright on accept.
        var doc = new TextDocument();
        var caption = new Paragraph("") { MarkRevision = RevisionKind.Deleted };
        doc.Blocks.Add(caption);
        var table = Table.Create(1, 1);
        doc.Blocks.Add(table);

        TrackChanges.AcceptAll(doc);

        doc.Blocks.Should().ContainSingle();
        doc.Blocks[0].Should().BeSameAs(table);
        TrackChanges.HasRevisions(doc).Should().BeFalse();
    }

    [Fact]
    public void AcceptAll_OnDeletedParagraphMark_FollowedByTable_WithVisibleText_KeepsTheParagraph()
    {
        // Sibling/no-regression: the fix must not widen into discarding a paragraph that still carries
        // visible text just because it cannot merge forward — the text has nowhere safe to go, so it stays.
        var doc = new TextDocument();
        var caption = new Paragraph("Table 1: Results") { MarkRevision = RevisionKind.Deleted };
        doc.Blocks.Add(caption);
        var table = Table.Create(1, 1);
        doc.Blocks.Add(table);

        TrackChanges.AcceptAll(doc);

        doc.Blocks.Should().HaveCount(2);
        doc.Blocks[0].Should().BeSameAs(caption);
        caption.PlainText.Should().Be("Table 1: Results");
        caption.MarkRevision.Should().Be(RevisionKind.None);
        TrackChanges.HasRevisions(doc).Should().BeFalse();
    }

    [Fact]
    public void AcceptAll_OnDeletedParagraphMark_EmptyButBookmarked_KeepsTheParagraph_AndThePreservesBookmark()
    {
        // Sibling/no-regression: an otherwise-empty paragraph that still anchors a bookmark must not be
        // silently deleted — that would destroy the bookmark, not just resolve the tracked change.
        var doc = new TextDocument();
        var anchor = new Paragraph("") { MarkRevision = RevisionKind.Deleted, BookmarkName = "anchor" };
        doc.Blocks.Add(anchor); // last block, nothing to merge into

        TrackChanges.AcceptAll(doc);

        doc.Blocks.Should().ContainSingle();
        anchor.BookmarkName.Should().Be("anchor");
        anchor.MarkRevision.Should().Be(RevisionKind.None);
    }

    [Fact]
    public void AcceptAll_OnDeletedParagraphMark_OnlyParagraphInCell_IsKept_NotRemoved()
    {
        // Cell-nested counterpart: OOXML requires every table cell to keep at least one paragraph, so
        // dropping the cell's *only* paragraph must not empty the cell even when it is otherwise empty.
        var doc = new TextDocument();
        var table = Table.Create(1, 1);
        var onlyParagraph = table.Rows[0].Cells[0].Paragraphs[0];
        onlyParagraph.MarkRevision = RevisionKind.Deleted;
        doc.Blocks.Add(table);

        TrackChanges.AcceptAll(doc);

        table.Rows[0].Cells[0].Paragraphs.Should().ContainSingle();
        table.Rows[0].Cells[0].Paragraphs[0].MarkRevision.Should().Be(RevisionKind.None);
    }

    [Fact]
    public void AcceptAll_OnDeletedParagraphMark_LastOfSeveralParagraphsInCell_IsRemoved()
    {
        // Cell-nested counterpart to the top-level "last block, empty" fix: when the cell has more than
        // one paragraph, dropping the trailing empty one removes it (it is not the cell's only paragraph).
        var doc = new TextDocument();
        var table = Table.Create(1, 1);
        var cell = table.Rows[0].Cells[0];
        cell.Paragraphs[0] = new Paragraph("Kept text");
        var trailing = new Paragraph("") { MarkRevision = RevisionKind.Deleted };
        cell.Paragraphs.Add(trailing);
        doc.Blocks.Add(table);

        TrackChanges.AcceptAll(doc);

        cell.Paragraphs.Should().ContainSingle();
        cell.Paragraphs[0].PlainText.Should().Be("Kept text");
    }
}
