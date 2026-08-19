namespace FreeW.Core.Model.Tests;

public class DocumentInspectorTests
{
    [Fact]
    public void TextDocument_UsesSharedDocumentPropertiesModel()
    {
        typeof(TextDocument)
            .GetProperty(nameof(TextDocument.Properties))!
            .PropertyType
            .Should()
            .Be(typeof(Free.Shared.Opc.DocumentProperties));
    }

    // A document carrying one of every inspected category: two comments, two tracked revisions
    // (one insertion + one deletion), several populated properties, and two bookmarks (one of them
    // in a table cell, with an internal-link anchor pointing at it).
    private static TextDocument BuildDocument()
    {
        var doc = new TextDocument();

        // Comments: two distinct comment ids in the side store, with a covered run + anchor each.
        doc.Comments[0] = new Comment(0, "First note", "Alice", "A");
        doc.Comments[1] = new Comment(1, "Second note", "Bob", "B");

        var bookmarked = new Paragraph { BookmarkName = "intro" };
        bookmarked.Runs.Add(new Run("Hello ") { CommentId = 0 });
        bookmarked.Runs.Add(Run.CommentReference(0));
        bookmarked.Runs.Add(new Run("added ") { Revision = RevisionKind.Inserted, RevisionAuthor = "Alice", RevisionDateXml = "2026-06-17T10:00:00Z" });
        bookmarked.Runs.Add(new Run("removed ") { Revision = RevisionKind.Deleted, RevisionAuthor = "Bob" });
        bookmarked.Runs.Add(new Run("jump") { HyperlinkAnchor = "target" });
        doc.Blocks.Add(bookmarked);

        var commented = new Paragraph();
        commented.Runs.Add(new Run("World") { CommentId = 1 });
        commented.Runs.Add(Run.CommentReference(1));
        doc.Blocks.Add(commented);

        // A second bookmark living in a table cell, to exercise the table-cell walk.
        var table = Table.Create(1, 1);
        table.Rows[0].Cells[0].Paragraphs[0].BookmarkName = "target";
        doc.Blocks.Add(table);

        // Populated document properties (4 of them).
        doc.Properties.Title = "My Title";
        doc.Properties.Author = "Alice";
        doc.Properties.Subject = "Testing";
        doc.Properties.Created = new DateTimeOffset(2026, 6, 17, 0, 0, 0, TimeSpan.Zero);

        return doc;
    }

    [Fact]
    public void Inspect_ReportsCorrectCounts()
    {
        var result = DocumentInspector.Inspect(BuildDocument());

        result.Comments.Should().Be(2);
        result.Revisions.Should().Be(2); // one insertion + one deletion
        result.NonEmptyProperties.Should().Be(4); // title, author, subject, created
        result.Bookmarks.Should().Be(2); // "intro" + the table-cell "target"

        result.HasComments.Should().BeTrue();
        result.HasRevisions.Should().BeTrue();
        result.HasProperties.Should().BeTrue();
        result.HasBookmarks.Should().BeTrue();
        result.IsClean.Should().BeFalse();
    }

    [Fact]
    public void Inspect_OnCleanDocument_ReportsZeroAndIsClean()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Just text"));

        var result = DocumentInspector.Inspect(doc);

