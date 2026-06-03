using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public partial class CommentCommandTests
{
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
}
