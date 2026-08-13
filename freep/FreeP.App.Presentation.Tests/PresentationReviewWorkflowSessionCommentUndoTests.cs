using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// Comment add/edit/delete/resolve/reopen/reply must all be routed through the presentation's
/// undo/redo command bus (round 134 fix). Each test performs the mutation, undoes it and asserts
/// the model is byte-for-byte back to its pre-mutation state, then redoes it and asserts the
/// mutation re-applies.
/// </summary>
public sealed class PresentationReviewWorkflowSessionCommentUndoTests
{
    [Fact]
    public void AddComment_UndoRemovesIt_RedoReAddsIt()
    {
        var (session, editor, slide) = CreateSession();

        session.AddComment("New note", author: "Alice", initials: "AL", xemu: 120, yemu: 240);

        slide.Comments.Should().ContainSingle();
        editor.CanUndo.Should().BeTrue();

        editor.Undo();

        slide.Comments.Should().BeEmpty("undo of Add must remove the comment entirely");

        editor.CanRedo.Should().BeTrue();
        editor.Redo();

        slide.Comments.Should().ContainSingle().Which.Should().Match<SlideComment>(c =>
            c.Text == "New note" && c.Author == "Alice" && c.Initials == "AL" &&
            c.Xemu == 120 && c.Yemu == 240);
    }

    [Fact]
    public void EditComment_UndoRestoresOriginalText_RedoReapplies()
    {
        var (session, editor, slide) = CreateSession();
        slide.Comments.Add(new SlideComment
        {
            Author = "Alice",
            Initials = "AL",
            Text = "Original text",
            Xemu = 10,
            Yemu = 20,
            Idx = 1,
        });
        var before = Snapshot(slide.Comments[0]);
        session.SelectedCommentIndex = 0;

        session.EditSelectedComment("Updated text", "Bob", "BB");

        slide.Comments[0].Text.Should().Be("Updated text");
        slide.Comments[0].Author.Should().Be("Bob");
        editor.CanUndo.Should().BeTrue();

        editor.Undo();

        slide.Comments.Should().ContainSingle();
        Snapshot(slide.Comments[0]).Should().BeEquivalentTo(before,
            "undo of Edit must restore the exact pre-edit comment");

        editor.CanRedo.Should().BeTrue();
        editor.Redo();

        slide.Comments[0].Text.Should().Be("Updated text");
        slide.Comments[0].Author.Should().Be("Bob");
        slide.Comments[0].Initials.Should().Be("BB");
    }

    [Fact]
    public void DeleteComment_UndoRestoresComment_RedoRemovesItAgain()
    {
        var (session, editor, slide) = CreateSession();
        slide.Comments.Add(new SlideComment
        {
            Author = "Alice",
            Initials = "AL",
            Text = "Keep me",
            Xemu = 30,
            Yemu = 40,
            Idx = 1,
        });
        var before = Snapshot(slide.Comments[0]);
        session.SelectedCommentIndex = 0;

        session.DeleteSelectedComment();

        slide.Comments.Should().BeEmpty();
        editor.CanUndo.Should().BeTrue();

        editor.Undo();

        slide.Comments.Should().ContainSingle();
        Snapshot(slide.Comments[0]).Should().BeEquivalentTo(before,
            "undo of Delete must bring the comment back exactly as it was");

        editor.CanRedo.Should().BeTrue();
        editor.Redo();

        slide.Comments.Should().BeEmpty();
    }

    [Fact]
    public void ResolveComment_UndoReopens_RedoResolvesAgain()
    {
        var (session, editor, slide) = CreateSession();
        slide.Comments.Add(new SlideComment
        {
            Author = "Alice",
            Initials = "AL",
            Text = "Please check this",
            Idx = 1,
        });
        var before = Snapshot(slide.Comments[0]);
        session.SelectedCommentIndex = 0;
        var resolvedAt = new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc);

        session.ResolveSelectedComment(resolvedAt, "Reviewer");

        slide.Comments[0].IsResolved.Should().BeTrue();
        slide.Comments[0].ResolvedBy.Should().Be("Reviewer");
        editor.CanUndo.Should().BeTrue();

        editor.Undo();

        Snapshot(slide.Comments[0]).Should().BeEquivalentTo(before,
            "undo of Resolve must restore the exact pre-resolve comment");
        slide.Comments[0].IsResolved.Should().BeFalse();

        editor.CanRedo.Should().BeTrue();
        editor.Redo();

