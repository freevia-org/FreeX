using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R84-commands-clear-delete-5-1/-2: band-scoped Delete Cells (Shift Up / Shift Left) never touched
/// sheet.Sparklines or sheet.PivotTables at all, unlike whole-row/whole-column delete (which routes
/// every address-bearing structure through RowColumnShiftHelpers.AddressState.cs's
/// CaptureSparklines/ShiftSparklines and CapturePivotTables/ShiftPivotTables) and unlike the
/// AutoFilter/StructuredTable guard right next to it (AutoFilterOverlapsBand). A sparkline's
/// Location/DataRange went silently stale (plotting a shifted-in range the user never saw), and a
/// PivotTable's SourceRange was silently shifted out from under it instead of the operation being
/// refused the way Excel refuses it for an AutoFilter/table.
/// </summary>
public sealed class R84_DeleteCellsSparklinePivotGuardTests
{
    // ── Finding 1: sparkline Location/DataRange must move/shrink, not strand ──────────────────────

    [Fact]
    public void DeleteCellsShiftUp_SparklineDataRangeShrinksAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(3));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new NumberValue(4));

        var dataRange = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 5, 1)); // A2:A5
        var location = new CellAddress(sheet.Id, 5, 2); // B5 — outside the col-A band, must not move
        var sparkline = new SparklineModel { DataRange = dataRange, Location = location, Kind = SparklineKind.Line };
        sheet.Sparklines.Add(sparkline);

        // Select A2:A2 and Delete Cells > Shift Cells Up.
        var range = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, 1));
        var command = new DeleteCellsCommand(sheet.Id, range, DeleteCellsShiftDirection.Up);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Sparklines.Should().ContainSingle();
        sparkline.DataRange.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 4, 1)),
            "the surviving A3:A5 data shifted up into A2:A4, so the sparkline must plot A2:A4, not the stale A2:A5");
        sparkline.Location.Should().Be(location, "B5 is outside the shifted column A band and must stay put");

        command.Revert(ctx);

        sheet.Sparklines.Should().ContainSingle();
        sparkline.DataRange.Should().Be(dataRange);
        sparkline.Location.Should().Be(location);
    }

    [Fact]
    public void DeleteCellsShiftLeft_SparklineDataRangeShrinksAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 3), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 4), new NumberValue(3));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 5), new NumberValue(4));

        var dataRange = new GridRange(new CellAddress(sheet.Id, 5, 2), new CellAddress(sheet.Id, 5, 5)); // B5:E5
        var location = new CellAddress(sheet.Id, 5, 6); // F5, right of the deleted band — must shift left
        var sparkline = new SparklineModel { DataRange = dataRange, Location = location, Kind = SparklineKind.Line };
        sheet.Sparklines.Add(sparkline);

        // Select B5:B5 and Delete Cells > Shift Cells Left.
        var range = new GridRange(new CellAddress(sheet.Id, 5, 2), new CellAddress(sheet.Id, 5, 2));
        var command = new DeleteCellsCommand(sheet.Id, range, DeleteCellsShiftDirection.Left);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Sparklines.Should().ContainSingle();
        sparkline.DataRange.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 5, 2), new CellAddress(sheet.Id, 5, 4)),
            "the surviving C5:E5 data shifted left into B5:D5, so the sparkline must plot B5:D5, not the stale B5:E5");
        sparkline.Location.Should().Be(new CellAddress(sheet.Id, 5, 5), "F5 shifted left by one column to E5");

        command.Revert(ctx);

        sheet.Sparklines.Should().ContainSingle();
        sparkline.DataRange.Should().Be(dataRange);
        sparkline.Location.Should().Be(location);
    }

    // ── Finding 2: PivotTable SourceRange overlap must reject the shift, like AutoFilter/tables ──

    [Fact]
    public void DeleteCellsShiftLeft_RejectsWhenPivotTableSourceRangeOverlapsBand()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        for (uint row = 1; row <= 20; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row));

        sheet.PivotTables.Add(new PivotTableModel
        {
            Name = "PivotTable1",
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 20, 3)), // A1:C20
            TargetRange = new GridRange(new CellAddress(sheet.Id, 1, 10), new CellAddress(sheet.Id, 5, 12)),
        });

        // Select B1:B1 (inside the pivot's source columns) and Delete Cells > Shift Cells Left.
        var range = new GridRange(new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, 1, 2));
        var outcome = new DeleteCellsCommand(sheet.Id, range, DeleteCellsShiftDirection.Left).Apply(ctx);

        outcome.Success.Should().BeFalse("Excel refuses to shift cells that would disrupt a PivotTable's source layout");
        sheet.PivotTables.Single().SourceRange.Should().Be(
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 20, 3)),
            "a rejected operation must leave the PivotTable's SourceRange untouched");
        sheet.GetValue(1, 2).Should().Be(new NumberValue(1), "a rejected operation must leave cell data untouched");
    }

    [Fact]
    public void DeleteCellsShiftUp_RejectsWhenPivotTableSourceRangeOverlapsBand()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        for (uint col = 1; col <= 3; col++)
            sheet.SetCell(new CellAddress(sheet.Id, 2, col), new NumberValue(col));

        sheet.PivotTables.Add(new PivotTableModel
        {
            Name = "PivotTable1",
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 20, 3)), // A1:C20
            TargetRange = new GridRange(new CellAddress(sheet.Id, 1, 10), new CellAddress(sheet.Id, 5, 12)),
        });

        // Select A2:A2 (inside the pivot's source rows) and Delete Cells > Shift Cells Up.
        var range = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, 1));
        var outcome = new DeleteCellsCommand(sheet.Id, range, DeleteCellsShiftDirection.Up).Apply(ctx);

        outcome.Success.Should().BeFalse("Excel refuses to shift cells that would disrupt a PivotTable's source layout");
        sheet.PivotTables.Single().SourceRange.Should().Be(
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 20, 3)),
            "a rejected operation must leave the PivotTable's SourceRange untouched");
        sheet.GetValue(2, 1).Should().Be(new NumberValue(1), "a rejected operation must leave cell data untouched");
    }
}
