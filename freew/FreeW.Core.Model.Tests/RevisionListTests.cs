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

    // --- Paragraph-mark revisions (freew-track-changes-accept F2) ---
    //
    // A tracked Backspace/Delete at a paragraph boundary (DocumentEditingSession
    // .TryDeleteBodyParagraphBoundaryAsRevision) sets ONLY Paragraph.MarkRevision -- no run is touched.
    // Before the fix, RevisionList.Enumerate never read MarkRevision at all, so this change was invisible
    // to the Reviewing Pane, Previous/Next, and single Accept/Reject (only Accept All/Reject All, which
    // walk TrackChanges.ResolveBlockList, could ever resolve it).

    [Fact]
    public void Enumerate_ListsParagraphMarkDeletionRevision()
    {
        var doc = new TextDocument();
        var p0 = new Paragraph();
        p0.Runs.Add(new Run("Hello "));
        p0.MarkRevision = RevisionKind.Deleted;
        p0.MarkRevisionAuthor = "Alice";
        p0.MarkRevisionDateXml = "2026-08-20T09:00:00Z";
        doc.Blocks.Add(p0);

        var p1 = new Paragraph();
        p1.Runs.Add(new Run("World"));
        doc.Blocks.Add(p1);

        var entries = RevisionList.Enumerate(doc);

        entries.Should().ContainSingle();
        entries[0].Kind.Should().Be(RevisionEntryKind.Deletion);
        entries[0].Author.Should().Be("Alice");
        entries[0].DateXml.Should().Be("2026-08-20T09:00:00Z");
        entries[0].Text.Should().Be(FormattingMarks.Pilcrow.ToString());
        entries[0].Paragraph.Should().BeSameAs(p0);
        entries[0].Run.Should().BeNull();
        entries[0].BlockIndex.Should().Be(0);
    }

    [Fact]
    public void Accept_ParagraphMarkDeletion_MergesTheTwoParagraphs()
    {
        // Accepting a deleted paragraph mark applies the tracked merge: this paragraph's runs move onto
        // the following paragraph and the boundary paragraph itself disappears -- exactly what Accept All
        // does for the same mark (TrackChanges.ResolveBlockList), just for this one entry only.
        var doc = new TextDocument();
        var p0 = new Paragraph();
        p0.Runs.Add(new Run("Hello "));
        p0.MarkRevision = RevisionKind.Deleted;
        p0.MarkRevisionAuthor = "Alice";
        doc.Blocks.Add(p0);

        var p1 = new Paragraph();
        p1.Runs.Add(new Run("World"));
        doc.Blocks.Add(p1);

        var entry = RevisionList.Enumerate(doc)[0];
        RevisionList.Accept(doc, entry).Should().BeTrue();

        doc.Blocks.Should().ContainSingle();
        var merged = (Paragraph)doc.Blocks[0];
        merged.PlainText.Should().Be("Hello World");
        merged.MarkRevision.Should().Be(RevisionKind.None);
        RevisionList.Enumerate(doc).Should().BeEmpty();
        // Independent oracle: TrackChanges.AcceptAll on an equivalent document must agree.
        TrackChanges.HasRevisions(doc).Should().BeFalse();
    }

    [Fact]
    public void Reject_ParagraphMarkDeletion_ClearsMarkAndKeepsBothParagraphs()
    {
        // Rejecting a deleted paragraph mark restores the boundary: the mark is cleared but the two
        // paragraphs stay separate (the opposite of Accept).
        var doc = new TextDocument();
        var p0 = new Paragraph();
        p0.Runs.Add(new Run("Hello "));
        p0.MarkRevision = RevisionKind.Deleted;
        doc.Blocks.Add(p0);

        var p1 = new Paragraph();
        p1.Runs.Add(new Run("World"));
        doc.Blocks.Add(p1);

        var entry = RevisionList.Enumerate(doc)[0];
        RevisionList.Reject(doc, entry).Should().BeTrue();

        doc.Blocks.Should().HaveCount(2);
        p0.MarkRevision.Should().Be(RevisionKind.None);
        p0.PlainText.Should().Be("Hello ");
        p1.PlainText.Should().Be("World");
        RevisionList.Enumerate(doc).Should().BeEmpty();
    }

    [Fact]
    public void Reject_ParagraphMarkInsertion_UndoesTheSplitByMergingParagraphs()
    {
        // The inverse gesture: a tracked Enter split one paragraph in two, marking the earlier paragraph's
        // OWN (new) end mark as Inserted. Rejecting the insertion undoes the split (merge); accepting it
        // keeps the split (mark just cleared) -- the mirror image of the Deletion case above.
        var doc = new TextDocument();
        var p0 = new Paragraph();
        p0.Runs.Add(new Run("Hello "));
        p0.MarkRevision = RevisionKind.Inserted;
        p0.MarkRevisionAuthor = "Bob";
        doc.Blocks.Add(p0);

        var p1 = new Paragraph();
        p1.Runs.Add(new Run("World"));
        doc.Blocks.Add(p1);

        var entry = RevisionList.Enumerate(doc).Single();
        entry.Kind.Should().Be(RevisionEntryKind.Insertion);

        RevisionList.Reject(doc, entry).Should().BeTrue();

        doc.Blocks.Should().ContainSingle();
        var merged = (Paragraph)doc.Blocks[0];
        merged.PlainText.Should().Be("Hello World");
        RevisionList.Enumerate(doc).Should().BeEmpty();
    }

    [Fact]
    public void Accept_ParagraphMarkInsertion_KeepsTheSplitAndClearsTheMarkOnly()
    {
        var doc = new TextDocument();
        var p0 = new Paragraph();
        p0.Runs.Add(new Run("Hello "));
        p0.MarkRevision = RevisionKind.Inserted;
        doc.Blocks.Add(p0);

        var p1 = new Paragraph();
        p1.Runs.Add(new Run("World"));
        doc.Blocks.Add(p1);

        var entry = RevisionList.Enumerate(doc).Single();
        RevisionList.Accept(doc, entry).Should().BeTrue();

        doc.Blocks.Should().HaveCount(2);
        p0.MarkRevision.Should().Be(RevisionKind.None);
        p0.PlainText.Should().Be("Hello ");
        p1.PlainText.Should().Be("World");
    }

    [Fact]
    public void Accept_ParagraphMarkDeletion_OnLastEmptyUnanchoredParagraph_DropsItOutright()
    {
        // No following paragraph to merge into: an empty, unanchored trailing paragraph whose mark
        // resolves to "removed" is dropped entirely -- mirrors TrackChanges' own fallback for this case.
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body text"));
        var trailing = new Paragraph();
        trailing.MarkRevision = RevisionKind.Deleted;
        doc.Blocks.Add(trailing);

        var entry = RevisionList.Enumerate(doc).Single();
        RevisionList.Accept(doc, entry).Should().BeTrue();

        doc.Blocks.Should().ContainSingle();
        RevisionList.Enumerate(doc).Should().BeEmpty();
    }

    [Fact]
    public void Accept_ParagraphMarkDeletion_InsideTableCell_MergesWithinTheCellOnly()
    {
        // Sibling/no-regression case: the same merge must work for a paragraph nested in a table cell,
        // touching only that cell's own paragraph list (not the top-level body block list).
        var doc = new TextDocument();
        var table = Table.Create(1, 1);
        var cell = table.Rows[0].Cells[0];
        cell.Paragraphs.Clear();
        var p0 = new Paragraph();
        p0.Runs.Add(new Run("cell one "));
        p0.MarkRevision = RevisionKind.Deleted;
        cell.Paragraphs.Add(p0);
        var p1 = new Paragraph();
        p1.Runs.Add(new Run("cell two"));
        cell.Paragraphs.Add(p1);
        doc.Blocks.Add(table);

        var entry = RevisionList.Enumerate(doc).Single();
        RevisionList.Accept(doc, entry).Should().BeTrue();

        cell.Paragraphs.Should().ContainSingle();
        cell.Paragraphs[0].PlainText.Should().Be("cell one cell two");
        RevisionList.Enumerate(doc).Should().BeEmpty();
    }

    [Fact]
    public void Accept_StaleMarkRevisionEntry_IsNoOp()
    {
        var doc = new TextDocument();
        var p0 = new Paragraph();
        p0.MarkRevision = RevisionKind.Deleted;
        doc.Blocks.Add(p0);
        doc.Blocks.Add(new Paragraph("next"));

        var entry = RevisionList.Enumerate(doc).Single();
        RevisionList.Accept(doc, entry).Should().BeTrue();
        // p0 was merged away; resolving the same (now stale) entry again must do nothing.
        RevisionList.Accept(doc, entry).Should().BeFalse();
    }

    // --- Headers/footers/footnotes/endnotes (freew-track-changes-accept F3) ---
    //
    // TrackChanges.HasRevisions/AcceptAll/RejectAll already reach every header/footer slot of every
    // section plus every footnote/endnote (TrackChanges.cs). Before the fix, RevisionList only walked the
    // document body, so a tracked change living in one of those places produced an empty Reviewing Pane
    // even though TrackChanges agreed a revision was pending.

    [Fact]
    public void Enumerate_ListsRevisionInsideHeader()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body text"));
        var headerParagraph = new Paragraph();
        headerParagraph.Runs.Add(new Run("Header "));
        headerParagraph.Runs.Add(new Run("added") { Revision = RevisionKind.Inserted, RevisionAuthor = "Ada" });
        doc.Header = new HeaderFooter();
        doc.Header.Paragraphs.Add(headerParagraph);

        var entries = RevisionList.Enumerate(doc);

        entries.Should().ContainSingle();
        entries[0].Kind.Should().Be(RevisionEntryKind.Insertion);
        entries[0].Author.Should().Be("Ada");
        entries[0].Text.Should().Be("added");
        // Independent oracle: TrackChanges must agree a revision is pending.
        TrackChanges.HasRevisions(doc).Should().BeTrue();
    }

    [Fact]
    public void Accept_RevisionInsideHeader_ResolvesItAndAgreesWithTrackChanges()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body text"));
        var headerParagraph = new Paragraph();
        headerParagraph.Runs.Add(new Run("Header "));
        headerParagraph.Runs.Add(new Run("added") { Revision = RevisionKind.Inserted, RevisionAuthor = "Ada" });
        doc.Header = new HeaderFooter();
        doc.Header.Paragraphs.Add(headerParagraph);

        var entry = RevisionList.Enumerate(doc).Single();
        RevisionList.Accept(doc, entry).Should().BeTrue();

        headerParagraph.PlainText.Should().Be("Header added");
        headerParagraph.Runs.Should().OnlyContain(r => r.Revision == RevisionKind.None);
        RevisionList.Enumerate(doc).Should().BeEmpty();
        TrackChanges.HasRevisions(doc).Should().BeFalse();
    }

    [Fact]
    public void Enumerate_AndResolve_RevisionInsideFootnote()
    {
        var doc = new TextDocument();
        var footnote = new Footnote(1);
        var footnoteParagraph = new Paragraph();
        footnoteParagraph.Runs.Add(new Run("note "));
        footnoteParagraph.Runs.Add(new Run("gone") { Revision = RevisionKind.Deleted, RevisionAuthor = "Eve" });
        footnote.Content.Add(footnoteParagraph);
        doc.Footnotes[1] = footnote;

        var entries = RevisionList.Enumerate(doc);
        entries.Should().ContainSingle();
        entries[0].Kind.Should().Be(RevisionEntryKind.Deletion);
        entries[0].Author.Should().Be("Eve");

        RevisionList.Accept(doc, entries[0]).Should().BeTrue();
        footnoteParagraph.PlainText.Should().Be("note ");
        RevisionList.Enumerate(doc).Should().BeEmpty();
        TrackChanges.HasRevisions(doc).Should().BeFalse();
    }

    [Fact]
    public void Enumerate_AndResolve_RevisionInsideEndnote()
    {
        var doc = new TextDocument();
        var endnote = new Endnote(1);
        var endnoteParagraph = new Paragraph();
        endnoteParagraph.Runs.Add(new Run("added") { Revision = RevisionKind.Inserted, RevisionAuthor = "Carol" });
        endnote.Content.Add(endnoteParagraph);
        doc.Endnotes[1] = endnote;

        var entries = RevisionList.Enumerate(doc);
        entries.Should().ContainSingle();
        entries[0].Kind.Should().Be(RevisionEntryKind.Insertion);
        entries[0].Author.Should().Be("Carol");

        RevisionList.Accept(doc, entries[0]).Should().BeTrue();
        RevisionList.Enumerate(doc).Should().BeEmpty();
        TrackChanges.HasRevisions(doc).Should().BeFalse();
    }

    [Fact]
    public void Accept_ParagraphMarkDeletion_InsideFootnote_MergesWithinTheFootnoteOnly()
    {
        // Sibling case for the F2 merge logic: the same paragraph-mark merge must reach a footnote's own
        // paragraph list, not just the document body.
        var doc = new TextDocument();
        var footnote = new Footnote(1);
        var p0 = new Paragraph();
        p0.Runs.Add(new Run("first "));
        p0.MarkRevision = RevisionKind.Deleted;
        footnote.Content.Add(p0);
        var p1 = new Paragraph();
        p1.Runs.Add(new Run("second"));
        footnote.Content.Add(p1);
        doc.Footnotes[1] = footnote;

        var entry = RevisionList.Enumerate(doc).Single();
        RevisionList.Accept(doc, entry).Should().BeTrue();

        footnote.Content.Should().ContainSingle();
        footnote.Content[0].PlainText.Should().Be("first second");
        RevisionList.Enumerate(doc).Should().BeEmpty();
    }

    [Fact]
    public void Enumerate_OnDocumentWithHeaderButNoRevisions_IsEmpty()
    {
        // No-regression check: an ordinary header (no tracked changes) must not spuriously appear.
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body text"));
        doc.Header = new HeaderFooter("Plain header, nothing tracked");

        RevisionList.Enumerate(doc).Should().BeEmpty();
        TrackChanges.HasRevisions(doc).Should().BeFalse();
    }
}
