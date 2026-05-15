using Freexcel.Core.Commands;
using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.Core.Model.Tests;

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
    public void ClearCommentsCommand_RejectsProtectedSheet()
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

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
