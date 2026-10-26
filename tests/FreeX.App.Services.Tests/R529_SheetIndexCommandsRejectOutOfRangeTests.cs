using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.App.Services.Tests;

/// <summary>
/// r529: FreeX's sheet-index commands DO bounds-check -- MoveSheetCommand.Apply through
/// IsValidIndex, DuplicateSheetsCommand.Apply against Sheets.Count -- and nothing in the suite
/// asserted it, so both guards could have been deleted without a single test failing.
///
/// <para>The guards matter because the model underneath does not check. Workbook.MoveSheet indexes
/// _sheets directly and calls Insert, so an out-of-range index throws from the model rather than
/// being refused by the command. These tests pin the refusal, and the clean failure MESSAGE with
/// it: the point is that the command answers, not that something somewhere catches.</para>
///
/// <para>Deliberately NOT asserted here: that Revert survives a workbook that shrank between Apply
/// and Revert. MoveSheetCommand.Revert calls MoveSheet unguarded, but sheets are removed only by
/// commands sharing one LIFO undo stack, so a delete's undo always runs before an older move's --
/// the state is unreachable, and a test would have to fabricate it. See the r529 ledger entry.</para>
/// </summary>
public sealed class R529_SheetIndexCommandsRejectOutOfRangeTests
{
    private static (Workbook Workbook, WorkbookCommandContext Context) TwoSheetWorkbook()
    {
        var workbook = new Workbook("Book1");
        while (workbook.Sheets.Count < 2)
            workbook.AddSheet("Sheet" + (workbook.Sheets.Count + 1));

        return (workbook, new WorkbookCommandContext(workbook));
    }

    [Theory]
    [InlineData(9999, 0)]
    [InlineData(0, 9999)]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    public void MoveSheet_refuses_an_index_outside_the_workbook(int fromIndex, int toIndex)
    {
        var (workbook, context) = TwoSheetWorkbook();
        var before = workbook.Sheets.Select(sheet => sheet.Id).ToArray();

        var outcome = new MoveSheetCommand(fromIndex, toIndex).Apply(context);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Be("Sheet index is out of range.");
        // The refusal has to leave the order untouched: a guard that rejects after mutating would
        // satisfy an outcome-only assertion while corrupting the workbook.
        workbook.Sheets.Select(sheet => sheet.Id).Should().Equal(before);
    }

    [Fact]
    public void MoveSheet_still_moves_a_valid_pair()
    {
        // Non-vacuity: the guard must not have turned every move into a refusal.
        var (workbook, context) = TwoSheetWorkbook();
        var second = workbook.Sheets[1].Id;

        new MoveSheetCommand(1, 0).Apply(context).Success.Should().BeTrue();

        workbook.Sheets[0].Id.Should().Be(second);
    }

    [Theory]
    [InlineData(9999)]
    [InlineData(-1)]
    public void DuplicateSheets_refuses_an_insert_position_outside_the_workbook(int insertBeforeIndex)
    {
        var (workbook, context) = TwoSheetWorkbook();
        var sourceId = workbook.Sheets[0].Id;
        var before = workbook.Sheets.Count;

        var outcome = new DuplicateSheetsCommand([sourceId], insertBeforeIndex).Apply(context);

        outcome.Success.Should().BeFalse();
        workbook.Sheets.Count.Should().Be(before);
    }

    [Fact]
    public void DuplicateSheets_accepts_the_end_position()
    {
        // r526's bound: for an INSERT the legal maximum is Count, not Count - 1. A guard written
        // for reading would reject this valid append.
        var (workbook, context) = TwoSheetWorkbook();
        var sourceId = workbook.Sheets[0].Id;
        var before = workbook.Sheets.Count;

        new DuplicateSheetsCommand([sourceId], before).Apply(context).Success.Should().BeTrue();

        workbook.Sheets.Count.Should().Be(before + 1);
    }
}
