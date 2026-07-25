using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R86-meta-3: band-scoped Insert Cells (Shift Right / Shift Down) never touched sheet.Sparklines at
/// all — the mirror of the R84-commands-clear-delete-5-1 fix that added
/// CaptureSparklines/RestoreSparklines/ShiftSparklinesInBand* handling to the Delete side (see
/// R84_DeleteCellsSparklinePivotGuardTests) was missing on the Insert side. A sparkline anchored at
/// A10 with DataRange B10:E10 kept plotting the stale B10:E10 after inserting a cell at C10 with
/// Shift Cells Right — silently excluding the shifted-in data and including the newly-blank cell,
/// with no error and nothing to undo it apart from reverting the whole command.
/// </summary>
public sealed class R86_InsertCellsSparklineShiftTests
{
    // ── Finding: sparkline Location/DataRange must grow/shift, not go stale ────────────────────────

    [Fact]
    public void InsertCellsShiftRight_SparklineDataRangeGrowsAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        sheet.SetCell(new CellAddress(sheet.Id, 10, 2), new NumberValue(1)); // B10
        sheet.SetCell(new CellAddress(sheet.Id, 10, 3), new NumberValue(2)); // C10
        sheet.SetCell(new CellAddress(sheet.Id, 10, 4), new NumberValue(3)); // D10
        sheet.SetCell(new CellAddress(sheet.Id, 10, 5), new NumberValue(4)); // E10

        var dataRange = new GridRange(new CellAddress(sheet.Id, 10, 2), new CellAddress(sheet.Id, 10, 5)); // B10:E10
        var location = new CellAddress(sheet.Id, 10, 1); // A10 — left of the insert point, must not move
        var sparkline = new SparklineModel { DataRange = dataRange, Location = location, Kind = SparklineKind.Line };
        sheet.Sparklines.Add(sparkline);

        // Select C10:C10 and Insert Cells > Shift Cells Right.
        var range = new GridRange(new CellAddress(sheet.Id, 10, 3), new CellAddress(sheet.Id, 10, 3));
        var command = new InsertCellsCommand(sheet.Id, range, InsertCellsShiftDirection.Right);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Sparklines.Should().ContainSingle();
        sparkline.DataRange.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 10, 2), new CellAddress(sheet.Id, 10, 6)),
            "the insert point (C10) straddles the data range, so it must grow to B10:F10 to keep including " +
            "the shifted-in D10:F10 data instead of silently plotting the stale B10:E10");
        sparkline.Location.Should().Be(location, "A10 is left of the insert point and must stay put");

        command.Revert(ctx);

        sheet.Sparklines.Should().ContainSingle();
        sparkline.DataRange.Should().Be(dataRange);
        sparkline.Location.Should().Be(location);
    }

    [Fact]
    public void InsertCellsShiftDown_SparklineDataRangeGrowsAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 5), new NumberValue(1)); // E2
        sheet.SetCell(new CellAddress(sheet.Id, 3, 5), new NumberValue(2)); // E3
        sheet.SetCell(new CellAddress(sheet.Id, 4, 5), new NumberValue(3)); // E4
        sheet.SetCell(new CellAddress(sheet.Id, 5, 5), new NumberValue(4)); // E5

        var dataRange = new GridRange(new CellAddress(sheet.Id, 2, 5), new CellAddress(sheet.Id, 5, 5)); // E2:E5
        var location = new CellAddress(sheet.Id, 1, 5); // E1 — above the insert point, must not move
        var sparkline = new SparklineModel { DataRange = dataRange, Location = location, Kind = SparklineKind.Line };
        sheet.Sparklines.Add(sparkline);

        // Select E3:E3 and Insert Cells > Shift Cells Down.
        var range = new GridRange(new CellAddress(sheet.Id, 3, 5), new CellAddress(sheet.Id, 3, 5));
        var command = new InsertCellsCommand(sheet.Id, range, InsertCellsShiftDirection.Down);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Sparklines.Should().ContainSingle();
        sparkline.DataRange.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 2, 5), new CellAddress(sheet.Id, 6, 5)),
            "the insert point (E3) straddles the data range, so it must grow to E2:E6 to keep including " +
            "the shifted-in E4:E6 data instead of silently plotting the stale E2:E5");
        sparkline.Location.Should().Be(location, "E1 is above the insert point and must stay put");

        command.Revert(ctx);

        sheet.Sparklines.Should().ContainSingle();
        sparkline.DataRange.Should().Be(dataRange);
        sparkline.Location.Should().Be(location);
    }

    // ── No-regression sibling: sparklines entirely outside the shifted band are untouched ──────────

    [Fact]
    public void InsertCellsShiftRight_SparklineOutsideRowBand_IsUnaffected()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        sheet.SetCell(new CellAddress(sheet.Id, 10, 2), new NumberValue(1));

        // Sparkline lives on row 20, entirely outside the row-10 band being shifted.
        var dataRange = new GridRange(new CellAddress(sheet.Id, 20, 2), new CellAddress(sheet.Id, 20, 5));
        var location = new CellAddress(sheet.Id, 20, 1);
        var sparkline = new SparklineModel { DataRange = dataRange, Location = location, Kind = SparklineKind.Line };
        sheet.Sparklines.Add(sparkline);

        var range = new GridRange(new CellAddress(sheet.Id, 10, 3), new CellAddress(sheet.Id, 10, 3));
        var command = new InsertCellsCommand(sheet.Id, range, InsertCellsShiftDirection.Right);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Sparklines.Should().ContainSingle();
        sparkline.DataRange.Should().Be(dataRange, "the sparkline's row (20) is outside the shifted band (row 10) and must be untouched");
        sparkline.Location.Should().Be(location);

        command.Revert(ctx);
        sparkline.DataRange.Should().Be(dataRange);
        sparkline.Location.Should().Be(location);
    }
}
