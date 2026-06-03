using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public partial class CommentCommandTests
{
    [Fact]
    public void AddThreadedCommentReplyCommand_AppendsReplyAndUndoRestoresThread()
    {
        var (_, sheet, ctx) = Setup();
        var addr = new CellAddress(sheet.Id, 1, 1);
        var original = new ThreadedComment("Root", "Quinn")
        {
            Replies = [new CommentReply("First", "Codex")]
        };
        sheet.ThreadedComments[addr] = original;

        var command = new AddThreadedCommentReplyCommand(sheet.Id, addr, "Second", "User", ModifiedAtUtc);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        outcome.AffectedCells.Should().ContainSingle().Which.Should().Be(addr);
        sheet.ThreadedComments[addr].ModifiedAtUtc.Should().Be(ModifiedAtUtc);
        sheet.ThreadedComments[addr].Replies.Should().Equal(
            new CommentReply("First", "Codex"),
            new CommentReply("Second", "User")
            {
                CreatedAtUtc = ModifiedAtUtc,
                ModifiedAtUtc = ModifiedAtUtc
            });

        command.Revert(ctx);

        sheet.ThreadedComments[addr].Should().Be(original);
    }

    [Fact]
    public void AddThreadedCommentReplyCommand_MissingThreadedComment_Fails()
    {
        var (_, sheet, ctx) = Setup();
        var addr = new CellAddress(sheet.Id, 1, 1);

        var outcome = new AddThreadedCommentReplyCommand(sheet.Id, addr, "Reply").Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("No threaded comment");
        sheet.ThreadedComments.Should().BeEmpty();
    }

    [Fact]
    public void UpdateThreadedCommentReplyCommand_UpdatesOnlySelectedReplyAndUndoRestoresThread()
    {
        var (_, sheet, ctx) = Setup();
        var addr = new CellAddress(sheet.Id, 1, 1);
        var original = new ThreadedComment("Root", "Quinn")
        {
            Replies =
            [
                new CommentReply("First", "Codex"),
                new CommentReply("Second", "User")
            ],
            IsResolved = true
        };
        sheet.ThreadedComments[addr] = original;

        var command = new UpdateThreadedCommentReplyCommand(
            sheet.Id,
            addr,
            replyIndex: 1,
            text: "Updated second",
            timestampUtc: ModifiedAtUtc);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        outcome.AffectedCells.Should().ContainSingle().Which.Should().Be(addr);
        sheet.ThreadedComments[addr].Should().BeEquivalentTo(original with
        {
            ModifiedAtUtc = ModifiedAtUtc,
            Replies =
            [
                new CommentReply("First", "Codex"),
                new CommentReply("Updated second", "User")
                {
                    ModifiedAtUtc = ModifiedAtUtc
                }
            ]
        });

        command.Revert(ctx);

        sheet.ThreadedComments[addr].Should().Be(original);
    }

    [Fact]
    public void UpdateThreadedCommentReplyCommand_AppliesResolvedStateAsPartOfReplyEditAndUndoRestoresThread()
    {
        var (_, sheet, ctx) = Setup();
        var addr = new CellAddress(sheet.Id, 1, 1);
        var original = new ThreadedComment("Root", "Quinn")
        {
            Replies = [new CommentReply("First", "Codex")]
        };
        sheet.ThreadedComments[addr] = original;

        var command = new UpdateThreadedCommentReplyCommand(
            sheet.Id,
            addr,
            replyIndex: 0,
            text: "Updated first",
            isResolved: true,
            timestampUtc: ModifiedAtUtc);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.ThreadedComments[addr].Should().BeEquivalentTo(original with
        {
            IsResolved = true,
            ModifiedAtUtc = ModifiedAtUtc,
            Replies =
            [
                new CommentReply("Updated first", "Codex")
                {
                    ModifiedAtUtc = ModifiedAtUtc
                }
            ]
        });

        command.Revert(ctx);

        sheet.ThreadedComments[addr].Should().Be(original);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void UpdateThreadedCommentReplyCommand_InvalidReplyIndex_FailsAndPreservesThread(int replyIndex)
    {
        var (_, sheet, ctx) = Setup();
        var addr = new CellAddress(sheet.Id, 1, 1);
        var original = new ThreadedComment("Root", "Quinn")
        {
            Replies = [new CommentReply("Only reply", "Codex")]
        };
        sheet.ThreadedComments[addr] = original;

        var outcome = new UpdateThreadedCommentReplyCommand(sheet.Id, addr, replyIndex, "Updated").Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("No threaded comment reply");
        sheet.ThreadedComments[addr].Should().Be(original);
    }

    [Fact]
    public void UpdateThreadedCommentReplyCommand_RejectsProtectedSheetWithoutEditObjectsPermission()
    {
        var (_, sheet, ctx) = Setup();
        sheet.IsProtected = true;
        var addr = new CellAddress(sheet.Id, 1, 1);
        var original = new ThreadedComment("Root", "Quinn")
        {
            Replies = [new CommentReply("Reply", "Codex")]
        };
        sheet.ThreadedComments[addr] = original;

        var outcome = new UpdateThreadedCommentReplyCommand(sheet.Id, addr, replyIndex: 0, text: "Updated").Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet.ThreadedComments[addr].Should().Be(original);
    }

    [Fact]
    public void DeleteThreadedCommentReplyCommand_RemovesOnlySelectedReplyAndUndoRestoresThread()
    {
        var (_, sheet, ctx) = Setup();
        var addr = new CellAddress(sheet.Id, 1, 1);
        var original = new ThreadedComment("Root", "Quinn")
        {
            Replies =
            [
                new CommentReply("First", "Codex"),
                new CommentReply("Second", "User"),
                new CommentReply("Third", "Reviewer")
            ],
            IsResolved = true
        };
        sheet.ThreadedComments[addr] = original;

        var command = new DeleteThreadedCommentReplyCommand(sheet.Id, addr, replyIndex: 1, timestampUtc: ModifiedAtUtc);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        outcome.AffectedCells.Should().ContainSingle().Which.Should().Be(addr);
        sheet.ThreadedComments[addr].Should().BeEquivalentTo(original with
        {
            ModifiedAtUtc = ModifiedAtUtc,
            Replies =
            [
                new CommentReply("First", "Codex"),
                new CommentReply("Third", "Reviewer")
            ]
        });

        command.Revert(ctx);

        sheet.ThreadedComments[addr].Should().Be(original);
    }

    [Fact]
    public void DeleteThreadedCommentReplyCommand_AppliesResolvedStateAsPartOfReplyDeleteAndUndoRestoresThread()
    {
        var (_, sheet, ctx) = Setup();
        var addr = new CellAddress(sheet.Id, 1, 1);
        var original = new ThreadedComment("Root", "Quinn")
        {
            Replies =
            [
                new CommentReply("First", "Codex"),
                new CommentReply("Second", "User")
            ]
        };
        sheet.ThreadedComments[addr] = original;

        var command = new DeleteThreadedCommentReplyCommand(
            sheet.Id,
            addr,
            replyIndex: 0,
            isResolved: true,
            timestampUtc: ModifiedAtUtc);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.ThreadedComments[addr].Should().BeEquivalentTo(original with
        {
            IsResolved = true,
            ModifiedAtUtc = ModifiedAtUtc,
            Replies = [new CommentReply("Second", "User")]
        });

        command.Revert(ctx);

        sheet.ThreadedComments[addr].Should().Be(original);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void DeleteThreadedCommentReplyCommand_InvalidReplyIndex_FailsAndPreservesThread(int replyIndex)
    {
        var (_, sheet, ctx) = Setup();
        var addr = new CellAddress(sheet.Id, 1, 1);
        var original = new ThreadedComment("Root", "Quinn")
        {
            Replies = [new CommentReply("Only reply", "Codex")]
        };
        sheet.ThreadedComments[addr] = original;

        var outcome = new DeleteThreadedCommentReplyCommand(sheet.Id, addr, replyIndex).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("No threaded comment reply");
        sheet.ThreadedComments[addr].Should().Be(original);
    }

    [Fact]
    public void DeleteThreadedCommentReplyCommand_RejectsProtectedSheetWithoutEditObjectsPermission()
    {
        var (_, sheet, ctx) = Setup();
        sheet.IsProtected = true;
        var addr = new CellAddress(sheet.Id, 1, 1);
        var original = new ThreadedComment("Root", "Quinn")
        {
            Replies = [new CommentReply("Reply", "Codex")]
        };
        sheet.ThreadedComments[addr] = original;

        var outcome = new DeleteThreadedCommentReplyCommand(sheet.Id, addr, replyIndex: 0).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet.ThreadedComments[addr].Should().Be(original);
    }
}
