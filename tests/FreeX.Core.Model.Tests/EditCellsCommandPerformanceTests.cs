using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class EditCellsCommandPerformanceTests
{
    [Fact]
    public void AffectedCells_ReusesPrecomputedAddressList()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var command = new EditCellsCommand(
            sheet.Id,
            [
                (a1, Cell.FromValue(new TextValue("A"))),
                (b1, Cell.FromValue(new TextValue("B")))
            ]);

        var affectedCells = command.AffectedCells;
        var outcome = command.Apply(new SimpleCtx(workbook));

        command.AffectedCells.Should().BeSameAs(affectedCells);
        outcome.AffectedCells.Should().BeSameAs(affectedCells);
        outcome.AffectedCells.Should().Equal(a1, b1);
    }

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
