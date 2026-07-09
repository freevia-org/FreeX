using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R17-meta-2: Insert Cells (Shift Right / Shift Down) must clamp (or drop) a merged region that
/// would shift past the last column/row instead of producing an out-of-bounds merged range, mirroring
/// the round-16 Insert Rows/Columns merge-clamp fix (see R16_merge_clamp_Tests). The Apply-time
/// "data would be pushed past the last column/row" guard is content-based (built from occupied cell
/// addresses only) and misses a merge that spans no cell values, so an un-clamped merge shift could
/// slip through and be persisted out of bounds.
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
    public void InsertCellsShiftRight_MergeNearLastColumn_ClampsInsteadOfOverflowing()
    {
        // Merge on the last two columns of row 1 (value-less, so the content-based
        // "past the last column" Apply guard never trips). Shifting it right by 2 columns
        // would push its End past MaxCol without clamping.
        var (_, sheet, ctx) = Setup();
        var mergeRange = new GridRange(
            new CellAddress(sheet.Id, 1, CellAddress.MaxCol - 2),
            new CellAddress(sheet.Id, 1, CellAddress.MaxCol - 1));
        sheet.AddMergedRegion(mergeRange);

        // Insert range spans columns 1..2 on row 1 -> width (shift count) = 2, insert-before-col = 1.
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 2));
        var cmd = new InsertCellsCommand(sheet.Id, range, InsertCellsShiftDirection.Right);

        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        foreach (var region in sheet.MergedRegions)
        {
            region.Start.Col.Should().BeLessThanOrEqualTo(CellAddress.MaxCol,
                "a shifted merged region must never start past the last column");
            region.End.Col.Should().BeLessThanOrEqualTo(CellAddress.MaxCol,
                "a shifted merged region must be clamped to the last column, not overflow it");
        }

        cmd.Revert(ctx);

        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(mergeRange,
            "undo restores the original pre-shift snapshot regardless of clamping applied on apply");
    }

    [Fact]
    public void InsertCellsShiftRight_MergeEntirelyPastLastColumnAfterShift_IsDropped()
    {
        // Merge on the very last column of row 1. Shifting right by even 1 column pushes the
        // whole region past MaxCol, so it must be dropped entirely (not left dangling out of bounds).
        var (_, sheet, ctx) = Setup();
        var mergeRange = new GridRange(
            new CellAddress(sheet.Id, 1, CellAddress.MaxCol),
            new CellAddress(sheet.Id, 2, CellAddress.MaxCol));
        sheet.AddMergedRegion(mergeRange);

        // Insert range spans rows 1..2 (covers both merge rows) at column 1 -> width = 1.
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1));
        var cmd = new InsertCellsCommand(sheet.Id, range, InsertCellsShiftDirection.Right);

        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.MergedRegions.Should().BeEmpty(
            "a merged region shifted entirely past the last column falls off the sheet and must be dropped");

        cmd.Revert(ctx);

        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(mergeRange,
            "undo restores the original merge even though it was dropped on apply");
    }

    [Fact]
    public void InsertCellsShiftDown_MergeNearLastRow_ClampsInsteadOfOverflowing()
    {
        // Merge on the last two rows of column 1 (value-less). Shifting it down by 2 rows would
        // push its End past MaxRow without clamping.
        var (_, sheet, ctx) = Setup();
        var mergeRange = new GridRange(
            new CellAddress(sheet.Id, CellAddress.MaxRow - 2, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow - 1, 1));
        sheet.AddMergedRegion(mergeRange);

        // Insert range spans rows 1..2 at column 1 -> height (shift count) = 2, insert-before-row = 1.
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1));
        var cmd = new InsertCellsCommand(sheet.Id, range, InsertCellsShiftDirection.Down);

        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        foreach (var region in sheet.MergedRegions)
        {
            region.Start.Row.Should().BeLessThanOrEqualTo(CellAddress.MaxRow,
                "a shifted merged region must never start past the last row");
            region.End.Row.Should().BeLessThanOrEqualTo(CellAddress.MaxRow,
                "a shifted merged region must be clamped to the last row, not overflow it");
        }

        cmd.Revert(ctx);

        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(mergeRange,
            "undo restores the original pre-shift snapshot regardless of clamping applied on apply");
    }

    [Fact]
    public void InsertCellsShiftDown_MergeEntirelyPastLastRowAfterShift_IsDropped()
    {
        // Merge on the very last row, spanning columns 1..2. Shifting down by even 1 row pushes
        // the whole region past MaxRow, so it must be dropped entirely.
        var (_, sheet, ctx) = Setup();
        var mergeRange = new GridRange(
            new CellAddress(sheet.Id, CellAddress.MaxRow, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 2));
        sheet.AddMergedRegion(mergeRange);

        // Insert range spans columns 1..2 at row 1 -> height = 1.
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 2));
        var cmd = new InsertCellsCommand(sheet.Id, range, InsertCellsShiftDirection.Down);

        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.MergedRegions.Should().BeEmpty(
            "a merged region shifted entirely past the last row falls off the sheet and must be dropped");

        cmd.Revert(ctx);

        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(mergeRange,
            "undo restores the original merge even though it was dropped on apply");
    }
}
