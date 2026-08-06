using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R124: real Excel never lets a single cell carry both a legacy Note and a threaded Comment at
/// the same time -- FreeX's own XLSX writer relies on that invariant (XlsxFileAdapter.Save.cs's
/// Comments-vs-ThreadedComments loops) and ConvertNotesToCommentsCommand already skips a cell
/// that already has a threaded comment for exactly this reason. Before this fix, the two
/// direct-authoring commands (SetCommentCommand / SetThreadedCommentCommand) never checked the
/// sibling dictionary before writing, so a user could populate sheet.Comments[addr] AND
/// sheet.ThreadedComments[addr] for the same address, producing a saved file shape real Excel
/// never writes (see the finding for the full save-side consequence).
/// </summary>
public class R124_CommentNoteThreadedMutualExclusionTests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new TestCommandContext(wb));
    }

    [Fact]
    public void SetCommentCommand_RejectsWhenCellAlreadyHasThreadedComment()
    {
        var (_, sheet, ctx) = Setup();
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.ThreadedComments[addr] = new ThreadedComment("Existing thread", "Quinn");

        var outcome = new SetCommentCommand(sheet.Id, addr, "New note").Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("threaded comment");

        // The invariant that matters: the cell must never end up with both.
        sheet.Comments.Should().NotContainKey(addr);
        sheet.ThreadedComments.Should().ContainKey(addr);
    }

    [Fact]
    public void SetThreadedCommentCommand_RejectsWhenCellAlreadyHasNote()
    {
        var (_, sheet, ctx) = Setup();
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.Comments[addr] = "Existing note";

        var outcome = new SetThreadedCommentCommand(sheet.Id, addr, "New thread").Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("Note");

        sheet.ThreadedComments.Should().NotContainKey(addr);
        sheet.Comments.Should().ContainKey(addr);
    }

    // No-regression sibling coverage: the guard must not block the ordinary, valid operations
    // that share the same code path -- creating the FIRST annotation on a bare cell, and editing
    // an EXISTING annotation of the same kind.

    [Fact]
    public void SetCommentCommand_StillAllowsNoteOnBareCell()
    {
        var (_, sheet, ctx) = Setup();
        var addr = new CellAddress(sheet.Id, 1, 1);

        var outcome = new SetCommentCommand(sheet.Id, addr, "Fresh note").Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.Comments[addr].Should().Be("Fresh note");
    }

    [Fact]
    public void SetCommentCommand_StillAllowsEditingExistingNoteText()
    {
        var (_, sheet, ctx) = Setup();
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.Comments[addr] = "Old text";

        var outcome = new SetCommentCommand(sheet.Id, addr, "New text").Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.Comments[addr].Should().Be("New text");
    }

    [Fact]
    public void SetThreadedCommentCommand_StillAllowsThreadOnBareCell()
    {
        var (_, sheet, ctx) = Setup();
        var addr = new CellAddress(sheet.Id, 1, 1);

        var outcome = new SetThreadedCommentCommand(sheet.Id, addr, "Fresh thread").Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.ThreadedComments[addr].Text.Should().Be("Fresh thread");
    }

    [Fact]
    public void SetThreadedCommentCommand_StillAllowsReplacingExistingThread()
    {
        var (_, sheet, ctx) = Setup();
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.ThreadedComments[addr] = new ThreadedComment("Old thread", "Quinn");

        var outcome = new SetThreadedCommentCommand(sheet.Id, addr, "New thread").Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.ThreadedComments[addr].Text.Should().Be("New thread");
    }
}
