using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R16-large-workbook-perf-1/2: Insert Rows/Columns must clamp (or drop) a merged region that would
/// shift past the last row/column instead of producing an out-of-bounds merged range. Excel drops
/// the part of a shifted region that would fall off the sheet.
/// </summary>
public class R16_merge_clamp_Tests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) SetupRows()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new TestCommandContext(wb));
    }

    private static (Workbook wb, Sheet sheet, ICommandContext ctx) SetupCols()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new TestCommandContext(wb));
    }

    [Fact]
    public void InsertRows_MergeNearLastRow_ClampsInsteadOfOverflowing()
    {
        // Merge on the last two rows of the sheet, entirely below the insertion point.
        // Shifting it down by 2 rows would push Start/End past MaxRow without clamping.
        var (_, sheet, ctx) = SetupRows();
        var mergeRange = new GridRange(
            new CellAddress(sheet.Id, CellAddress.MaxRow - 1, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 2));
        sheet.AddMergedRegion(mergeRange);

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 2, count: 2);
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
    public void InsertRows_MergeEntirelyPastLastRowAfterShift_IsDropped()
    {
        // Merge on the very last row, below the insertion point. Shifting down by even 1 row
        // pushes the whole region past MaxRow, so it must be dropped entirely (not left dangling).
        var (_, sheet, ctx) = SetupRows();
        var mergeRange = new GridRange(
            new CellAddress(sheet.Id, CellAddress.MaxRow, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 2));
        sheet.AddMergedRegion(mergeRange);

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 2, count: 1);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.MergedRegions.Should().BeEmpty(
            "a merged region shifted entirely past the last row falls off the sheet and must be dropped");

        cmd.Revert(ctx);

        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(mergeRange,
            "undo restores the original merge even though it was dropped on apply");
    }

    [Fact]
    public void InsertColumns_MergeNearLastColumn_ClampsInsteadOfOverflowing()
    {
        // Merge on the last two columns of the sheet, entirely right of the insertion point.
        // Shifting it right by 2 columns would push Start/End past MaxCol without clamping.
        var (_, sheet, ctx) = SetupCols();
        var mergeRange = new GridRange(
            new CellAddress(sheet.Id, 1, CellAddress.MaxCol - 1),
            new CellAddress(sheet.Id, 2, CellAddress.MaxCol));
        sheet.AddMergedRegion(mergeRange);

        var cmd = new InsertColumnsCommand(sheet.Id, beforeCol: 2, count: 2);
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
    public void InsertColumns_MergeEntirelyPastLastColumnAfterShift_IsDropped()
    {
        // Merge on the very last column, right of the insertion point. Shifting right by even 1
        // column pushes the whole region past MaxCol, so it must be dropped entirely.
        var (_, sheet, ctx) = SetupCols();
        var mergeRange = new GridRange(
            new CellAddress(sheet.Id, 1, CellAddress.MaxCol),
            new CellAddress(sheet.Id, 2, CellAddress.MaxCol));
        sheet.AddMergedRegion(mergeRange);

        var cmd = new InsertColumnsCommand(sheet.Id, beforeCol: 2, count: 1);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.MergedRegions.Should().BeEmpty(
            "a merged region shifted entirely past the last column falls off the sheet and must be dropped");

        cmd.Revert(ctx);

        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(mergeRange,
            "undo restores the original merge even though it was dropped on apply");
    }
}
