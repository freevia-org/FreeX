using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// chart-binding F1: InsertCellsCommand/DeleteCellsCommand (the band-scoped "Insert Cells... /
/// Delete Cells..." dialog commands, distinct from whole-row/whole-column insert/delete) never
/// touched ChartModel.DataRange at all before this fix -- a chart plotting data inside the shifted
/// band silently kept pointing at its old, now-wrong coordinate window after the shift, while every
/// real cell value/formula in the same band correctly relocated. Mirrors the equivalent coverage
/// InsertDeleteRowsCommand/InsertDeleteColumnsCommand already have via
/// RowColumnShiftHelpers.ShiftChartRowsUp/Down/ColumnsUp/Down, but band-scoped (only a chart whose
/// DataRange lies fully inside the shifted band's row/column span is touched -- mirroring how
/// ShiftNamedRangesInBandRight/Down already treat named ranges for this same command).
/// </summary>
public sealed class ChartBindingF1_InsertDeleteCellsChartDataRangeTests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new TestCommandContext(wb));
    }

    private static GridRange Range(SheetId id, uint r1, uint c1, uint r2, uint c2) =>
        new(new CellAddress(id, r1, c1), new CellAddress(id, r2, c2));

    // ══════════════════════════════════════════════════════════════════════════
    // Reproduces the finding's probe scenarios.
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void InsertCells_ShiftDown_ChartDataRangeInBand_ShiftsDownWithTheData()
    {
        // Chart plots A1:A5 (values 10,20,30,40,50). Insert-shift-down at A1:A1 pushes every real
        // cell value down one row (A1 blank, old A1..A5 now at A2..A6) -- the chart's DataRange must
        // follow to A2:A6, not stay frozen at A1:A5 (which would now plot the inserted blank cell
        // plus only 4 of the original 5 values).
        var (_, sheet, ctx) = Setup();
        var chart = new ChartModel { DataRange = Range(sheet.Id, 1, 1, 5, 1) }; // A1:A5
        sheet.Charts.Add(chart);

        var cmd = new InsertCellsCommand(sheet.Id, Range(sheet.Id, 1, 1, 1, 1), InsertCellsShiftDirection.Down);
        cmd.Apply(ctx).Success.Should().BeTrue();

        chart.DataRange.Should().Be(Range(sheet.Id, 2, 1, 6, 1),
            because: "A1:A5 shifts down 1 row to A2:A6, following the real data it plots");
    }

    [Fact]
    public void DeleteCells_ShiftUp_ChartDataRangeInBand_ShrinksToSurvivingPortion()
    {
        // Chart plots B2:B6 (values 100..500). Delete-shift-up at B2:B2 removes B2 and pulls
        // B3..B6 up to B2..B5 -- the chart's DataRange must shrink to B2:B5, not stay frozen at
        // B2:B6 (which would plot one row past the real data, a phantom blank point).
        var (_, sheet, ctx) = Setup();
        var chart = new ChartModel { DataRange = Range(sheet.Id, 2, 2, 6, 2) }; // B2:B6
        sheet.Charts.Add(chart);

        var cmd = new DeleteCellsCommand(sheet.Id, Range(sheet.Id, 2, 2, 2, 2), DeleteCellsShiftDirection.Up);
        cmd.Apply(ctx).Success.Should().BeTrue();

        chart.DataRange.Should().Be(Range(sheet.Id, 2, 2, 5, 2),
            because: "B2 is deleted and B3:B6 shifts up to B2:B5, so the chart must track the surviving data");
    }

    [Fact]
    public void InsertCells_ShiftRight_ChartDataRangeInBand_ShiftsRightWithTheData()
    {
        // Right-direction sibling of the Shift-Down probe above.
        var (_, sheet, ctx) = Setup();
        var chart = new ChartModel { DataRange = Range(sheet.Id, 1, 1, 1, 5) }; // A1:E1
        sheet.Charts.Add(chart);

        var cmd = new InsertCellsCommand(sheet.Id, Range(sheet.Id, 1, 1, 1, 1), InsertCellsShiftDirection.Right);
        cmd.Apply(ctx).Success.Should().BeTrue();

        chart.DataRange.Should().Be(Range(sheet.Id, 1, 2, 1, 6),
            because: "A1:E1 shifts right 1 column to B1:F1, following the real data it plots");
    }

    [Fact]
    public void DeleteCells_ShiftLeft_ChartDataRangeInBand_ShrinksToSurvivingPortion()
    {
        // Left-direction sibling of the Shift-Up probe above.
        var (_, sheet, ctx) = Setup();
        var chart = new ChartModel { DataRange = Range(sheet.Id, 2, 2, 2, 6) }; // B2:F2
        sheet.Charts.Add(chart);

        var cmd = new DeleteCellsCommand(sheet.Id, Range(sheet.Id, 2, 2, 2, 2), DeleteCellsShiftDirection.Left);
        cmd.Apply(ctx).Success.Should().BeTrue();

        chart.DataRange.Should().Be(Range(sheet.Id, 2, 2, 2, 5),
            because: "B2 is deleted and C2:F2 shifts left to B2:E2, so the chart must track the surviving data");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Undo coverage.
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void InsertCells_ShiftDown_ChartDataRange_Undo_RestoresOriginalRange()
    {
        var (_, sheet, ctx) = Setup();
        var chart = new ChartModel { DataRange = Range(sheet.Id, 1, 1, 5, 1) }; // A1:A5
        sheet.Charts.Add(chart);

        var cmd = new InsertCellsCommand(sheet.Id, Range(sheet.Id, 1, 1, 1, 1), InsertCellsShiftDirection.Down);
        cmd.Apply(ctx).Success.Should().BeTrue();
        cmd.Revert(ctx);

        chart.DataRange.Should().Be(Range(sheet.Id, 1, 1, 5, 1),
            because: "undo restores the original pre-insert DataRange");
    }

    [Fact]
    public void DeleteCells_ShiftUp_ChartDataRange_Undo_RestoresOriginalRange()
    {
        var (_, sheet, ctx) = Setup();
        var chart = new ChartModel { DataRange = Range(sheet.Id, 2, 2, 6, 2) }; // B2:B6
        sheet.Charts.Add(chart);

        var cmd = new DeleteCellsCommand(sheet.Id, Range(sheet.Id, 2, 2, 2, 2), DeleteCellsShiftDirection.Up);
        cmd.Apply(ctx).Success.Should().BeTrue();
        cmd.Revert(ctx);

        chart.DataRange.Should().Be(Range(sheet.Id, 2, 2, 6, 2),
            because: "undo restores the original pre-delete DataRange");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Sibling no-regression: band-scoping must not over-shift a chart outside the touched band.
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void InsertCells_ShiftDown_ChartDataRangeInDifferentColumn_IsUnaffected()
    {
        // Insert-shift-down only affects column A (the requested range's own column). A chart
        // plotting column C is untouched by this band-scoped op, even though its rows overlap --
        // unlike a whole-row insert (which would shift every column, including C). This is the
        // adjacent case the fix must not break: band-scoping (checked against the request's own
        // column span, matching ShiftNamedRangesInBandDown's existing containment check) must stay
        // narrow, not degrade into an unconditional whole-row-style shift.
        var (_, sheet, ctx) = Setup();
        var chart = new ChartModel { DataRange = Range(sheet.Id, 1, 3, 5, 3) }; // C1:C5
        sheet.Charts.Add(chart);

        var cmd = new InsertCellsCommand(sheet.Id, Range(sheet.Id, 1, 1, 1, 1), InsertCellsShiftDirection.Down);
        cmd.Apply(ctx).Success.Should().BeTrue();

        chart.DataRange.Should().Be(Range(sheet.Id, 1, 3, 5, 3),
            because: "column C is outside the shifted A-only band, so this chart's DataRange must stay exactly as it was");
    }

    [Fact]
    public void DeleteCells_ShiftUp_ChartDataRangeInDifferentColumn_IsUnaffected()
    {
        var (_, sheet, ctx) = Setup();
        var chart = new ChartModel { DataRange = Range(sheet.Id, 2, 4, 6, 4) }; // D2:D6
        sheet.Charts.Add(chart);

        var cmd = new DeleteCellsCommand(sheet.Id, Range(sheet.Id, 2, 2, 2, 2), DeleteCellsShiftDirection.Up);
        cmd.Apply(ctx).Success.Should().BeTrue();

        chart.DataRange.Should().Be(Range(sheet.Id, 2, 4, 6, 4),
            because: "column D is outside the deleted B-only band, so this chart's DataRange must stay exactly as it was");
    }

    [Fact]
    public void InsertCells_ShiftDown_ChartDataRangeAboveInsertPoint_IsUnaffected()
    {
        // Sibling no-regression: a chart entirely above the insert point (within the same column
        // band) must not move -- only ranges at/after the insert point shift.
        var (_, sheet, ctx) = Setup();
        var chart = new ChartModel { DataRange = Range(sheet.Id, 1, 1, 3, 1) }; // A1:A3
        sheet.Charts.Add(chart);

        var cmd = new InsertCellsCommand(sheet.Id, Range(sheet.Id, 10, 1, 10, 1), InsertCellsShiftDirection.Down);
        cmd.Apply(ctx).Success.Should().BeTrue();

        chart.DataRange.Should().Be(Range(sheet.Id, 1, 1, 3, 1),
            because: "A1:A3 is entirely above row 10, the insert point, so it is unaffected");
    }
}
