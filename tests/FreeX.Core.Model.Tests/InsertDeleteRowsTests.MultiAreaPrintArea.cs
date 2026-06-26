using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public partial class InsertDeleteRowsTests
{
    // Regression tests for LL1: undo of insert/delete rows used to snapshot only the
    // first print area (via the single-area PrintArea convenience accessor), so undoing
    // with a multi-area print area silently discarded all but the first area.

    [Fact]
    public void InsertRow_MultiAreaPrintArea_UndoRestoresBothAreas()
    {
        var (_, sheet, ctx) = Setup();
        var area1 = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 3));
        var area2 = new GridRange(new CellAddress(sheet.Id, 1, 5), new CellAddress(sheet.Id, 5, 7));
        sheet.SetPrintAreas([area1, area2]);

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 3, count: 2);
        cmd.Apply(ctx);

        // Forward shift must still operate on all areas.
        sheet.PrintAreas.Should().HaveCount(2);

        cmd.Revert(ctx);

        // Undo must restore both original areas exactly.
        sheet.PrintAreas.Should().HaveCount(2);
        sheet.PrintAreas.Should().ContainInOrder(area1, area2);
    }

    [Fact]
    public void DeleteRow_MultiAreaPrintArea_UndoRestoresBothAreas()
    {
        var (_, sheet, ctx) = Setup();
        var area1 = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 3));
        var area2 = new GridRange(new CellAddress(sheet.Id, 1, 5), new CellAddress(sheet.Id, 5, 7));
        sheet.SetPrintAreas([area1, area2]);

        var cmd = new DeleteRowsCommand(sheet.Id, startRow: 3, count: 2);
        cmd.Apply(ctx);

        // Both areas should be shrunk/shifted down after apply.
        sheet.PrintAreas.Should().HaveCount(2);

        cmd.Revert(ctx);

        // Undo must restore both original areas exactly.
        sheet.PrintAreas.Should().HaveCount(2);
        sheet.PrintAreas.Should().ContainInOrder(area1, area2);
    }

    [Fact]
    public void InsertRow_SingleAreaPrintArea_UndoRoundTripUnchanged()
    {
        // Verify single-area print areas are not regressed.
        var (_, sheet, ctx) = Setup();
        var area = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 6, 3));
        sheet.PrintArea = area;

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 3, count: 2);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        sheet.PrintAreas.Should().HaveCount(1);
        sheet.PrintAreas[0].Should().Be(area);
    }

    [Fact]
    public void DeleteRow_SingleAreaPrintArea_UndoRoundTripUnchanged()
    {
        // Verify single-area print areas are not regressed.
        var (_, sheet, ctx) = Setup();
        var area = new GridRange(new CellAddress(sheet.Id, 6, 1), new CellAddress(sheet.Id, 7, 3));
        sheet.PrintArea = area;

        var cmd = new DeleteRowsCommand(sheet.Id, startRow: 3, count: 2);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        sheet.PrintAreas.Should().HaveCount(1);
        sheet.PrintAreas[0].Should().Be(area);
    }
}
