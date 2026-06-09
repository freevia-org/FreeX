using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Calc.Tests;

public sealed class DataTableCommandCalcTests
{
    [Fact]
    public void OneVariableDataTableCommand_ReportsAffectedCellsForAutomaticRecalculation()
    {
        var (workbook, sheet, context) = TestWorkbookFixture.CreateContext("DataTableCalc");
        var inputCell = new CellAddress(sheet.Id, 1, 2);
        var tableFormula = new CellAddress(sheet.Id, 1, 4);
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());

        sheet.SetCell(inputCell, new NumberValue(10));
        sheet.SetFormula(tableFormula, "B1+5");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(2));
        engine.RecalculateAllFormulas(workbook);

        var command = new OneVariableDataTableCommand(
            new GridRange(new CellAddress(sheet.Id, 1, 3), new CellAddress(sheet.Id, 3, 4)),
            tableFormula,
            inputCell,
            DataTableInputOrientation.Column);

        var outcome = command.Apply(context);
        outcome.Success.Should().BeTrue();
        outcome.AffectedCells.Should().Equal(
            new CellAddress(sheet.Id, 2, 4),
            new CellAddress(sheet.Id, 3, 4));

        engine.Recalculate(workbook, outcome.AffectedCells!);

        sheet.GetValue(2, 4).Should().Be(new NumberValue(6));
        sheet.GetValue(3, 4).Should().Be(new NumberValue(7));
        sheet.GetValue(inputCell).Should().Be(new NumberValue(10));
        sheet.GetValue(tableFormula).Should().Be(new NumberValue(15));
    }
}
