using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

// R111-commands-freeze-split-shift-2: Insert/Delete Rows and Columns never shifted Freeze Panes
// (Sheet.FrozenRows/FrozenCols) or Split Panes (Sheet.SplitRow/SplitColumn), even though every other
// row/column-bearing piece of sheet state (print titles, merges, named ranges, etc.) is re-anchored
// by RowColumnShiftHelpers.ShiftAddressBearingState. Concretely: freeze the header row (FrozenRows =
// 1), then Insert Row above it -- Excel keeps the header pinned (grows the freeze band to 2), but
// FreeX left FrozenRows at 1 so the real header scrolled out of the frozen band.
public sealed class R111_FreezeSplitPanesShiftTests
{
    [Fact]
    public void R111_InsertRowAboveFrozenHeader_GrowsFrozenRowsToKeepHeaderPinned()
    {
        var (_, sheet, ctx) = Setup();
        sheet.FrozenRows = 1; // View > Freeze Top Row: row 1 (the header) is frozen.

        var command = new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 1);
        command.Apply(ctx).Success.Should().BeTrue();

        // The header, now on row 2, must still be inside the frozen band.
        sheet.FrozenRows.Should().Be(2);

        command.Revert(ctx);
        sheet.FrozenRows.Should().Be(1);
    }

    [Fact]
    public void R111_InsertRowInsideFrozenBand_GrowsFrozenRowsByInsertCount()
    {
        var (_, sheet, ctx) = Setup();
        sheet.FrozenRows = 5;

        var command = new InsertRowsCommand(sheet.Id, beforeRow: 3, count: 2);
        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FrozenRows.Should().Be(7);

        command.Revert(ctx);
        sheet.FrozenRows.Should().Be(5);
    }

    [Fact]
    public void R111_InsertRowBelowFrozenBand_LeavesFrozenRowsUnchanged()
    {
        var (_, sheet, ctx) = Setup();
        sheet.FrozenRows = 3;

        // Inserting strictly below the frozen band (a new scrollable row) must not grow the freeze.
        var command = new InsertRowsCommand(sheet.Id, beforeRow: 4, count: 5);
        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FrozenRows.Should().Be(3);

        command.Revert(ctx);
        sheet.FrozenRows.Should().Be(3);
    }

    [Fact]
    public void R111_DeleteRowInsideFrozenBand_ShrinksFrozenRows()
    {
        var (_, sheet, ctx) = Setup();
        sheet.FrozenRows = 5;

        var command = new DeleteRowsCommand(sheet.Id, startRow: 2, count: 2);
        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FrozenRows.Should().Be(3);

        command.Revert(ctx);
        sheet.FrozenRows.Should().Be(5);
    }

    [Fact]
    public void R111_DeleteRowsSpanningFrozenBandBoundary_ClampsFrozenRowsToSurvivingPrefix()
    {
        var (_, sheet, ctx) = Setup();
        sheet.FrozenRows = 3;

        // Deletes rows 2-5, which removes rows 2-3 of the frozen band and 2 rows below it.
        // Only row 1 of the original band survives, so FrozenRows must clamp to 1.
        var command = new DeleteRowsCommand(sheet.Id, startRow: 2, count: 4);
        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FrozenRows.Should().Be(1);

        command.Revert(ctx);
        sheet.FrozenRows.Should().Be(3);
    }

    [Fact]
    public void R111_DeleteRowsAtTopOfFrozenBand_ClampsFrozenRowsToZero()
    {
        var (_, sheet, ctx) = Setup();
        sheet.FrozenRows = 2;

        var command = new DeleteRowsCommand(sheet.Id, startRow: 1, count: 2);
        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FrozenRows.Should().Be(0);

        command.Revert(ctx);
        sheet.FrozenRows.Should().Be(2);
    }

    [Fact]
    public void R111_InsertColumnInsideFrozenColumnBand_GrowsFrozenCols()
    {
        var (_, sheet, ctx) = Setup();
        sheet.FrozenCols = 2; // Freeze First Column-family (columns A-B frozen).

        var command = new InsertColumnsCommand(sheet.Id, beforeCol: 1, count: 1);
        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FrozenCols.Should().Be(3);

        command.Revert(ctx);
        sheet.FrozenCols.Should().Be(2);
    }

    [Fact]
    public void R111_DeleteColumnInsideFrozenColumnBand_ShrinksFrozenCols()
    {
        var (_, sheet, ctx) = Setup();
        sheet.FrozenCols = 4;

        var command = new DeleteColumnsCommand(sheet.Id, startCol: 2, count: 1);
        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FrozenCols.Should().Be(3);

        command.Revert(ctx);
        sheet.FrozenCols.Should().Be(4);
    }

    [Fact]
    public void R111_InsertColumnBelowFrozenColumnBand_LeavesFrozenColsUnchanged()
    {
        var (_, sheet, ctx) = Setup();
        sheet.FrozenCols = 2;

        var command = new InsertColumnsCommand(sheet.Id, beforeCol: 3, count: 1);
        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FrozenCols.Should().Be(2);

        command.Revert(ctx);
        sheet.FrozenCols.Should().Be(2);
    }

    // Sibling: Split Panes (the non-frozen "split view" case, Sheet.SplitRow/SplitColumn) must move
    // in lockstep with an insert/delete the same way Freeze Panes does, rather than staying fixed at
    // its stale pre-shift row/column index.
    [Fact]
    public void R111_InsertRowAboveSplitRow_ShiftsSplitRowDownByInsertCount()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SplitRow = 5;
        sheet.SplitColumn = 3;

        var command = new InsertRowsCommand(sheet.Id, beforeRow: 2, count: 2);
        command.Apply(ctx).Success.Should().BeTrue();

        sheet.SplitRow.Should().Be(7);
        sheet.SplitColumn.Should().Be(3); // unaffected by a row-axis shift

        command.Revert(ctx);
        sheet.SplitRow.Should().Be(5);
    }

    [Fact]
    public void R111_InsertColumnAboveSplitColumn_ShiftsSplitColumnRightByInsertCount()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SplitRow = 5;
        sheet.SplitColumn = 3;

        var command = new InsertColumnsCommand(sheet.Id, beforeCol: 2, count: 1);
        command.Apply(ctx).Success.Should().BeTrue();

        sheet.SplitColumn.Should().Be(4);
        sheet.SplitRow.Should().Be(5); // unaffected by a column-axis shift

        command.Revert(ctx);
        sheet.SplitColumn.Should().Be(3);
    }

    // No-regression sibling: the untouched neighbouring feature (Print Titles) that this fix
    // deliberately mirrors must keep shifting exactly as before, alongside the freeze band, in the
    // very same command application.
    [Fact]
    public void R111_InsertRowAboveFrozenHeaderAndPrintTitleRow_BothGrowInLockstep()
    {
        var (_, sheet, ctx) = Setup();
        sheet.FrozenRows = 1;
        sheet.PrintTitleRows = new WorksheetRepeatRange(1, 1);

        var command = new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 1);
        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FrozenRows.Should().Be(2);
        // Print Titles is a reference to the specific named row(s) (not a top-anchored band like
        // Freeze Panes), so a row inserted exactly at its Start relocates the whole reference down
        // by the insert count -- this is pre-existing ShiftPrintTitles behaviour, unchanged by this
        // fix, asserted here only to prove the two features shift correctly side by side.
        sheet.PrintTitleRows.Should().Be(new WorksheetRepeatRange(2, 2));

        command.Revert(ctx);
        sheet.FrozenRows.Should().Be(1);
        sheet.PrintTitleRows.Should().Be(new WorksheetRepeatRange(1, 1));
    }

    private static (Workbook Workbook, Sheet Sheet, ICommandContext Context) Setup()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet, new TestCommandContext(workbook));
    }
}
