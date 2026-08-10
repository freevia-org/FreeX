using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R133-cmd-rowcol-sparkline-dateaxis: whole-row/whole-column Insert/Delete shift a sparkline's
/// DataRange (see RowColumnShiftHelpers.AddressState.ShiftSparklines) but, before this fix, never
/// touched the group's optional DateAxisRange (Excel's "Date Axis Type" sparkline group setting),
/// so the date axis desynced from the shifted data and the sparkline plotted against the wrong
/// dates. Mirrors the band-scoped Insert/Delete Cells coverage already in
/// tests/FreeX.Core.Model.Tests/R84_DeleteCellsSparklinePivotGuardTests.cs and
/// R86_InsertCellsSparklineShiftTests.cs, but for the whole-row/whole-column commands.
/// </summary>
public sealed class R133_RowColumnShiftSparklineDateAxisRangeTests
{
    [Fact]
    public void InsertRowsAbove_ShiftsDateAxisRangeWithData()
    {
        var (workbook, sheet, ctx) = Setup();
        var sparkline = new SparklineModel
        {
            Location = Addr(sheet, 1, 5),
            DataRange = Range(sheet, 6, 1, 8, 1),
            DateAxisRange = Range(sheet, 6, 2, 8, 2)
        };
        sheet.Sparklines.Add(sparkline);

        var command = new InsertRowsCommand(sheet.Id, beforeRow: 4, count: 2);
        command.Apply(ctx).Success.Should().BeTrue();

        var shifted = sheet.Sparklines.Should().ContainSingle().Subject;
        shifted.DataRange.Should().Be(Range(sheet, 8, 1, 10, 1));
        shifted.DateAxisRange.Should().Be(Range(sheet, 8, 2, 10, 2));

        command.Revert(ctx);

        var reverted = sheet.Sparklines.Should().ContainSingle().Subject;
        reverted.DataRange.Should().Be(Range(sheet, 6, 1, 8, 1));
        reverted.DateAxisRange.Should().Be(Range(sheet, 6, 2, 8, 2));
    }

    [Fact]
    public void DeleteRowsThroughRange_ShrinksDateAxisRangeInStepWithData()
    {
        var (workbook, sheet, ctx) = Setup();
        var sparkline = new SparklineModel
        {
            Location = Addr(sheet, 1, 5),
            DataRange = Range(sheet, 6, 1, 9, 1),
            DateAxisRange = Range(sheet, 6, 2, 9, 2)
        };
        sheet.Sparklines.Add(sparkline);

        // Deletes rows 7-8, a partial overlap through the middle of both ranges: each should
        // shrink from 4 rows down to 2 (6-7), tracking each other exactly.
        var command = new DeleteRowsCommand(sheet.Id, startRow: 7, count: 2);
        command.Apply(ctx).Success.Should().BeTrue();

        var shifted = sheet.Sparklines.Should().ContainSingle().Subject;
        shifted.DataRange.Should().Be(Range(sheet, 6, 1, 7, 1));
        shifted.DateAxisRange.Should().Be(Range(sheet, 6, 2, 7, 2));

        command.Revert(ctx);

        var reverted = sheet.Sparklines.Should().ContainSingle().Subject;
        reverted.DataRange.Should().Be(Range(sheet, 6, 1, 9, 1));
        reverted.DateAxisRange.Should().Be(Range(sheet, 6, 2, 9, 2));
    }

    [Fact]
    public void DeleteRowsFullyCoveringDateAxisRange_ClearsDateAxisRangeButKeepsSparklineAlive()
    {
        var (workbook, sheet, ctx) = Setup();
        // DataRange spans rows 5-15 (wide); DateAxisRange is the narrower subset rows 6-8, fully
        // inside the deleted band. Deleting rows 6-8 must fully delete (REF!-equivalent) the
        // DateAxisRange -- cleared to null, exactly like InsertCellsCommand.ShiftSparklinesInBandLeft/
        // Right already do for the band-scoped path -- while the sparkline itself survives because
        // its DataRange is only partially covered, not fully deleted.
        var sparkline = new SparklineModel
        {
            Location = Addr(sheet, 1, 5),
            DataRange = Range(sheet, 5, 1, 15, 1),
            DateAxisRange = Range(sheet, 6, 2, 8, 2)
        };
        sheet.Sparklines.Add(sparkline);

        var command = new DeleteRowsCommand(sheet.Id, startRow: 6, count: 3);
        command.Apply(ctx).Success.Should().BeTrue();

        var shifted = sheet.Sparklines.Should().ContainSingle().Subject;
        shifted.DataRange.Should().Be(Range(sheet, 5, 1, 12, 1));
        shifted.DateAxisRange.Should().BeNull();

        command.Revert(ctx);

        var reverted = sheet.Sparklines.Should().ContainSingle().Subject;
        reverted.DataRange.Should().Be(Range(sheet, 5, 1, 15, 1));
        reverted.DateAxisRange.Should().Be(Range(sheet, 6, 2, 8, 2));
    }

    /// <summary>
    /// Sibling no-regression: a sparkline with no date axis (the common case -- DateAxisRange is
    /// null) must keep shifting its DataRange exactly as before and must not throw or acquire a
    /// spurious DateAxisRange from the fix above.
    /// </summary>
    [Fact]
    public void InsertRowsAbove_WithoutDateAxisRange_LeavesItNullAndStillShiftsDataRange()
    {
        var (workbook, sheet, ctx) = Setup();
        var sparkline = new SparklineModel
        {
            Location = Addr(sheet, 1, 5),
            DataRange = Range(sheet, 6, 1, 8, 1)
        };
        sheet.Sparklines.Add(sparkline);
        sparkline.DateAxisRange.Should().BeNull();

        var command = new InsertRowsCommand(sheet.Id, beforeRow: 4, count: 2);
        command.Apply(ctx).Success.Should().BeTrue();

        var shifted = sheet.Sparklines.Should().ContainSingle().Subject;
        shifted.DataRange.Should().Be(Range(sheet, 8, 1, 10, 1));
        shifted.DateAxisRange.Should().BeNull();
    }

    private static (Workbook Workbook, Sheet Sheet, ICommandContext Context) Setup()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet, new TestCommandContext(workbook));
    }

    private static CellAddress Addr(Sheet sheet, uint row, uint col) => new(sheet.Id, row, col);

    private static GridRange Range(Sheet sheet, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(new CellAddress(sheet.Id, startRow, startCol), new CellAddress(sheet.Id, endRow, endCol));
}