        slide.Comments[0].IsResolved.Should().BeTrue();
        slide.Comments[0].ResolvedBy.Should().Be("Reviewer");
    }

    [Fact]
    public void ReopenComment_UndoRestoresResolvedState_RedoReopensAgain()
    {
        var (session, editor, slide) = CreateSession();
        var resolvedAt = new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc);
        slide.Comments.Add(new SlideComment
        {
            Author = "Alice",
            Initials = "AL",
            Text = "Please check this",
            Idx = 1,
            IsResolved = true,
            ResolvedDateTime = resolvedAt,
            ResolvedBy = "Reviewer",
        });
        var before = Snapshot(slide.Comments[0]);
        session.SelectedCommentIndex = 0;

        session.ReopenSelectedComment();

        slide.Comments[0].IsResolved.Should().BeFalse();
        editor.CanUndo.Should().BeTrue();

        editor.Undo();

        Snapshot(slide.Comments[0]).Should().BeEquivalentTo(before,
            "undo of Reopen must restore the exact pre-reopen (resolved) comment");
        slide.Comments[0].IsResolved.Should().BeTrue();
        slide.Comments[0].ResolvedBy.Should().Be("Reviewer");

        editor.CanRedo.Should().BeTrue();
        editor.Redo();

        slide.Comments[0].IsResolved.Should().BeFalse();
    }

    [Fact]
    public void ReplyToComment_UndoesAsOneUnit_RedoReappliesWholeReply()
    {
        var (session, editor, slide) = CreateSession();
        slide.Comments.Add(new SlideComment
        {
            Author = "Alice",
            Initials = "AL",
            Text = "Original question",
            Idx = 1,
        });
        var before = Snapshot(slide.Comments[0]);
        before.Replies.Should().BeEmpty();
        session.SelectedCommentIndex = 0;
        var replyAt = new DateTime(2026, 8, 1, 9, 5, 0, DateTimeKind.Utc);

        session.ReplyToSelectedComment("Here is the answer", replyAt, "Bob", "BB");

        slide.Comments.Should().ContainSingle();
        slide.Comments[0].Replies.Should().ContainSingle().Which.Text.Should().Be("Here is the answer");
        editor.CanUndo.Should().BeTrue();

        editor.Undo();

        // The reply must be undone as a single unit: no partial/per-field state, the whole
        // comment (replies included) reverts to exactly the pre-reply snapshot.
        slide.Comments.Should().ContainSingle();
        slide.Comments[0].Replies.Should().BeEmpty("undo of Reply must remove the whole reply, not partially");
        Snapshot(slide.Comments[0]).Should().BeEquivalentTo(before);

        editor.CanRedo.Should().BeTrue();
        editor.Redo();

        slide.Comments[0].Replies.Should().ContainSingle().Which.Text.Should().Be("Here is the answer");
    }

    [Fact]
    public void InvalidCommentMutation_DoesNotPushUndoEntry()
    {
        // Sibling/no-regression: a mutation that fails validation (no comment selected for
        // Delete) must not touch the model or the undo stack at all.
        var (session, editor, slide) = CreateSession();
        session.SelectedCommentIndex = null;

        var plan = session.DeleteSelectedComment();

        plan.ShouldApply.Should().BeFalse();
        slide.Comments.Should().BeEmpty();
        editor.CanUndo.Should().BeFalse();
    }

    private static (PresentationReviewWorkflowSession Session, EditingSession Editor, Slide Slide) CreateSession()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var session = new PresentationReviewWorkflowSession(
            () => editor,
            new PresentationReviewWorkflowSessionCallbacks(
                MarkDirty: () => { },
                RefreshCanvas: () => { },
                RefreshNotesPane: () => { },
                RenderAccessibilityCheckerPaneIfVisible: _ => { },
                PresentAccessibilityCheckerPane: _ => { },
                OpenAltTextPane: () => { },
                OpenHyperlinkDialog: () => { },
                OpenMediaCaptionPane: () => { },
                RenderCommentPane: _ => { },
                RenderAltTextPaneIfVisible: _ => { },
                RenderReadingOrderPaneIfVisible: _ => { },
                PresentReadingOrderPane: _ => { },
                RenderProofingPaneIfVisible: _ => { },
                PresentProofingPane: _ => { },
                UpdateAfterCommentMutation: () => { },
                UpdateAfterCommentNavigation: () => { },
                UpdateAfterProofingCorrection: () => { }));
        return (session, editor, slide);
    }

    private static CommentSnapshot Snapshot(SlideComment comment) => new(
        comment.Author,
        comment.Initials,
        comment.Text,
        comment.DateTime,
        comment.IsResolved,
        comment.ResolvedDateTime,
        comment.ResolvedBy,
        comment.Xemu,
        comment.Yemu,
        comment.Idx,
        comment.Replies.Select(r => (r.Author, r.Initials, r.Text, r.DateTime)).ToList());

    private sealed record CommentSnapshot(
        string Author,
        string Initials,
        string Text,
        DateTime? DateTime,
        bool IsResolved,
        DateTime? ResolvedDateTime,
        string ResolvedBy,
        long Xemu,
        long Yemu,
        int Idx,
        List<(string Author, string Initials, string Text, DateTime? DateTime)> Replies);
}
