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
    }

    [StaFact]
    public void ToggleResolveCommentAtCaret_TogglesResolved()
    {
        var view = ViewWithOneComment();
        view.Model.Comments[0].Resolved.Should().BeFalse();

        view.ToggleResolveCommentAtCaret().Should().Be(true);
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
}
