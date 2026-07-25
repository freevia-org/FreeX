using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R90-app-goalseek-whatif-5-3: a What-If Analysis "Data Table" result body is a single logical
/// array in Excel ({=TABLE(,...)}), so editing/deleting just one interior cell must be blocked
/// ("You cannot change part of a Data Table.") even though FreeX stores each body cell as its own
/// ordinary formula cell. Exercises the real product entry points -- OneVariableDataTableCommand to
/// create the table and ClearContentsCommand (the command the Delete key dispatches) to attempt the
/// single-cell edit -- rather than poking the guard's internals directly.
/// </summary>
public sealed class R90_DataTableSplitGuardTests
{
    private static (Workbook workbook, Sheet sheet, ICommandContext ctx) CreateOneVariableDataTable()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var inputCell = new CellAddress(sheet.Id, 1, 2); // B1
        var formulaCell = new CellAddress(sheet.Id, 1, 4); // D1 -- the result column's own header formula
        sheet.SetCell(inputCell, new NumberValue(10));
        sheet.SetFormula(formulaCell, "B1*2");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(1)); // C2 trial value
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(2)); // C3 trial value

        // Column-oriented one-variable Data Table over C1:D3 -- trial values run down column C
        // (Start.Col), the result column D carries its own header formula at D1, and the body
        // (D2:D3) is what OneVariableDataTableCommand fills in and what must be guarded as a unit.
        var command = new OneVariableDataTableCommand(
            new GridRange(new CellAddress(sheet.Id, 1, 3), new CellAddress(sheet.Id, 3, 4)),
            formulaCell,
            inputCell);
        command.Apply(ctx).Success.Should().BeTrue();

        return (workbook, sheet, ctx);
    }

    [Fact]
    public void ClearContentsCommand_RejectsDeletingOneInteriorCellOfADataTableBody()
    {
        var (_, sheet, ctx) = CreateOneVariableDataTable();
        var singleBodyCell = new CellAddress(sheet.Id, 3, 4); // D3 -- one of the two body cells
        sheet.GetCell(3, 4)!.FormulaText.Should().Be("C3*2");

        var outcome = new ClearContentsCommand(sheet.Id, new GridRange(singleBodyCell, singleBodyCell)).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Be("You cannot change part of a Data Table.");
        // The cell must be untouched -- the command was rejected before any mutation.
        sheet.GetCell(3, 4)!.FormulaText.Should().Be("C3*2");
    }

    [Fact]
    public void ClearContentsCommand_AllowsClearingTheWholeDataTableBodyAtOnce()
    {
        var (_, sheet, ctx) = CreateOneVariableDataTable();
        var wholeBody = new GridRange(new CellAddress(sheet.Id, 2, 4), new CellAddress(sheet.Id, 3, 4));

        var outcome = new ClearContentsCommand(sheet.Id, wholeBody).Apply(ctx);

        outcome.Success.Should().BeTrue("Excel allows clearing an entire array/Data Table body in one action");
        sheet.GetCell(2, 4)!.Value.Should().Be(BlankValue.Instance);
        sheet.GetCell(3, 4)!.Value.Should().Be(BlankValue.Instance);
    }
}
