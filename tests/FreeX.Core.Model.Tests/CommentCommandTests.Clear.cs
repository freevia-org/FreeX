using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public partial class CommentCommandTests
{
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
}
