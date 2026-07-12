using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public partial class CommentCommandTests
{
    [Fact]
    public void SetThreadedCommentCommand_AddsThreadedCommentAndUndoRemovesIt()
    {
        var (_, sheet, ctx) = Setup();
        var addr = new CellAddress(sheet.Id, 1, 1);

        var cmd = new SetThreadedCommentCommand(sheet.Id, addr, "Start discussion", timestampUtc: CreatedAtUtc);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.ThreadedComments[addr].Text.Should().Be("Start discussion");
        sheet.ThreadedComments[addr].CreatedAtUtc.Should().Be(CreatedAtUtc);
        sheet.ThreadedComments[addr].ModifiedAtUtc.Should().Be(CreatedAtUtc);

        cmd.Revert(ctx);

        sheet.ThreadedComments.Should().NotContainKey(addr);
    }

    [Fact]
    public void SetThreadedCommentCommand_ReplacesExistingThreadedCommentAndUndoRestoresIt()
    {
        var (_, sheet, ctx) = Setup();
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.ThreadedComments[addr] = new ThreadedComment("Old", "Quinn");

        var cmd = new SetThreadedCommentCommand(sheet.Id, addr, "New", "Codex", CreatedAtUtc);
        cmd.Apply(ctx);
        sheet.ThreadedComments[addr].Should().Be(new ThreadedComment("New", "Codex")
        {
            CreatedAtUtc = CreatedAtUtc,
            ModifiedAtUtc = CreatedAtUtc
        });

        cmd.Revert(ctx);

        sheet.ThreadedComments[addr].Should().Be(new ThreadedComment("Old", "Quinn"));
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

        var command = new UpdateThreadedCommentTextCommand(sheet.Id, addr, "New root", ModifiedAtUtc);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        outcome.AffectedCells.Should().ContainSingle().Which.Should().Be(addr);
        sheet.ThreadedComments[addr].Should().Be(original with
        {
            Text = "New root",
            ModifiedAtUtc = ModifiedAtUtc,
            // R35-deferred-comment-edit-timestamp-1: a genuine root-text edit now stamps a
            // distinct RootTextEditedAtUtc (via ThreadedCommentTimestamps.TouchRootTextEdit) so
            // this timestamp survives even if a later reply bumps the shared ModifiedAtUtc.
            RootTextEditedAtUtc = ModifiedAtUtc
        });

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
    public void ResolveThreadedCommentCommand_TogglesResolvedStateAndUndoRestoresThread()
    {
        var (_, sheet, ctx) = Setup();
        var addr = new CellAddress(sheet.Id, 1, 1);
        var original = new ThreadedComment("Root", "Quinn")
        {
            Replies = [new CommentReply("Reply", "Codex")]
        };
        sheet.ThreadedComments[addr] = original;

        var command = new ResolveThreadedCommentCommand(sheet.Id, addr, resolved: true, timestampUtc: ModifiedAtUtc);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.ThreadedComments[addr].Should().Be(original with
        {
            IsResolved = true,
            ModifiedAtUtc = ModifiedAtUtc
        });

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
            replyAuthor: "Reviewer",
            timestampUtc: ModifiedAtUtc);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        outcome.AffectedCells.Should().ContainSingle().Which.Should().Be(addr);
        sheet.ThreadedComments[addr].Should().BeEquivalentTo(new ThreadedComment("New root", "Quinn")
        {
            Replies =
            [
                new CommentReply("First reply", "Codex"),
                new CommentReply("Second reply", "Reviewer")
                {
                    CreatedAtUtc = ModifiedAtUtc,
                    ModifiedAtUtc = ModifiedAtUtc
                }
            ],
            IsResolved = true,
            ModifiedAtUtc = ModifiedAtUtc,
            // R35-deferred-comment-edit-timestamp-1: the root text also changed in this call, so
            // RootTextEditedAtUtc is stamped alongside ModifiedAtUtc/the appended reply.
            RootTextEditedAtUtc = ModifiedAtUtc
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
}
