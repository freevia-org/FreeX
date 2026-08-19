namespace FreeW.Core.Model.Tests;

public class RevisionListTests
{
    private static TextDocument BuildDocument()
    {
        // Paragraph 0: "Keep " + [inserted "added "] + [deleted "removed "] + "tail"
        // Paragraph 1: "plain" + [inserted "more"]
        var doc = new TextDocument();
        var p0 = new Paragraph();
        p0.Runs.Add(new Run("Keep "));
        p0.Runs.Add(new Run("added ") { Revision = RevisionKind.Inserted, RevisionAuthor = "Alice", RevisionDateXml = "2026-06-17T10:00:00Z" });
        p0.Runs.Add(new Run("removed ") { Revision = RevisionKind.Deleted, RevisionAuthor = "Bob", RevisionDateXml = "2026-06-17T11:00:00Z" });
        p0.Runs.Add(new Run("tail"));
        doc.Blocks.Add(p0);

        var p1 = new Paragraph();
        p1.Runs.Add(new Run("plain"));
        p1.Runs.Add(new Run("more") { Revision = RevisionKind.Inserted, RevisionAuthor = "Carol" });
        doc.Blocks.Add(p1);
        return doc;
    }

    [Fact]
    public void Enumerate_ListsEveryRevisionInReadingOrder()
    {
        var entries = RevisionList.Enumerate(BuildDocument());

        entries.Should().HaveCount(3);

        entries[0].Kind.Should().Be(RevisionEntryKind.Insertion);
        entries[0].Author.Should().Be("Alice");
        entries[0].Text.Should().Be("added ");
        entries[0].BlockIndex.Should().Be(0);
        entries[0].DateXml.Should().Be("2026-06-17T10:00:00Z");

        entries[1].Kind.Should().Be(RevisionEntryKind.Deletion);
        entries[1].Author.Should().Be("Bob");
        entries[1].Text.Should().Be("removed ");
        entries[1].BlockIndex.Should().Be(0);

        entries[2].Kind.Should().Be(RevisionEntryKind.Insertion);
        entries[2].Author.Should().Be("Carol");
        entries[2].BlockIndex.Should().Be(1);
    }

