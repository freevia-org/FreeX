using FluentAssertions;
using FreeX.App.Presentation.Comments;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Comments;

public sealed class ThreadedCommentDialogPlannerTests
{
    [Fact]
    public void CreateResult_DistinguishesNewThreadRootFromExistingReply()
    {
        var existing = new ThreadedComment("Old root", "Quinn")
        {
            Replies = [new CommentReply("Existing reply", "Codex")]
        };

        ThreadedCommentDialogPlanner.CreateResult(null, "  New root  ", "", isResolved: false)
            .Should()
            .Be(new ThreadedCommentDialogResult(null, "New root", false));
        ThreadedCommentDialogPlanner.CreateResult(existing, "  Edited root  ", "  Reply text  ", isResolved: true)
            .Should()
            .Be(new ThreadedCommentDialogResult("Edited root", "Reply text", true));
        ThreadedCommentDialogPlanner.CreateResult(existing, " Old root ", " ", isResolved: false)
            .Should()
            .Be(new ThreadedCommentDialogResult(null, null, false));
    }

    [Fact]
    public void TryCreateResult_RejectsBlankNewComment()
    {
        ThreadedCommentDialogPlanner.TryCreateResult(null, " ", "", isResolved: false, out _, out var error)
            .Should()
            .BeFalse();

        error.Should().Be(ThreadedCommentDialogValidationError.EnterComment);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void TryCreateResult_RejectsBlankExistingRootEdit(string rootText)
    {
        var existing = new ThreadedComment("Old root", "Quinn");

        ThreadedCommentDialogPlanner.TryCreateResult(existing, rootText, "Reply", isResolved: false, out _, out var error)
            .Should()
            .BeFalse();

        error.Should().Be(ThreadedCommentDialogValidationError.EnterComment);
    }

    [Fact]
    public void TryCreateReplyEditResult_CapturesSelectedReplyIndexAndTrimmedText()
    {
        var existing = new ThreadedComment("Root note", "Quinn")
        {
            Replies =
            [
                new CommentReply("First", "Codex"),
                new CommentReply("Second", "FreeX")
            ]
        };

        ThreadedCommentDialogPlanner.TryCreateReplyEditResult(existing, 1, "  Updated second  ", out var result, out var error)
            .Should()
            .BeTrue();

        error.Should().Be(ThreadedCommentDialogValidationError.None);
        result.Should().Be(new ThreadedCommentDialogResult(
            null,
            null,
            false,
            ThreadedCommentDialogAction.EditReply,
            1,
            "Updated second"));
    }

    [Fact]
    public void TryCreateReplyEditResult_RejectsUnavailableInvalidOrBlankReply()
    {
        var existing = new ThreadedComment("Root note", "Quinn")
        {
            Replies = [new CommentReply("First", "Codex")]
        };

        ThreadedCommentDialogPlanner.TryCreateReplyEditResult(null, 0, "Updated", out _, out var missingError)
            .Should()
            .BeFalse();
        ThreadedCommentDialogPlanner.TryCreateReplyEditResult(existing, 9, "Updated", out _, out var indexError)
            .Should()
            .BeFalse();
        ThreadedCommentDialogPlanner.TryCreateReplyEditResult(existing, 0, " ", out _, out var blankError)
            .Should()
            .BeFalse();

        missingError.Should().Be(ThreadedCommentDialogValidationError.NoThreadedCommentAvailable);
        indexError.Should().Be(ThreadedCommentDialogValidationError.SelectReply);
        blankError.Should().Be(ThreadedCommentDialogValidationError.EnterReply);
    }

    [Fact]
    public void TryCreateReplyDeleteResult_CapturesSelectionAndResolvedState()
    {
        var existing = new ThreadedComment("Root note", "Quinn")
        {
            Replies = [new CommentReply("First", "Codex")]
        };

        ThreadedCommentDialogPlanner.TryCreateReplyDeleteResult(existing, 0, true, out var result, out var error)
            .Should()
            .BeTrue();

        error.Should().Be(ThreadedCommentDialogValidationError.None);
        result.Should().Be(new ThreadedCommentDialogResult(
            null,
            null,
            true,
            ThreadedCommentDialogAction.DeleteReply,
            0));
    }

    [Fact]
    public void TryCreateReplyDeleteResult_RejectsUnavailableOrInvalidReply()
    {
        var existing = new ThreadedComment("Root note", "Quinn")
        {
            Replies = [new CommentReply("First", "Codex")]
        };

        ThreadedCommentDialogPlanner.TryCreateReplyDeleteResult(null, 0, out _, out var missingError)
            .Should()
            .BeFalse();
        ThreadedCommentDialogPlanner.TryCreateReplyDeleteResult(existing, -1, out _, out var indexError)
            .Should()
            .BeFalse();

        missingError.Should().Be(ThreadedCommentDialogValidationError.NoThreadedCommentAvailable);
        indexError.Should().Be(ThreadedCommentDialogValidationError.SelectReply);
    }

    [Fact]
    public void DescribeReply_OwnsChoiceTextAndLocalizedAutomationName()
    {
        var reply = new CommentReply(
            "First line\r\nsecond line with enough trailing text to trigger a compact summary tail",
            "  Codex  ")
        {
            CreatedAtUtc = new DateTimeOffset(2026, 6, 28, 12, 34, 0, TimeSpan.FromHours(2))
        };

        var descriptor = ThreadedCommentDialogPlanner.DescribeReply(1, reply);

        descriptor.ChoiceText.Should().Be(
            "2. Codex - 2026-06-28 10:34 UTC: First line  second line with enough trailing text to trig...");
        descriptor.AutomationName.ResourceKey.Should().Be("ThreadedComment_ReplyAutomationNameFormat");
        descriptor.AutomationName.LiteralText.Should().BeNull();
        descriptor.AutomationName.Arguments.Should().Equal(
            2,
            "Codex - 2026-06-28 10:34 UTC",
            "First line  second line with enough trailing text to trig...");
        ThreadedCommentDialogPlanner.FormatReplyChoice(1, reply).Should().Be(descriptor.ChoiceText);
    }

    [Fact]
    public void ReplySemanticIds_PreserveExistingAccessibilityContracts()
    {
        ThreadedCommentDialogPlanner.ReplySelectorAutomationId.Should().Be("ThreadedCommentReplySelector");
        ThreadedCommentDialogPlanner.SelectedReplyEditorAutomationId.Should().Be("ThreadedCommentSelectedReplyBox");
        ThreadedCommentDialogPlanner.UpdateReplyAutomationId.Should().Be("ThreadedCommentUpdateReplyButton");
        ThreadedCommentDialogPlanner.DeleteReplyAutomationId.Should().Be("ThreadedCommentDeleteReplyButton");
    }

    [Fact]
    public void FormatMessageHeading_UsesTimestampWhenAuthorIsBlank()
    {
        ThreadedCommentDialogPlanner.FormatMessageHeading(" ", new DateTimeOffset(2026, 6, 28, 12, 34, 0, TimeSpan.FromHours(2)))
            .Should()
            .Be("2026-06-28 10:34 UTC");
    }
}
