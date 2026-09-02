using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// r198 (backlog item 66): <c>RemoveDuplicateRowsCommand</c> has two paths that change nothing --
/// an unpopulated range, and a range where no row is a duplicate -- and both returned
/// <c>new CommandOutcome(true)</c> without <c>IsNoOp</c>. The bus therefore pushed an undo entry for
/// a command that did nothing, which also CLEARS REDO: running Data &gt; Remove Duplicates on a
/// selection with no duplicates silently discarded whatever the user could have redone.
/// </summary>
public sealed class R198_RemoveDuplicatesNoOpTests
{
    private static (Workbook Workbook, Sheet Sheet, TestCommandContext Ctx) Fixture()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet, new TestCommandContext(workbook));
    }

    [Fact]
    public void OnAnEmptyRange_ReportsNoOp()
    {
        var (_, sheet, ctx) = Fixture();
        var sid = sheet.Id;

        var outcome = new RemoveDuplicateRowsCommand(
                sid,
                new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 10, 3)),
                [0u])
            .Apply(ctx);

        outcome.Success.Should().BeTrue();
        outcome.IsNoOp.Should().BeTrue("nothing was populated, so nothing was removed");
    }

    [Fact]
    public void WhenNoRowIsADuplicate_ReportsNoOp()
    {
        var (_, sheet, ctx) = Fixture();
        var sid = sheet.Id;

        for (uint row = 1; row <= 3; row++)
            sheet.SetCell(new CellAddress(sid, row, 1), new TextValue($"unique-{row}"));

        var outcome = new RemoveDuplicateRowsCommand(
                sid,
                new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 3, 1)),
                [0u])
            .Apply(ctx);

        outcome.Success.Should().BeTrue();
        outcome.IsNoOp.Should().BeTrue("every row was distinct, so nothing was removed");
    }

    [Fact]
    public void WhenARowIsRemoved_DoesNotReportNoOp()
    {
        // The control: a real removal must still push its undo entry.
        var (_, sheet, ctx) = Fixture();
        var sid = sheet.Id;

        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("dup"));
        sheet.SetCell(new CellAddress(sid, 2, 1), new TextValue("dup"));

        var outcome = new RemoveDuplicateRowsCommand(
                sid,
                new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 2, 1)),
                [0u])
            .Apply(ctx);

        outcome.Success.Should().BeTrue();
        outcome.IsNoOp.Should().BeFalse();
    }
}