    [Fact]
    public void Enumerate_OnPlainDocument_IsEmpty()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("nothing tracked here"));
        RevisionList.Enumerate(doc).Should().BeEmpty();
    }

    [Fact]
    public void Accept_SingleInsertion_NormalizesThatRunOnly()
    {
        var doc = BuildDocument();
        var insertion = RevisionList.Enumerate(doc)[0];

        RevisionList.Accept(doc, insertion).Should().BeTrue();

        var p0 = doc.Paragraphs.First();
        // Inserted run is now ordinary text; the deletion is still pending.
        p0.PlainText.Should().Be("Keep added removed tail");
        var added = p0.Runs.Single(r => r.Text == "added ");
        added.Revision.Should().Be(RevisionKind.None);
        added.RevisionAuthor.Should().BeNull();
        // Other revisions untouched: deletion + second-paragraph insertion remain.
        RevisionList.Enumerate(doc).Should().HaveCount(2);
    }

    [Fact]
    public void Reject_SingleInsertion_RemovesThatRunOnly()
    {
        var doc = BuildDocument();
        var insertion = RevisionList.Enumerate(doc)[0];

        RevisionList.Reject(doc, insertion).Should().BeTrue();

        var p0 = doc.Paragraphs.First();
        // The inserted run is gone; the deletion text stays (still pending).
        p0.PlainText.Should().Be("Keep removed tail");
        RevisionList.Enumerate(doc).Should().HaveCount(2);
    }

    [Fact]
    public void Accept_SingleDeletion_RemovesThatRunOnly()
    {
        var doc = BuildDocument();
        var deletion = RevisionList.Enumerate(doc)[1];

        RevisionList.Accept(doc, deletion).Should().BeTrue();

        var p0 = doc.Paragraphs.First();
        // The deletion is applied (run removed); the insertion is still pending.
        p0.PlainText.Should().Be("Keep added tail");
        RevisionList.Enumerate(doc).Should().HaveCount(2);
    }

    [Fact]
    public void Reject_SingleDeletion_RestoresThatRunAsOrdinaryText()
    {
        var doc = BuildDocument();
        var deletion = RevisionList.Enumerate(doc)[1];

        RevisionList.Reject(doc, deletion).Should().BeTrue();

        var p0 = doc.Paragraphs.First();
        var restored = p0.Runs.Single(r => r.Text == "removed ");
        restored.Revision.Should().Be(RevisionKind.None);
        restored.RevisionAuthor.Should().BeNull();
        RevisionList.Enumerate(doc).Should().HaveCount(2);
    }

    [Fact]
    public void AcceptThenRejectEach_LeavesDocumentClean()
    {
        // Resolving every entry one at a time (re-enumerating between, as the pane does) ends with no
        // pending revisions — Previous/Next navigation walks exactly these entries.
        var doc = BuildDocument();

        // Accept the first remaining each pass until none remain.
        while (RevisionList.Enumerate(doc) is { Count: > 0 } list)
            RevisionList.Accept(doc, list[0]).Should().BeTrue();

        TrackChanges.HasRevisions(doc).Should().BeFalse();
    }

    // --- Formatting revisions (w:rPrChange) ---

    [Fact]
    public void Enumerate_IncludesFormattingRevision()
    {
        var doc = new TextDocument();
        var p = new Paragraph();
        p.Runs.Add(new Run("styled", new RunFormatting { Bold = true })
        {
            FormatRevision = new FormatRevision(RunFormatting.Default, "Alice", "2026-06-19T09:00:00Z")
        });
        doc.Blocks.Add(p);

        var entries = RevisionList.Enumerate(doc);
        entries.Should().ContainSingle();
        entries[0].Kind.Should().Be(RevisionEntryKind.Formatting);
        entries[0].Author.Should().Be("Alice");
        entries[0].Text.Should().Be("styled");
    }

    [Fact]
    public void Accept_FormattingRevision_KeepsNewFormatting()
    {
        var doc = new TextDocument();
        var p = new Paragraph();
        p.Runs.Add(new Run("styled", new RunFormatting { Bold = true })
        {
            FormatRevision = new FormatRevision(RunFormatting.Default, "Alice", null)
        });
        doc.Blocks.Add(p);
        var entry = RevisionList.Enumerate(doc)[0];

        RevisionList.Accept(doc, entry).Should().BeTrue();

        var run = doc.Paragraphs.First().Runs.First();
        run.Formatting.Bold.Should().BeTrue();
        run.FormatRevision.Should().BeNull();
    }

    [Fact]
    public void Reject_FormattingRevision_RestoresPreviousFormatting()
    {
        var doc = new TextDocument();
        var p = new Paragraph();
        p.Runs.Add(new Run("styled", new RunFormatting { Bold = true })
        {
            FormatRevision = new FormatRevision(RunFormatting.Default, "Alice", null)
        });
        doc.Blocks.Add(p);
        var entry = RevisionList.Enumerate(doc)[0];

        RevisionList.Reject(doc, entry).Should().BeTrue();

        var run = doc.Paragraphs.First().Runs.First();
        run.Formatting.Bold.Should().BeFalse();
        run.FormatRevision.Should().BeNull();
        run.Text.Should().Be("styled");
    }

    [Fact]
    public void Enumerate_AndResolve_InsideTableCells()
    {
        var doc = new TextDocument();
        var table = Table.Create(1, 1);
        var cellParagraph = table.Rows[0].Cells[0].Paragraphs[0];
        cellParagraph.Runs.Add(new Run("keep "));
        cellParagraph.Runs.Add(new Run("gone") { Revision = RevisionKind.Deleted, RevisionAuthor = "Eve" });
        doc.Blocks.Add(table);

        var entries = RevisionList.Enumerate(doc);
        entries.Should().ContainSingle();
        entries[0].Kind.Should().Be(RevisionEntryKind.Deletion);
        entries[0].Author.Should().Be("Eve");

        RevisionList.Accept(doc, entries[0]).Should().BeTrue();
        cellParagraph.PlainText.Should().Be("keep ");
        RevisionList.Enumerate(doc).Should().BeEmpty();
    }

    // A shape (text box) carries its own paragraph list (Run.Shape.TextParagraphs); a tracked change
    // anchored there must reach the Reviewing Pane exactly as one anchored in the body/a table cell does
    // (this is the real user path: ReviewingPaneSession.Refresh -> RevisionList.Enumerate -> the pane's
    // list). Mirrors TrackChangesTests.BuildDocumentWithRevisionInsideShape.
    private static TextDocument BuildDocumentWithRevisionInsideShape()
    {
        var shape = new Shape(ShapeKind.TextBox, 100, 50);
        var shapeParagraph = new Paragraph();
        shapeParagraph.Runs.Add(new Run("Box keep "));
        shapeParagraph.Runs.Add(new Run("box added") { Revision = RevisionKind.Inserted, RevisionAuthor = "Ada" });
        shape.TextParagraphs.Add(shapeParagraph);

        var doc = new TextDocument();
        var hostParagraph = new Paragraph();
        hostParagraph.Runs.Add(Run.FromShape(shape));
        doc.Blocks.Add(hostParagraph);
        return doc;
    }

    [Fact]
    public void Enumerate_ListsRevisionInsideShapeTextBox()
    {
        // Before the fix, RevisionList had its own body/table-only paragraph walk, so a text-box-only
        // revision produced an empty Reviewing Pane even though TrackChanges.HasRevisions saw it.
        var doc = BuildDocumentWithRevisionInsideShape();

        var entries = RevisionList.Enumerate(doc);

        entries.Should().ContainSingle();
        entries[0].Kind.Should().Be(RevisionEntryKind.Insertion);
        entries[0].Author.Should().Be("Ada");
        entries[0].Text.Should().Be("box added");
    }

    [Fact]
    public void Accept_RevisionInsideShapeTextBox_ResolvesItAndAgreesWithTrackChanges()
    {
        var doc = BuildDocumentWithRevisionInsideShape();
        var entry = RevisionList.Enumerate(doc)[0];

        RevisionList.Accept(doc, entry).Should().BeTrue();

        var shape = doc.Paragraphs.First().Runs[0].Shape!;
        var shapeParagraph = shape.TextParagraphs.Single();
        shapeParagraph.PlainText.Should().Be("Box keep box added");
        shapeParagraph.Runs.Should().OnlyContain(r => r.Revision == RevisionKind.None);
        RevisionList.Enumerate(doc).Should().BeEmpty();
        // Independent oracle: TrackChanges walks shapes too (r147), so the two must agree rather than
        // RevisionList's own (now-empty) list being taken on faith.
        TrackChanges.HasRevisions(doc).Should().BeFalse();
    }

    [Fact]
    public void Accept_StaleEntry_IsNoOp()
    {
        var doc = BuildDocument();
        var deletion = RevisionList.Enumerate(doc)[1];
        // Resolve it once (removes the run), then a second resolve of the same stale entry must do nothing.
        RevisionList.Accept(doc, deletion).Should().BeTrue();
        RevisionList.Accept(doc, deletion).Should().BeFalse();
    }

    [Fact]
    public void Enumerate_AndResolve_InsideNestedTableCells()
    {
        // A table nested inside a table cell: the Reviewing Pane must still surface (and let the user
        // accept/reject) a tracked change anchored there, not just in top-level table cells.
        var doc = new TextDocument();
        var outerTable = Table.Create(1, 1);
        var nestedTable = Table.Create(1, 1);
        var nestedParagraph = nestedTable.Rows[0].Cells[0].Paragraphs[0];
        nestedParagraph.Runs.Add(new Run("keep "));
        nestedParagraph.Runs.Add(new Run("gone") { Revision = RevisionKind.Deleted, RevisionAuthor = "Eve" });
        outerTable.Rows[0].Cells[0].NestedTables.Add(nestedTable);
        doc.Blocks.Add(outerTable);

        var entries = RevisionList.Enumerate(doc);
        entries.Should().ContainSingle();
        entries[0].Kind.Should().Be(RevisionEntryKind.Deletion);
        entries[0].Author.Should().Be("Eve");

        RevisionList.Accept(doc, entries[0]).Should().BeTrue();
        nestedParagraph.PlainText.Should().Be("keep ");
        RevisionList.Enumerate(doc).Should().BeEmpty();
    }

    // A Word move (w:moveFrom/w:moveTo) is two runs sharing MoveRevisionId: the source run is Deleted,
    // the destination run is Inserted (TextDocument.cs's MoveRevisionId doc comment). Resolving the two
    // halves independently and inconsistently must NOT be possible through the single-entry Accept/Reject
    // path -- doing so used to duplicate or delete the moved text (freew-track-changes F2).
    private static TextDocument BuildMoveDocument()
    {
        // Paragraph 0: "Before " + [deleted "old ", MoveRevisionId=7] (the move's source)
        // Paragraph 1: "After " + [inserted "new", MoveRevisionId=7] (the move's destination)
        var doc = new TextDocument();
        var p0 = new Paragraph();
        p0.Runs.Add(new Run("Before "));
        p0.Runs.Add(new Run("old ") { Revision = RevisionKind.Deleted, RevisionAuthor = "Alice", MoveRevisionId = 7 });
        doc.Blocks.Add(p0);

        var p1 = new Paragraph();
        p1.Runs.Add(new Run("After "));
        p1.Runs.Add(new Run("new") { Revision = RevisionKind.Inserted, RevisionAuthor = "Alice", MoveRevisionId = 7 });
        doc.Blocks.Add(p1);
        return doc;
    }

    [Fact]
    public void Reject_OneHalfOfAMove_RejectsBothHalves_TextStaysAtOriginalLocationOnly()
    {
        var doc = BuildMoveDocument();
        var entries = RevisionList.Enumerate(doc);
        var deletion = entries.Single(e => e.Kind == RevisionEntryKind.Deletion);

        // Rejecting the source (moveFrom) half must also reject the destination (moveTo) half -- a
        // rejected move restores the text at its original location and does NOT also leave it at the
        // new location.
        RevisionList.Reject(doc, deletion).Should().BeTrue();

        var p0 = (Paragraph)doc.Blocks[0];
        var p1 = (Paragraph)doc.Blocks[1];
        p0.PlainText.Should().Be("Before old ");
        p1.PlainText.Should().Be("After ");
        RevisionList.Enumerate(doc).Should().BeEmpty();
    }

    [Fact]
    public void Accept_OneHalfOfAMove_AcceptsBothHalves_TextEndsUpAtNewLocationOnly()
    {
        var doc = BuildMoveDocument();
        var entries = RevisionList.Enumerate(doc);
        var deletion = entries.Single(e => e.Kind == RevisionEntryKind.Deletion);

        // Accepting the source (moveFrom) half must also accept the destination (moveTo) half -- an
        // accepted move removes the text from its original location and keeps it only at the new one.
        RevisionList.Accept(doc, deletion).Should().BeTrue();

        var p0 = (Paragraph)doc.Blocks[0];
        var p1 = (Paragraph)doc.Blocks[1];
        p0.PlainText.Should().Be("Before ");
        p1.PlainText.Should().Be("After new");
        RevisionList.Enumerate(doc).Should().BeEmpty();
    }

    [Fact]
    public void Accept_TheInsertedHalfOfAMove_AlsoResolvesTheDeletedHalfTheSameWay()
    {
        // Symmetric to the two tests above: whichever half the Reviewing Pane's Accept/Reject button was
        // clicked on, the pair resolves together.
        var doc = BuildMoveDocument();
        var insertion = RevisionList.Enumerate(doc).Single(e => e.Kind == RevisionEntryKind.Insertion);

        RevisionList.Accept(doc, insertion).Should().BeTrue();

        var p0 = (Paragraph)doc.Blocks[0];
        var p1 = (Paragraph)doc.Blocks[1];
        p0.PlainText.Should().Be("Before ");
        p1.PlainText.Should().Be("After new");
        RevisionList.Enumerate(doc).Should().BeEmpty();
    }

    [Fact]
    public void Resolve_UnrelatedOrdinaryDeletion_DoesNotDisturbAnUnrelatedMovePair()
    {
        // Sibling/no-regression case: a document with both an ordinary (unlinked) deletion and an
        // independent move pair. Resolving the ordinary deletion must touch only itself -- the move pair,
        // which does not share its MoveRevisionId, stays fully pending.
        var doc = BuildMoveDocument();
        var p0 = (Paragraph)doc.Blocks[0];
        p0.Runs.Add(new Run("unrelated") { Revision = RevisionKind.Deleted, RevisionAuthor = "Bob" });

        var ordinary = RevisionList.Enumerate(doc).Single(e => e.Text == "unrelated");
        RevisionList.Accept(doc, ordinary).Should().BeTrue();

        p0.PlainText.Should().Be("Before old ");
        var remaining = RevisionList.Enumerate(doc);
        remaining.Should().HaveCount(2);
        remaining.Should().Contain(e => e.Kind == RevisionEntryKind.Deletion && e.Text == "old ");
        remaining.Should().Contain(e => e.Kind == RevisionEntryKind.Insertion && e.Text == "new");
    }
}
