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
