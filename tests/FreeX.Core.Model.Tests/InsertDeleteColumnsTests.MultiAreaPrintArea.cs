using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public partial class InsertDeleteColumnsTests
{
    // Regression tests for LL1: undo of insert/delete columns used to snapshot only the
    // first print area (via the single-area PrintArea convenience accessor), so undoing
    // with a multi-area print area silently discarded all but the first area.

    [Fact]
    public void InsertColumn_MultiAreaPrintArea_UndoRestoresBothAreas()
    {
        var (_, sheet, ctx) = Setup();
        var area1 = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 3));
        var area2 = new GridRange(new CellAddress(sheet.Id, 1, 5), new CellAddress(sheet.Id, 5, 7));
        sheet.SetPrintAreas([area1, area2]);

        var cmd = new InsertColumnsCommand(sheet.Id, beforeCol: 3, count: 2);
        cmd.Apply(ctx);

        // Both areas should be shifted right after apply.
        sheet.PrintAreas.Should().HaveCount(2);

        cmd.Revert(ctx);

        // Undo must restore both original areas exactly.
        sheet.PrintAreas.Should().HaveCount(2);
        sheet.PrintAreas.Should().ContainInOrder(area1, area2);
    }

    [Fact]
    public void DeleteColumn_MultiAreaPrintArea_UndoRestoresBothAreas()
    {
        var (_, sheet, ctx) = Setup();
        var area1 = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 3));
        var area2 = new GridRange(new CellAddress(sheet.Id, 1, 5), new CellAddress(sheet.Id, 5, 7));
        sheet.SetPrintAreas([area1, area2]);

        var cmd = new DeleteColumnsCommand(sheet.Id, startCol: 3, count: 2);
        cmd.Apply(ctx);

        // Both areas should be shrunk/shifted left after apply.
        sheet.PrintAreas.Should().HaveCount(2);

        cmd.Revert(ctx);

        // Undo must restore both original areas exactly.
        sheet.PrintAreas.Should().HaveCount(2);
        sheet.PrintAreas.Should().ContainInOrder(area1, area2);
    }

    [Fact]
    public void InsertColumn_SingleAreaPrintArea_UndoRoundTripUnchanged()
    {
        // Verify single-area print areas are not regressed.
        var (_, sheet, ctx) = Setup();
        var area = new GridRange(new CellAddress(sheet.Id, 1, 5), new CellAddress(sheet.Id, 3, 6));
        sheet.PrintArea = area;

        var cmd = new InsertColumnsCommand(sheet.Id, beforeCol: 3, count: 2);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        sheet.PrintAreas.Should().HaveCount(1);
        sheet.PrintAreas[0].Should().Be(area);
    }

    [Fact]
    public void DeleteColumn_SingleAreaPrintArea_UndoRoundTripUnchanged()
    {
        // Verify single-area print areas are not regressed.
        var (_, sheet, ctx) = Setup();
        var area = new GridRange(new CellAddress(sheet.Id, 1, 6), new CellAddress(sheet.Id, 3, 7));
        sheet.PrintArea = area;

        var cmd = new DeleteColumnsCommand(sheet.Id, startCol: 3, count: 2);
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        sheet.PrintAreas.Should().HaveCount(1);
        sheet.PrintAreas[0].Should().Be(area);
    }
}
