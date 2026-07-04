using System.Linq;
using FreeW.App.Host.Editing;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Editor-behaviour coverage for threaded comments: replying to / resolving the comment thread covering the
/// caret. The caret is placed inside the commented paragraph so the editor resolves the covering comment from
/// the committed model, then Reply appends a child comment and Resolve toggles the thread's done flag.
/// </summary>
public sealed class ThreadedCommentCommandTests
{
    // A one-paragraph document whose only run is covered by comment id 0, with the matching anchor run.
    private static DocumentView ViewWithOneComment()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(new Run("Reviewed text") { CommentId = 0 });
        para.Runs.Add(Run.CommentReference(0));
        doc.Blocks.Add(para);
        doc.Comments[0] = new Comment(0, "Please clarify", "Alice", "A");

        var view = new DocumentView();
        view.LoadModel(doc);
        // Place the caret at the start of the (only) commented paragraph.
        view.CaretPosition = view.Document.Blocks.FirstBlock!.ContentStart;
        return view;
    }

    private static DocumentView ViewWithTwoComments()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();

        var first = new Paragraph();
        first.Runs.Add(new Run("First reviewed text") { CommentId = 0 });
        first.Runs.Add(Run.CommentReference(0));
        doc.Blocks.Add(first);

        doc.Blocks.Add(new Paragraph("Plain text"));

        var second = new Paragraph();
        second.Runs.Add(new Run("Second reviewed text") { CommentId = 2 });
        second.Runs.Add(Run.CommentReference(2));
        doc.Blocks.Add(second);

        doc.Comments[0] = new Comment(0, "First note", "Alice", "A");
        doc.Comments[2] = new Comment(2, "Second note", "Casey", "C");

        var view = new DocumentView();
        view.LoadModel(doc);
        view.CaretPosition = view.Document.Blocks.FirstBlock!.ContentStart;
        return view;
    }

    [StaFact]
    public void ReplyToCommentAtCaret_AppendsAReply()
    {
        var view = ViewWithOneComment();

        var added = view.ReplyToCommentAtCaret("Clarified above", "Bob", "B");

        added.Should().BeTrue();
        var comment = view.Model.Comments[0];
        comment.Replies.Should().HaveCount(1);
        comment.Replies[0].PlainText.Should().Be("Clarified above");
        comment.Replies[0].Author.Should().Be("Bob");
        // The reply got a fresh, document-unique id (clear of the parent's id 0).
        comment.Replies[0].Id.Should().Be(1);

        view.Commands.CanUndo.Should().BeTrue();
        view.Undo();
        comment.Replies.Should().BeEmpty();
        view.Commands.CanRedo.Should().BeTrue();
        view.Redo();
        comment.Replies.Should().ContainSingle();
    }

    [StaFact]
    public void ToggleResolveCommentAtCaret_TogglesResolved()
    {
        var view = ViewWithOneComment();
        view.Model.Comments[0].Resolved.Should().BeFalse();

        view.ToggleResolveCommentAtCaret().Should().Be(true);
        view.Model.Comments[0].Resolved.Should().BeTrue();
        view.Commands.CanUndo.Should().BeTrue();
        view.Undo();
        view.Model.Comments[0].Resolved.Should().BeFalse();
        view.Commands.CanRedo.Should().BeTrue();
        view.Redo();
        view.Model.Comments[0].Resolved.Should().BeTrue();

        view.ToggleResolveCommentAtCaret().Should().Be(false);
        view.Model.Comments[0].Resolved.Should().BeFalse();
    }

    [StaFact]
    public void ReplyAndResolve_NoOpWithoutACommentAtCaret()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph();
        para.Runs.Add(new Run("Plain text"));
        doc.Blocks.Add(para);

        var view = new DocumentView();
        view.LoadModel(doc);
        view.CaretPosition = view.Document.Blocks.FirstBlock!.ContentStart;

        view.ReplyToCommentAtCaret("nope", "Bob", "B").Should().BeFalse();
        view.ToggleResolveCommentAtCaret().Should().BeNull();
    }

    [StaFact]
    public void DeleteCommentAtCaret_RemovesThreadRangeAndReference()
    {
        var view = ViewWithOneComment();
        view.Model.Comments[0].AddReply(1, "Follow-up", "Bob", "B");

        view.DeleteCommentAtCaret().Should().BeTrue();

        view.Model.Comments.Should().NotContainKey(0);
        var paragraph = (Paragraph)view.Model.Blocks[0];
        paragraph.Runs.Should().ContainSingle();
        paragraph.Runs[0].Text.Should().Be("Reviewed text");
        paragraph.Runs[0].CommentId.Should().BeNull();
        paragraph.Runs[0].IsCommentReference.Should().BeFalse();

        view.Commands.CanUndo.Should().BeTrue();
        view.Undo();
        view.Model.Comments.Should().ContainKey(0);
        paragraph.Runs.Should().HaveCount(2);
        paragraph.Runs[0].CommentId.Should().Be(0);
        paragraph.Runs[1].IsCommentReference.Should().BeTrue();

        view.Commands.CanRedo.Should().BeTrue();
        view.Redo();
        view.Model.Comments.Should().NotContainKey(0);
        paragraph.Runs.Should().ContainSingle();
    }

    [StaFact]
    public void CommentNavigation_MovesBetweenThreadsInDocumentOrder()
    {
        var view = ViewWithTwoComments();

        view.MoveToNextComment().Should().BeTrue();
        view.ToggleResolveCommentAtCaret().Should().BeTrue();
        view.Model.Comments[2].Resolved.Should().BeTrue();
        view.Model.Comments[0].Resolved.Should().BeFalse();

        view.BringBlockIntoView(2);
        view.MoveToPreviousComment().Should().BeTrue();
        view.ToggleResolveCommentAtCaret().Should().BeTrue();
        view.Model.Comments[0].Resolved.Should().BeTrue();
    }

    [StaFact]
    public void CommentNavigation_WrapsAndNoOpsWithoutComments()
    {
        var view = ViewWithTwoComments();

        view.MoveToPreviousComment().Should().BeTrue();
        view.DeleteCommentAtCaret().Should().BeTrue();
        view.Model.Comments.Should().NotContainKey(2);

        var plain = new DocumentView();
        plain.LoadModel(TextDocument.CreateEmpty());
        plain.MoveToNextComment().Should().BeFalse();
        plain.MoveToPreviousComment().Should().BeFalse();
        plain.DeleteCommentAtCaret().Should().BeFalse();
    }

    [StaFact]
    public void CommentNavigation_HandlesCommentsInsideTableCells()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();

        var table = new Table();
        var row = new TableRow();
        var cell = new TableCell();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Table reviewed text") { CommentId = 4 });
        paragraph.Runs.Add(Run.CommentReference(4));
        cell.Paragraphs.Add(paragraph);
        row.Cells.Add(cell);
        table.Rows.Add(row);
        doc.Blocks.Add(table);
        doc.Comments[4] = new Comment(4, "Table note", "Drew", "D");

        var view = new DocumentView();
        view.LoadModel(doc);

        view.MoveToNextComment().Should().BeTrue();
        view.ToggleResolveCommentAtCaret().Should().BeTrue();
        view.DeleteCommentAtCaret().Should().BeTrue();
        view.Model.Comments.Should().NotContainKey(4);
    }
}
