using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.Core.Model.Tests;

public sealed class DataTableCommandTests
{
    [Fact]
    public void OneVariableDataTableCommand_FillsResultFormulasAndUndoRestores()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new SimpleCtx(workbook);
        var inputCell = new CellAddress(sheet.Id, 1, 2);
        var formulaCell = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(inputCell, new NumberValue(10));
        sheet.SetFormula(formulaCell, "B1*2");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new TextValue("Result"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 4), new TextValue("old"));

        var command = new OneVariableDataTableCommand(
            new GridRange(new CellAddress(sheet.Id, 1, 3), new CellAddress(sheet.Id, 3, 4)),
            formulaCell,
            inputCell);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetCell(2, 4)!.FormulaText.Should().Be("C2*2");
        sheet.GetCell(3, 4)!.FormulaText.Should().Be("C3*2");

        command.Revert(ctx);

        sheet.GetValue(2, 4).Should().Be(new TextValue("old"));
        sheet.GetCell(3, 4).Should().BeNull();
    }

    [Fact]
    public void OneVariableDataTableCommand_RowInputUsesTopRowTrialValues()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new SimpleCtx(workbook);
        var inputCell = new CellAddress(sheet.Id, 1, 2);
        var formulaCell = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(inputCell, new NumberValue(10));
        sheet.SetFormula(formulaCell, "B1*2");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("old"));

        var command = new OneVariableDataTableCommand(
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 3)),
            formulaCell,
            inputCell,
            DataTableInputOrientation.Row);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetCell(2, 2)!.FormulaText.Should().Be("B1*2");
        sheet.GetCell(2, 3)!.FormulaText.Should().Be("C1*2");

        command.Revert(ctx);

        sheet.GetValue(2, 2).Should().Be(new TextValue("old"));
        sheet.GetCell(2, 3).Should().BeNull();
    }

    [Fact]
    public void TwoVariableDataTableCommand_FillsGridFormulasAndUndoRestores()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new SimpleCtx(workbook);
        var rowInputCell = new CellAddress(sheet.Id, 1, 2);
        var columnInputCell = new CellAddress(sheet.Id, 1, 3);
        var formulaCell = new CellAddress(sheet.Id, 1, 4);
        sheet.SetCell(rowInputCell, new NumberValue(10));
        sheet.SetCell(columnInputCell, new NumberValue(20));
        sheet.SetFormula(formulaCell, "B1+C1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 5), new NumberValue(100));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 6), new NumberValue(200));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 4), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 4), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 5), new TextValue("old"));

        var command = new TwoVariableDataTableCommand(
            new GridRange(new CellAddress(sheet.Id, 1, 4), new CellAddress(sheet.Id, 3, 6)),
            formulaCell,
            rowInputCell,
            columnInputCell);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetCell(2, 5)!.FormulaText.Should().Be("E1+D2");
        sheet.GetCell(2, 6)!.FormulaText.Should().Be("F1+D2");
        sheet.GetCell(3, 5)!.FormulaText.Should().Be("E1+D3");
        sheet.GetCell(3, 6)!.FormulaText.Should().Be("F1+D3");

        command.Revert(ctx);

        sheet.GetValue(2, 5).Should().Be(new TextValue("old"));
        sheet.GetCell(3, 6).Should().BeNull();
    }

    private sealed class SimpleCtx(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;
        public Sheet GetSheet(SheetId sheetId) => Workbook.GetSheet(sheetId)!;
    }
}
