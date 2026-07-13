using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R17-meta-2 (superseded by R38-commands-insert-delete-shift-2-2): Insert Cells (Shift Right /
/// Shift Down) must never silently clamp/drop a merged region that would shift past the last
/// column/row. R17 originally clamped/dropped such value-less merges because the content-based
/// "past the last column/row" Apply guard (built from occupied cell addresses only) never tripped
/// for a merge spanning no cell values. R38-commands-insert-delete-shift-2-2 found that this
/// silent-clamp/drop behavior itself diverges from Excel, which instead blocks the whole insert
/// with a "data would be pushed past the last column/row" rejection — matching how it already
/// blocks the operation for value-bearing cells — so the Apply-time guard now also consults
/// MergedRegions and rejects instead of clamping/dropping.
/// </summary>
public class R17_insert_cells_clamp_Tests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new TestCommandContext(wb));
    }

    [Fact]
    public void InsertCellsShiftRight_MergeNearLastColumn_BlocksInsteadOfClamping()
    {
        // Merge on the last two columns of row 1 (value-less). Shifting it right by 2 columns
        // would push its End past MaxCol, so the insert must be rejected outright instead of
        // clamping the merge to the last column.
        var (_, sheet, ctx) = Setup();
        var mergeRange = new GridRange(
            new CellAddress(sheet.Id, 1, CellAddress.MaxCol - 2),
            new CellAddress(sheet.Id, 1, CellAddress.MaxCol - 1));
        sheet.AddMergedRegion(mergeRange);

        // Insert range spans columns 1..2 on row 1 -> width (shift count) = 2, insert-before-col = 1.
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 2));
        var cmd = new InsertCellsCommand(sheet.Id, range, InsertCellsShiftDirection.Right);

        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeFalse("shifting the merge past the last column must be blocked, not clamped");
        outcome.ErrorMessage.Should().Contain("last column");
        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(mergeRange,
            "a rejected insert must leave the merge completely untouched");

        cmd.Revert(ctx);

        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(mergeRange,
            "reverting a no-op (rejected) apply must be harmless");
    }

    [Fact]
    public void InsertCellsShiftRight_MergeEntirelyPastLastColumnAfterShift_BlocksInsteadOfDropping()
    {
        // Merge on the very last column of row 1. Shifting right by even 1 column pushes the
        // whole region past MaxCol, so the insert must be rejected instead of silently dropping
        // the merge.
        var (_, sheet, ctx) = Setup();
        var mergeRange = new GridRange(
            new CellAddress(sheet.Id, 1, CellAddress.MaxCol),
            new CellAddress(sheet.Id, 2, CellAddress.MaxCol));
        sheet.AddMergedRegion(mergeRange);

        // Insert range spans rows 1..2 (covers both merge rows) at column 1 -> width = 1.
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1));
        var cmd = new InsertCellsCommand(sheet.Id, range, InsertCellsShiftDirection.Right);

        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeFalse("shifting the merge entirely past the last column must be blocked, not dropped");
        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(mergeRange,
            "a rejected insert must leave the merge completely untouched");

        cmd.Revert(ctx);

        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(mergeRange,
            "reverting a no-op (rejected) apply must be harmless");
    }

    [Fact]
    public void InsertCellsShiftDown_MergeNearLastRow_BlocksInsteadOfClamping()
    {
        // Merge on the last two rows of column 1 (value-less). Shifting it down by 2 rows would
        // push its End past MaxRow, so the insert must be rejected instead of clamping.
        var (_, sheet, ctx) = Setup();
        var mergeRange = new GridRange(
            new CellAddress(sheet.Id, CellAddress.MaxRow - 2, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow - 1, 1));
        sheet.AddMergedRegion(mergeRange);

        // Insert range spans rows 1..2 at column 1 -> height (shift count) = 2, insert-before-row = 1.
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1));
        var cmd = new InsertCellsCommand(sheet.Id, range, InsertCellsShiftDirection.Down);

        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeFalse("shifting the merge past the last row must be blocked, not clamped");
        outcome.ErrorMessage.Should().Contain("last row");
        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(mergeRange,
            "a rejected insert must leave the merge completely untouched");

        cmd.Revert(ctx);

        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(mergeRange,
            "reverting a no-op (rejected) apply must be harmless");
    }

    [Fact]
    public void InsertCellsShiftDown_MergeEntirelyPastLastRowAfterShift_BlocksInsteadOfDropping()
    {
        // Merge on the very last row, spanning columns 1..2. Shifting down by even 1 row pushes
        // the whole region past MaxRow, so the insert must be rejected instead of silently
        // dropping the merge.
        var (_, sheet, ctx) = Setup();
        var mergeRange = new GridRange(
            new CellAddress(sheet.Id, CellAddress.MaxRow, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 2));
        sheet.AddMergedRegion(mergeRange);

        // Insert range spans columns 1..2 at row 1 -> height = 1.
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 2));
        var cmd = new InsertCellsCommand(sheet.Id, range, InsertCellsShiftDirection.Down);

        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeFalse("shifting the merge entirely past the last row must be blocked, not dropped");
        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(mergeRange,
            "a rejected insert must leave the merge completely untouched");

        cmd.Revert(ctx);

        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(mergeRange,
            "reverting a no-op (rejected) apply must be harmless");
    }
}
