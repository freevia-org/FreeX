namespace FreeW.Core.Model.Tests;

public sealed class CommentCommandTests
{
    private sealed class TestContext(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document { get; } = document;
    }

    [Fact]
    public void AddCommentCommand_IsCommentHistory_AndUndoRedoRestoresAnchors()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Hello world"));
        var bus = new DocumentCommandBus(new TestContext(doc));
        var comment = new Comment(3, "note", "Ann", "A");

        bus.Execute(new AddCommentCommand(0, 6, 11, 3, comment));

        bus.NextUndoMutationKind.Should().Be(DocumentCommandMutationKind.Comment);
        doc.Comments.Should().ContainKey(3);
        AnchorText((Paragraph)doc.Blocks[0], 3).Should().Be("world");

        bus.Undo().Should().BeTrue();
        bus.NextRedoMutationKind.Should().Be(DocumentCommandMutationKind.Comment);
        doc.Comments.Should().NotContainKey(3);
        ((Paragraph)doc.Blocks[0]).Runs.Should().OnlyContain(run => run.CommentId == null);

        bus.Redo().Should().BeTrue();
        doc.Comments.Should().ContainKey(3);
        AnchorText((Paragraph)doc.Blocks[0], 3).Should().Be("world");
    }

    [Fact]
    public void AddCommentCommand_RemapsBookmarkAfterSplitRuns_AndUndoRestoresExactBoundary()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("HelloWorld"));
        paragraph.Runs.Add(new Run("Tail"));
        paragraph.BookmarkNames.Add("TailBookmark");
        paragraph.BookmarkBoundaries.Add(new BookmarkBoundary("4", BookmarkBoundaryKind.Start, 1, "TailBookmark"));
        paragraph.BookmarkBoundaries.Add(new BookmarkBoundary("4", BookmarkBoundaryKind.End, 2));
        doc.Blocks.Add(paragraph);
        var bus = new DocumentCommandBus(new TestContext(doc));

        bus.Execute(new AddCommentCommand(0, 0, 5, 3, new Comment(3, "note", "Ann", "A")));

        paragraph.Runs.Select(run => run.Text).Should().Equal("Hello", "", "World", "Tail");
        paragraph.BookmarkBoundaries.Select(boundary => boundary.RunIndex).Should().Equal(3, 4);
        paragraph.Runs[paragraph.BookmarkBoundaries[0].RunIndex].Text.Should().Be("Tail");

        bus.Undo().Should().BeTrue();
        paragraph.Runs.Select(run => run.Text).Should().Equal("HelloWorld", "Tail");
        paragraph.BookmarkBoundaries.Select(boundary => boundary.RunIndex).Should().Equal(1, 2);
    }

    [Fact]
    public void DeleteCommentCommand_IsCommentHistory_AndUndoRedoRestoresTableCellAnchors()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Cell reviewed text") { CommentId = 7 });
        paragraph.Runs.Add(Run.CommentReference(7));
        var cell = new TableCell();
        cell.Paragraphs.Add(paragraph);
        var row = new TableRow();
        row.Cells.Add(cell);
        var table = new Table();
        table.Rows.Add(row);
        doc.Blocks.Add(table);
        doc.Comments[7] = new Comment(7, "table note", "T", "T");
        var bus = new DocumentCommandBus(new TestContext(doc));

        bus.Execute(new DeleteCommentCommand(7));

        bus.NextUndoMutationKind.Should().Be(DocumentCommandMutationKind.Comment);
        doc.Comments.Should().NotContainKey(7);
        paragraph.Runs.Should().ContainSingle();
        paragraph.Runs[0].CommentId.Should().BeNull();
        paragraph.Runs[0].IsCommentReference.Should().BeFalse();

        bus.Undo().Should().BeTrue();
        doc.Comments.Should().ContainKey(7);
        paragraph.Runs.Should().HaveCount(2);
        paragraph.Runs[0].CommentId.Should().Be(7);
        paragraph.Runs[1].IsCommentReference.Should().BeTrue();

        bus.Redo().Should().BeTrue();
        doc.Comments.Should().NotContainKey(7);
        paragraph.Runs.Should().ContainSingle();
    }

    [Fact]
    public void ReplyAndResolveCommands_AreCommentHistory_AndUndoRedo()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Comments[0] = new Comment(0, "note", "Ann", "A");
        var bus = new DocumentCommandBus(new TestContext(doc));

        bus.Execute(new AddCommentReplyCommand(0, new Comment(1, "reply", "Bob", "B")));
        bus.NextUndoMutationKind.Should().Be(DocumentCommandMutationKind.Comment);
        doc.Comments[0].Replies.Should().ContainSingle();
        bus.Undo().Should().BeTrue();
        doc.Comments[0].Replies.Should().BeEmpty();
        bus.Redo().Should().BeTrue();
        doc.Comments[0].Replies.Should().ContainSingle();

        bus.Execute(new SetCommentResolvedCommand(0, true));
        bus.NextUndoMutationKind.Should().Be(DocumentCommandMutationKind.Comment);
        doc.Comments[0].Resolved.Should().BeTrue();
        bus.Undo().Should().BeTrue();
        doc.Comments[0].Resolved.Should().BeFalse();
        bus.Redo().Should().BeTrue();
        doc.Comments[0].Resolved.Should().BeTrue();
    }

    private static string AnchorText(Paragraph paragraph, int commentId) =>
        string.Concat(paragraph.Runs.Where(run => run.CommentId == commentId && !run.IsCommentReference).Select(run => run.Text));
}
