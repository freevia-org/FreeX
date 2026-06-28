using FluentAssertions;
using FreeX.App.Presentation.Comments;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class ThreadedCommentDialogTests
{
    [Fact]
    public void ReplyEditResult_CapturesSelectedReplyIndexAndTrimmedText()
    {
        var existing = new ThreadedComment("Root note", "Quinn")
        {
            Replies =
            [
                new CommentReply("First", "Codex"),
                new CommentReply("Second", "FreeX")
            ]
        };

        ThreadedCommentDialog.TryCreateReplyEditResult(existing, 1, "  Updated second  ", out var result, out var error)
            .Should()
            .BeTrue(error);

        result.Should().Be(new ThreadedCommentDialogResult(
            null,
            null,
            false,
            ThreadedCommentDialogAction.EditReply,
            1,
            "Updated second"));
    }

    [Fact]
    public void ReplyEditResult_CapturesResolvedStateForSelectedReplyAction()
    {
        var existing = new ThreadedComment("Root note", "Quinn")
        {
            Replies = [new CommentReply("First", "Codex")]
        };

        ThreadedCommentDialog.TryCreateReplyEditResult(existing, 0, "Updated", true, out var result, out var error)
            .Should()
            .BeTrue(error);

        result.Should().Be(new ThreadedCommentDialogResult(
            null,
            null,
            true,
            ThreadedCommentDialogAction.EditReply,
            0,
            "Updated"));
    }

    [Fact]
    public void ReplyDeleteResult_CapturesSelectedReplyIndex()
    {
        var existing = new ThreadedComment("Root note", "Quinn")
        {
            Replies = [new CommentReply("First", "Codex")]
        };

        ThreadedCommentDialog.TryCreateReplyDeleteResult(existing, 0, out var result, out var error)
            .Should()
            .BeTrue(error);

        result.Should().Be(new ThreadedCommentDialogResult(
            null,
            null,
            false,
            ThreadedCommentDialogAction.DeleteReply,
            0));
    }

    [Fact]
    public void ReplyDeleteResult_CapturesResolvedStateForSelectedReplyAction()
    {
        var existing = new ThreadedComment("Root note", "Quinn")
        {
            Replies = [new CommentReply("First", "Codex")]
        };

        ThreadedCommentDialog.TryCreateReplyDeleteResult(existing, 0, true, out var result, out var error)
            .Should()
            .BeTrue(error);

        result.Should().Be(new ThreadedCommentDialogResult(
            null,
            null,
            true,
            ThreadedCommentDialogAction.DeleteReply,
            0));
    }

    [Fact]
    public void ReplyEditResult_RejectsBlankReplyText()
    {
        var existing = new ThreadedComment("Root note", "Quinn")
        {
            Replies = [new CommentReply("First", "Codex")]
        };

        ThreadedCommentDialog.TryCreateReplyEditResult(existing, 0, " ", out _, out var error)
            .Should()
            .BeFalse();

        error.Should().Be(UiText.Get("ThreadedComment_EnterReplyMessage"));
    }
}
