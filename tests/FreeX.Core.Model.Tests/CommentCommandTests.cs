using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public class CommentCommandTests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new SimpleCtx(wb));
    }

    [Fact]
    public void SetCommentCommand_AddsCommentAndUndoRemovesIt()
    {
        var (_, sheet, ctx) = Setup();
        var addr = new CellAddress(sheet.Id, 1, 1);

        var cmd = new SetCommentCommand(sheet.Id, addr, "Review this");
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.Comments[addr].Should().Be("Review this");

        cmd.Revert(ctx);

        sheet.Comments.Should().NotContainKey(addr);
    }

    [Fact]
    public void SetCommentCommand_ReplacesExistingCommentAndUndoRestoresIt()
    {
        var (_, sheet, ctx) = Setup();
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.Comments[addr] = "Old";

        var cmd = new SetCommentCommand(sheet.Id, addr, "New");
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        sheet.Comments[addr].Should().Be("Old");
    }

    [Fact]
    public void DeleteCommentCommand_RemovesCommentAndUndoRestoresIt()
    {
        var (_, sheet, ctx) = Setup();
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.Comments[addr] = "Keep me";

        var cmd = new DeleteCommentCommand(sheet.Id, addr);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.Comments.Should().NotContainKey(addr);

        cmd.Revert(ctx);

        sheet.Comments[addr].Should().Be("Keep me");
    }

    [Fact]
    public void DeleteCommentCommand_MissingComment_Fails()
    {
        var (_, sheet, ctx) = Setup();
        var addr = new CellAddress(sheet.Id, 1, 1);

        var outcome = new DeleteCommentCommand(sheet.Id, addr).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("No comment");
    }

    [Fact]
    public void ClearCommentsCommand_RemovesCommentsInRangeAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        sheet.Comments[a1] = "A";
        sheet.Comments[b1] = "B";
        sheet.Comments[c1] = "C";
        var range = new GridRange(a1, b1);

        var cmd = new ClearCommentsCommand(sheet.Id, range);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.Comments.Should().NotContainKey(a1);
        sheet.Comments.Should().NotContainKey(b1);
        sheet.Comments[c1].Should().Be("C");

        cmd.Revert(ctx);

        sheet.Comments[a1].Should().Be("A");
        sheet.Comments[b1].Should().Be("B");
        sheet.Comments[c1].Should().Be("C");
    }

    [Fact]
    public void ClearCommentsCommand_RemovesThreadedCommentsInRangeAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        sheet.ThreadedComments[a1] = new ThreadedComment("A", "Quinn");
        sheet.ThreadedComments[b1] = new ThreadedComment("B", "Codex");
        sheet.ThreadedComments[c1] = new ThreadedComment("C", "FreeX");
        var range = new GridRange(a1, b1);

        var cmd = new ClearCommentsCommand(sheet.Id, range);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        outcome.AffectedCells.Should().BeEquivalentTo([a1, b1]);
        sheet.ThreadedComments.Should().NotContainKey(a1);
        sheet.ThreadedComments.Should().NotContainKey(b1);
        sheet.ThreadedComments[c1].Should().Be(new ThreadedComment("C", "FreeX"));

        cmd.Revert(ctx);

        sheet.ThreadedComments[a1].Should().Be(new ThreadedComment("A", "Quinn"));
        sheet.ThreadedComments[b1].Should().Be(new ThreadedComment("B", "Codex"));
        sheet.ThreadedComments[c1].Should().Be(new ThreadedComment("C", "FreeX"));
    }

    [Fact]
    public void ClearCommentsCommand_RejectsProtectedSheetWithoutEditObjectsPermission()
    {
        var (_, sheet, ctx) = Setup();
        sheet.IsProtected = true;
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.Comments[addr] = "Keep";

        var outcome = new ClearCommentsCommand(sheet.Id, new GridRange(addr, addr)).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet.Comments[addr].Should().Be("Keep");
    }

    [Fact]
    public void ClearCommentsCommand_AllowsProtectedSheetWithEditObjectsPermission()
    {
        var (_, sheet, ctx) = Setup();
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.EditObjects);
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.Comments[addr] = "Clear me";

        var outcome = new ClearCommentsCommand(sheet.Id, new GridRange(addr, addr)).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.Comments.Should().NotContainKey(addr);
    }

    [Fact]
    public void SetThreadedCommentCommand_AddsThreadedCommentAndUndoRemovesIt()
    {
        var (_, sheet, ctx) = Setup();
        var addr = new CellAddress(sheet.Id, 1, 1);

        var cmd = new SetThreadedCommentCommand(sheet.Id, addr, "Start discussion");
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.ThreadedComments[addr].Text.Should().Be("Start discussion");

        cmd.Revert(ctx);

        sheet.ThreadedComments.Should().NotContainKey(addr);
    }

    [Fact]
    public void SetThreadedCommentCommand_ReplacesExistingThreadedCommentAndUndoRestoresIt()
    {
        var (_, sheet, ctx) = Setup();
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.ThreadedComments[addr] = new ThreadedComment("Old", "Quinn");

        var cmd = new SetThreadedCommentCommand(sheet.Id, addr, "New", "Codex");
        cmd.Apply(ctx);
        sheet.ThreadedComments[addr].Should().Be(new ThreadedComment("New", "Codex"));

        cmd.Revert(ctx);

        sheet.ThreadedComments[addr].Should().Be(new ThreadedComment("Old", "Quinn"));
    }

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

        var command = new AddThreadedCommentReplyCommand(sheet.Id, addr, "Second", "User");
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        outcome.AffectedCells.Should().ContainSingle().Which.Should().Be(addr);
        sheet.ThreadedComments[addr].Replies.Should().Equal(
            new CommentReply("First", "Codex"),
            new CommentReply("Second", "User"));

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
    public void UpdateThreadedCommentTextCommand_UpdatesRootTextAndPreservesThreadMetadata()
    {
        var (_, sheet, ctx) = Setup();
        var addr = new CellAddress(sheet.Id, 1, 1);
        var original = new ThreadedComment("Old root", "Quinn")
        {
            Replies = [new CommentReply("Reply", "Codex")],
            IsResolved = true
        };
        sheet.ThreadedComments[addr] = original;

        var command = new UpdateThreadedCommentTextCommand(sheet.Id, addr, "New root");
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        outcome.AffectedCells.Should().ContainSingle().Which.Should().Be(addr);
        sheet.ThreadedComments[addr].Should().Be(original with { Text = "New root" });

        command.Revert(ctx);

        sheet.ThreadedComments[addr].Should().Be(original);
    }

    [Fact]
    public void UpdateThreadedCommentTextCommand_MissingThreadedComment_Fails()
    {
        var (_, sheet, ctx) = Setup();
        var addr = new CellAddress(sheet.Id, 1, 1);

        var outcome = new UpdateThreadedCommentTextCommand(sheet.Id, addr, "New root").Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("No threaded comment");
    }

    [Fact]
    public void UpdateThreadedCommentTextCommand_RejectsProtectedSheetWithoutEditObjectsPermission()
    {
        var (_, sheet, ctx) = Setup();
        sheet.IsProtected = true;
        var addr = new CellAddress(sheet.Id, 1, 1);
        var original = new ThreadedComment("Old root", "Quinn");
        sheet.ThreadedComments[addr] = original;

        var outcome = new UpdateThreadedCommentTextCommand(sheet.Id, addr, "New root").Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet.ThreadedComments[addr].Should().Be(original);
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

        var command = new UpdateThreadedCommentReplyCommand(sheet.Id, addr, replyIndex: 1, text: "Updated second");
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        outcome.AffectedCells.Should().ContainSingle().Which.Should().Be(addr);
        sheet.ThreadedComments[addr].Should().BeEquivalentTo(original with
        {
            Replies =
            [
                new CommentReply("First", "Codex"),
                new CommentReply("Updated second", "User")
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

        var command = new DeleteThreadedCommentReplyCommand(sheet.Id, addr, replyIndex: 1);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        outcome.AffectedCells.Should().ContainSingle().Which.Should().Be(addr);
        sheet.ThreadedComments[addr].Should().BeEquivalentTo(original with
        {
            Replies =
            [
                new CommentReply("First", "Codex"),
                new CommentReply("Third", "Reviewer")
            ]
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

    [Fact]
    public void ResolveThreadedCommentCommand_TogglesResolvedStateAndUndoRestoresThread()
    {
        var (_, sheet, ctx) = Setup();
        var addr = new CellAddress(sheet.Id, 1, 1);
        var original = new ThreadedComment("Root", "Quinn")
        {
            Replies = [new CommentReply("Reply", "Codex")]
        };
        sheet.ThreadedComments[addr] = original;

        var command = new ResolveThreadedCommentCommand(sheet.Id, addr, resolved: true);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.ThreadedComments[addr].Should().Be(original with { IsResolved = true });

        command.Revert(ctx);

        sheet.ThreadedComments[addr].Should().Be(original);
    }

    [Fact]
    public void ResolveThreadedCommentCommand_MissingThreadedComment_Fails()
    {
        var (_, sheet, ctx) = Setup();
        var addr = new CellAddress(sheet.Id, 1, 1);

        var outcome = new ResolveThreadedCommentCommand(sheet.Id, addr, resolved: true).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("No threaded comment");
        sheet.ThreadedComments.Should().BeEmpty();
    }

    [Fact]
    public void ApplyThreadedCommentChangesCommand_AppliesEditReplyAndResolvedStateAsOneUndoableChange()
    {
        var (_, sheet, ctx) = Setup();
        var addr = new CellAddress(sheet.Id, 1, 1);
        var original = new ThreadedComment("Old root", "Quinn")
        {
            Replies = [new CommentReply("First reply", "Codex")]
        };
        sheet.ThreadedComments[addr] = original;

        var command = new ApplyThreadedCommentChangesCommand(
            sheet.Id,
            addr,
            rootText: "New root",
            replyText: "Second reply",
            isResolved: true,
            replyAuthor: "Reviewer");
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        outcome.AffectedCells.Should().ContainSingle().Which.Should().Be(addr);
        sheet.ThreadedComments[addr].Should().BeEquivalentTo(new ThreadedComment("New root", "Quinn")
        {
            Replies =
            [
                new CommentReply("First reply", "Codex"),
                new CommentReply("Second reply", "Reviewer")
            ],
            IsResolved = true
        });

        command.Revert(ctx);

        sheet.ThreadedComments[addr].Should().Be(original);
    }

    [Fact]
    public void ApplyThreadedCommentChangesCommand_NoChanges_FailsAndPreservesThread()
    {
        var (_, sheet, ctx) = Setup();
        var addr = new CellAddress(sheet.Id, 1, 1);
        var original = new ThreadedComment("Root", "Quinn")
        {
            Replies = [new CommentReply("Reply", "Codex")],
            IsResolved = true
        };
        sheet.ThreadedComments[addr] = original;

        var outcome = new ApplyThreadedCommentChangesCommand(
            sheet.Id,
            addr,
            rootText: null,
            replyText: " ",
            isResolved: true).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("No threaded comment changes");
        sheet.ThreadedComments[addr].Should().Be(original);
    }

    [Fact]
    public void ApplyThreadedCommentChangesCommand_RejectsProtectedSheetWithoutEditObjectsPermission()
    {
        var (_, sheet, ctx) = Setup();
        sheet.IsProtected = true;
        var addr = new CellAddress(sheet.Id, 1, 1);
        var original = new ThreadedComment("Root", "Quinn");
        sheet.ThreadedComments[addr] = original;

        var outcome = new ApplyThreadedCommentChangesCommand(
            sheet.Id,
            addr,
            rootText: "Edited",
            replyText: "Reply",
            isResolved: true).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet.ThreadedComments[addr].Should().Be(original);
    }

    [Fact]
    public void DeleteThreadedCommentCommand_RemovesThreadedCommentAndUndoRestoresIt()
    {
        var (_, sheet, ctx) = Setup();
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.ThreadedComments[addr] = new ThreadedComment("Keep me", "Quinn");

        var cmd = new DeleteThreadedCommentCommand(sheet.Id, addr);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        outcome.AffectedCells.Should().ContainSingle().Which.Should().Be(addr);
        sheet.ThreadedComments.Should().NotContainKey(addr);

        cmd.Revert(ctx);

        sheet.ThreadedComments[addr].Should().Be(new ThreadedComment("Keep me", "Quinn"));
    }

    [Fact]
    public void DeleteThreadedCommentCommand_MissingThreadedComment_Fails()
    {
        var (_, _, ctx) = Setup();
        var addr = new CellAddress(ctx.Workbook.Sheets[0].Id, 1, 1);

        var outcome = new DeleteThreadedCommentCommand(ctx.Workbook.Sheets[0].Id, addr).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("No threaded comment");
    }

    [Fact]
    public void DeleteThreadedCommentCommand_RejectsProtectedSheetWithoutEditObjectsPermission()
    {
        var (_, sheet, ctx) = Setup();
        sheet.IsProtected = true;
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.ThreadedComments[addr] = new ThreadedComment("Keep", "Quinn");

        var outcome = new DeleteThreadedCommentCommand(sheet.Id, addr).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet.ThreadedComments[addr].Should().Be(new ThreadedComment("Keep", "Quinn"));
    }

    [Fact]
    public void DeleteThreadedCommentCommand_AllowsProtectedSheetWithEditObjectsPermission()
    {
        var (_, sheet, ctx) = Setup();
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.EditObjects);
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.ThreadedComments[addr] = new ThreadedComment("Delete me", "Quinn");

        var outcome = new DeleteThreadedCommentCommand(sheet.Id, addr).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.ThreadedComments.Should().NotContainKey(addr);
    }

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