        result.Should().Be(new InspectionResult(0, 0, 0, 0));
        result.IsClean.Should().BeTrue();
    }

    [Fact]
    public void Inspect_DoesNotMutateDocument()
    {
        var doc = BuildDocument();

        DocumentInspector.Inspect(doc);

        // Re-inspecting yields the same counts — nothing was removed.
        DocumentInspector.Inspect(doc).Should().Be(new InspectionResult(2, 2, 4, 2));
    }

    [Fact]
    public void RemoveComments_ClearsCommentsOnly()
    {
        var doc = BuildDocument();

        DocumentInspector.RemoveComments(doc);

        doc.Comments.Should().BeEmpty();
        // No run anywhere still carries a comment id or is a comment-reference anchor.
        var allRuns = doc.Paragraphs.SelectMany(p => p.Runs).ToList();
        allRuns.Should().OnlyContain(r => r.CommentId == null && !r.IsCommentReference);

        var after = DocumentInspector.Inspect(doc);
        after.Comments.Should().Be(0);
        // Other categories untouched.
        after.Revisions.Should().Be(2);
        after.NonEmptyProperties.Should().Be(4);
        after.Bookmarks.Should().Be(2);
    }

    // A comment anchored in the default header, the default footer, a footnote, and an endnote — every
    // paragraph store OUTSIDE the body that Word allows a review comment to anchor in.
    private static TextDocument BuildDocumentWithCommentsOutsideBody()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body text"));

        doc.Comments[10] = new Comment(10, "Header note", "Ann", "A");
        doc.Header = new HeaderFooter();
        var headerParagraph = new Paragraph();
        headerParagraph.Runs.Add(new Run("Header text") { CommentId = 10 });
        headerParagraph.Runs.Add(Run.CommentReference(10));
        doc.Header.Paragraphs.Add(headerParagraph);

        doc.Comments[11] = new Comment(11, "Footer note", "Bob", "B");
        doc.Footer = new HeaderFooter();
        var footerParagraph = new Paragraph();
        footerParagraph.Runs.Add(new Run("Footer text") { CommentId = 11 });
        footerParagraph.Runs.Add(Run.CommentReference(11));
        doc.Footer.Paragraphs.Add(footerParagraph);

        doc.Comments[12] = new Comment(12, "Footnote note", "Cid", "C");
        var footnote = new Footnote(1);
        var footnoteParagraph = new Paragraph();
        footnoteParagraph.Runs.Add(new Run("Footnote text") { CommentId = 12 });
        footnoteParagraph.Runs.Add(Run.CommentReference(12));
        footnote.Content.Add(footnoteParagraph);
        doc.Footnotes[1] = footnote;

        doc.Comments[13] = new Comment(13, "Endnote note", "Dan", "D");
        var endnote = new Endnote(1);
        var endnoteParagraph = new Paragraph();
        endnoteParagraph.Runs.Add(new Run("Endnote text") { CommentId = 13 });
        endnoteParagraph.Runs.Add(Run.CommentReference(13));
        endnote.Content.Add(endnoteParagraph);
        doc.Endnotes[1] = endnote;

        return doc;
    }

    [Fact]
    public void RemoveComments_StripsAnchorsInHeaderFooterFootnoteAndEndnote()
    {
        var doc = BuildDocumentWithCommentsOutsideBody();

        DocumentInspector.RemoveComments(doc);

        doc.Comments.Should().BeEmpty();

        // No run in the header, footer, footnote, or endnote still carries a comment id or an
        // unresolved comment-reference anchor. A stale mark here is exactly what the docx writer would
        // otherwise still serialise as a dangling w:commentRangeStart/End/w:commentReference — a package
        // Word would refuse to open cleanly and flag for repair.
        var headerRuns = doc.Header!.Paragraphs.SelectMany(p => p.Runs).ToList();
        headerRuns.Should().OnlyContain(r => r.CommentId == null && !r.IsCommentReference);
        headerRuns.Should().Contain(r => r.Text == "Header text");

        var footerRuns = doc.Footer!.Paragraphs.SelectMany(p => p.Runs).ToList();
        footerRuns.Should().OnlyContain(r => r.CommentId == null && !r.IsCommentReference);
        footerRuns.Should().Contain(r => r.Text == "Footer text");

        var footnoteRuns = doc.Footnotes[1].Content.SelectMany(p => p.Runs).ToList();
        footnoteRuns.Should().OnlyContain(r => r.CommentId == null && !r.IsCommentReference);
        footnoteRuns.Should().Contain(r => r.Text == "Footnote text");

        var endnoteRuns = doc.Endnotes[1].Content.SelectMany(p => p.Runs).ToList();
        endnoteRuns.Should().OnlyContain(r => r.CommentId == null && !r.IsCommentReference);
        endnoteRuns.Should().Contain(r => r.Text == "Endnote text");
    }

    [Fact]
    public void RemoveComments_OnBodyOnlyDocument_StillWorks()
    {
        // Sibling no-regression check: a document with only a body comment (no header/footer/footnote/
        // endnote at all — the common case) still has its comment fully removed by the widened walk.
        var doc = BuildDocument();

        DocumentInspector.RemoveComments(doc);

        doc.Comments.Should().BeEmpty();
        doc.Paragraphs.SelectMany(p => p.Runs)
            .Should().OnlyContain(r => r.CommentId == null && !r.IsCommentReference);
    }

    [Fact]
    public void RemoveComments_DropsAnchorRuns_ButKeepsText()
    {
        var doc = BuildDocument();

        DocumentInspector.RemoveComments(doc);

        // "Hello " survives (its CommentId cleared); the anchor run after it is gone.
        var first = (Paragraph)doc.Blocks[0];
        first.PlainText.Should().StartWith("Hello ");
        first.Runs.Should().NotContain(r => r.IsCommentReference);
    }

    [Fact]
    public void RemoveRevisions_AcceptsAllChanges_ClearsRevisionsOnly()
    {
        var doc = BuildDocument();

        DocumentInspector.RemoveRevisions(doc);

        TrackChanges.HasRevisions(doc).Should().BeFalse();
        // "added " was an insertion (kept); "removed " was a deletion (dropped).
        var first = (Paragraph)doc.Blocks[0];
        first.PlainText.Should().Contain("added ");
        first.PlainText.Should().NotContain("removed ");

        var after = DocumentInspector.Inspect(doc);
        after.Revisions.Should().Be(0);
        after.Comments.Should().Be(2);
        after.NonEmptyProperties.Should().Be(4);
        after.Bookmarks.Should().Be(2);
    }

    // A shape (text box) carries its own paragraph list (Run.Shape.TextParagraphs); a tracked change
    // anchored ONLY there is the real bug from the audit: File > Info > Check for Issues > Inspect
    // Document reported Revisions=0 and left the checkbox disabled/unchecked (both shells derive
    // IsEnabled from this same count > 0 — see DocumentInspectorDialog.AddRow / SafetyDialogs.AddCheck),
    // so RemoveRevisions -> TrackChanges.AcceptAll was dead code for this document.
    private static TextDocument BuildDocumentWithRevisionOnlyInsideShape()
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
    public void Inspect_CountsRevisionInsideShapeTextBox_SoTheRemoveCheckboxCanBeEnabled()
    {
        var result = DocumentInspector.Inspect(BuildDocumentWithRevisionOnlyInsideShape());

        // This is exactly the count both DocumentInspectorDialog (WPF) and SafetyDialogs (Avalonia) use
        // to compute the checkbox's IsEnabled (count > 0) — asserting the count IS asserting enablement.
        result.Revisions.Should().Be(1);
        result.HasRevisions.Should().BeTrue();
        result.IsClean.Should().BeFalse();
    }

    [Fact]
    public void RemoveRevisions_ClearsRevisionInsideShapeTextBoxAndAgreesWithTrackChanges()
    {
        var doc = BuildDocumentWithRevisionOnlyInsideShape();

        DocumentInspector.RemoveRevisions(doc);

        var shape = doc.Paragraphs.First().Runs[0].Shape!;
        shape.TextParagraphs.Single().PlainText.Should().Be("Box keep box added");

        var after = DocumentInspector.Inspect(doc);
        after.Revisions.Should().Be(0);
        after.HasRevisions.Should().BeFalse();
        // Independent oracle: TrackChanges.AcceptAll already reaches shapes (r147); DocumentInspector's
        // own before/after count must agree with it rather than being taken on faith.
        TrackChanges.HasRevisions(doc).Should().BeFalse();
    }

    [Fact]
    public void RemoveSelected_WithRevisionsChosen_RemovesShapeRevisionViaTheRealDispatchPath()
    {
        // Enter at RemoveSelected — the same method BackstageInfoSafetyPanePlanner/DocumentInspectorDialog
        // call after the user checks "Revisions" and clicks Remove — not RemoveRevisions directly.
        var doc = BuildDocumentWithRevisionOnlyInsideShape();

        var result = DocumentInspector.RemoveSelected(
            doc,
            new InspectionRemovalSelection(Comments: false, Revisions: true, Properties: false, Bookmarks: false));

        result.Before.Revisions.Should().Be(1);
        result.After.Revisions.Should().Be(0);
        result.Removed.Revisions.Should().Be(1);
    }

    [Fact]
    public void RemoveProperties_ClearsPropertiesOnly()
    {
        var doc = BuildDocument();

        DocumentInspector.RemoveProperties(doc);

        doc.Properties.Title.Should().BeNull();
        doc.Properties.Author.Should().BeNull();
        doc.Properties.Subject.Should().BeNull();
        doc.Properties.Created.Should().BeNull();

        var after = DocumentInspector.Inspect(doc);
        after.NonEmptyProperties.Should().Be(0);
        after.Comments.Should().Be(2);
        after.Revisions.Should().Be(2);
        after.Bookmarks.Should().Be(2);
    }

    [Fact]
    public void RemoveBookmarks_ClearsBookmarksAndAnchorsOnly()
    {
        var doc = BuildDocument();

        DocumentInspector.RemoveBookmarks(doc);

        doc.Paragraphs.Should().OnlyContain(p => p.BookmarkName == null);
        // The internal-link anchor that pointed at "target" is also cleared.
        var allRuns = doc.Blocks.OfType<Paragraph>().SelectMany(p => p.Runs);
        allRuns.Should().OnlyContain(r => r.HyperlinkAnchor == null);

        var after = DocumentInspector.Inspect(doc);
        after.Bookmarks.Should().Be(0);
        after.Comments.Should().Be(2);
        after.Revisions.Should().Be(2);
        after.NonEmptyProperties.Should().Be(4);
    }

    [Fact]
    public void RemoveAll_ProducesCleanDocument()
    {
        var doc = BuildDocument();

        DocumentInspector.RemoveComments(doc);
        DocumentInspector.RemoveRevisions(doc);
        DocumentInspector.RemoveProperties(doc);
        DocumentInspector.RemoveBookmarks(doc);

        DocumentInspector.Inspect(doc).IsClean.Should().BeTrue();
    }

    [Fact]
    public void RemoveSelected_RemovesOnlyChosenCategoriesAndReportsDifference()
    {
        var doc = BuildDocument();

        var result = DocumentInspector.RemoveSelected(
            doc,
            new InspectionRemovalSelection(
                Comments: true,
                Revisions: false,
                Properties: false,
                Bookmarks: true));

        result.Before.Should().Be(new InspectionResult(2, 2, 4, 2));
        result.After.Should().Be(new InspectionResult(0, 2, 4, 0));
        result.Removed.Should().Be(new InspectionResult(2, 0, 0, 2));
        result.After.HasRevisions.Should().BeTrue();
        result.After.HasProperties.Should().BeTrue();
    }

    [Fact]
    public void RemoveSelected_WithNoCategories_IsNonMutatingAndReportsNoDifference()
    {
        var doc = BuildDocument();
        var selection = new InspectionRemovalSelection(false, false, false, false);

        var result = DocumentInspector.RemoveSelected(doc, selection);

        selection.Any.Should().BeFalse();
        result.Before.Should().Be(new InspectionResult(2, 2, 4, 2));
        result.After.Should().Be(result.Before);
        result.Removed.Should().Be(new InspectionResult(0, 0, 0, 0));
    }

    // --- Metadata living inside a table nested in a table cell (tc/w:tbl) ---

    private static TextDocument BuildDocumentWithNestedTableMetadata()
    {
        var doc = new TextDocument();
        var outerTable = Table.Create(1, 1);
        var nestedTable = Table.Create(1, 1);
        var nestedParagraph = nestedTable.Rows[0].Cells[0].Paragraphs[0];
        nestedParagraph.BookmarkName = "deepAnchor";
        nestedParagraph.Runs.Add(new Run("deep ") { CommentId = 5 });
        nestedParagraph.Runs.Add(Run.CommentReference(5));
        nestedParagraph.Runs.Add(new Run("deep-added") { Revision = RevisionKind.Inserted, RevisionAuthor = "Eve" });
        doc.Comments[5] = new Comment(5, "Nested note", "Eve", "E");
        outerTable.Rows[0].Cells[0].NestedTables.Add(nestedTable);
        doc.Blocks.Add(outerTable);
        return doc;
    }

    [Fact]
    public void Inspect_CountsMetadataInsideNestedTable()
    {
        var result = DocumentInspector.Inspect(BuildDocumentWithNestedTableMetadata());

        result.Comments.Should().Be(1);
        result.Revisions.Should().Be(1);
        result.Bookmarks.Should().Be(1);
    }

    [Fact]
    public void RemoveComments_StripsCommentMarksInsideNestedTable()
    {
        var doc = BuildDocumentWithNestedTableMetadata();

        DocumentInspector.RemoveComments(doc);

        var nestedParagraph = ((Table)doc.Blocks[0]).Rows[0].Cells[0].NestedTables[0].Rows[0].Cells[0].Paragraphs[0];
        nestedParagraph.Runs.Should().OnlyContain(r => r.CommentId == null && !r.IsCommentReference);
        DocumentInspector.Inspect(doc).Comments.Should().Be(0);
    }

    [Fact]
    public void RemoveBookmarks_ClearsBookmarksInsideNestedTable()
    {
        var doc = BuildDocumentWithNestedTableMetadata();

        DocumentInspector.RemoveBookmarks(doc);

        var nestedParagraph = ((Table)doc.Blocks[0]).Rows[0].Cells[0].NestedTables[0].Rows[0].Cells[0].Paragraphs[0];
        nestedParagraph.BookmarkName.Should().BeNull();
        DocumentInspector.Inspect(doc).Bookmarks.Should().Be(0);
    }
}
