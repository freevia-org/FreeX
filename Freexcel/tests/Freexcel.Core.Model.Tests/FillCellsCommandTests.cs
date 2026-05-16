using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.Core.Model.Tests;

public sealed class FillCellsCommandTests
{
    [Fact]
    public void FillDown_CopiesTopRowCellsAndAdjustsRelativeFormulas()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var source = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(source, Cell.FromFormula("B1+$C$1"));

        var command = new FillCellsCommand(
            sheet.Id,
            new GridRange(source, new CellAddress(sheet.Id, 3, 1)),
            FillCellsDirection.Down);

        command.Apply(new SimpleCommandContext(workbook)).Success.Should().BeTrue();

        sheet.GetCell(new CellAddress(sheet.Id, 2, 1))!.FormulaText.Should().Be("B2+$C$1");
        sheet.GetCell(new CellAddress(sheet.Id, 3, 1))!.FormulaText.Should().Be("B3+$C$1");
    }

    [Fact]
    public void FillRight_CopiesLeftColumnCellsAndUndoRestoresTargets()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var source = new CellAddress(sheet.Id, 1, 1);
        var target = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(source, Cell.FromValue(new TextValue("copied")));
        sheet.SetCell(target, Cell.FromValue(new TextValue("old")));
        var context = new SimpleCommandContext(workbook);

        var command = new FillCellsCommand(
            sheet.Id,
            new GridRange(source, target),
            FillCellsDirection.Right);

        command.Apply(context).Success.Should().BeTrue();
        sheet.GetCell(target)!.Value.Should().Be(new TextValue("copied"));

        command.Revert(context);

        sheet.GetCell(target)!.Value.Should().Be(new TextValue("old"));
    }

    private sealed class SimpleCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook => workbook;
        public Sheet GetSheet(SheetId sheetId) => workbook.GetSheet(sheetId)!;
    }
}
