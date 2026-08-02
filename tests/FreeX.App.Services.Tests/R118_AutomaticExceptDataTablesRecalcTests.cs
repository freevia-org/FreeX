using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R118-calc-except-data-tables: <see cref="WorkbookCalculationMode.AutomaticExceptDataTables"/>
/// ("Automatic Except for Data Tables") is documented (see CalculationOptions.cs) as recalculating
/// everything EXCEPT What-If Analysis Data Tables, which should only refresh on F9/Shift+F9.
/// Before the fix, <see cref="WorkbookCellEditService.RecalculateIfAutomatic"/> treated
/// AutomaticExceptDataTables identically to plain Automatic, so a Data Table's body recalculated on
/// every ordinary edit -- including edits to some OTHER, unrelated part of the sheet that the
/// table's master formula happens to read -- exactly defeating the point of selecting this option.
/// These tests go through the real product entry point (<see cref="WorkbookCellEditService"/>'s
/// <see cref="WorkbookCellEditService.ExecuteEditCommand"/>/<see cref="WorkbookCellEditService.CommitCellText"/>,
/// the same ones <see cref="WorkbookSession"/> uses for every cell edit) rather than asserting on a
/// hand-built model.
/// </summary>
public sealed class R118_AutomaticExceptDataTablesRecalcTests
{
    [Fact]
    public void AutomaticExceptDataTables_EditingUnrelatedPrecedent_LeavesDataTableBodyFrozenUntilF9()
    {
        var (workbook, sheet, service, recalcEngine) = CreateEditServiceWithDataTable(
            out var multiplier, out var bodyC2, out var bodyC3);

        // Sanity: the table computed normally while still in the default Automatic mode.
        sheet.GetCell(bodyC2)!.Value.Should().Be(new NumberValue(2));
        sheet.GetCell(bodyC3)!.Value.Should().Be(new NumberValue(4));

        workbook.CalculationMode = WorkbookCalculationMode.AutomaticExceptDataTables;

        // Edit an "other part of the sheet" cell the master formula reads (NOT the Data Table's own
        // input/header cells) -- the exact scenario the mode's own doc comment and Excel's ground
        // truth describe: everything else should recalculate live, but the Data Table must not.
        var result = service.CommitCellText(workbook, sheet.Id, multiplier, "5");

        result.Success.Should().BeTrue();

        // The multiplier cell itself always updates -- only the Data Table's body must stay frozen.
        sheet.GetCell(multiplier)!.Value.Should().Be(new NumberValue(5));
        sheet.GetCell(bodyC2)!.Value.Should().Be(new NumberValue(2),
            "a Data Table body cell must not recalculate automatically in AutomaticExceptDataTables mode");
        sheet.GetCell(bodyC3)!.Value.Should().Be(new NumberValue(4),
            "a Data Table body cell must not recalculate automatically in AutomaticExceptDataTables mode");

        // F9 (Calculate Now) must still force the Data Table to pick up the new precedent value.
        service.RecalculateAll(workbook);

        sheet.GetCell(bodyC2)!.Value.Should().Be(new NumberValue(5));
        sheet.GetCell(bodyC3)!.Value.Should().Be(new NumberValue(10));
    }

    // No-regression sibling: the same edit, in plain Automatic mode, must keep rippling into the
    // Data Table exactly as before -- AutomaticExceptDataTables is the only mode that gets the new
    // carve-out.
    [Fact]
    public void Automatic_EditingUnrelatedPrecedent_StillRecalculatesDataTableBodyImmediately()
    {
        var (workbook, sheet, service, _) = CreateEditServiceWithDataTable(
            out var multiplier, out var bodyC2, out var bodyC3);

        workbook.CalculationMode.Should().Be(WorkbookCalculationMode.Automatic);

        var result = service.CommitCellText(workbook, sheet.Id, multiplier, "5");

        result.Success.Should().BeTrue();
        sheet.GetCell(bodyC2)!.Value.Should().Be(new NumberValue(5));
        sheet.GetCell(bodyC3)!.Value.Should().Be(new NumberValue(10));
    }

    /// <summary>
    /// Builds: A1 = <paramref name="multiplier"/> (a plain value cell the Data Table's master
    /// formula reads besides its own input cell), B1 = the Data Table's input cell, D1 = the master
    /// formula "B1*A1", C2/C3 = trial input header values 1 and 2, and a one-variable,
    /// column-oriented Data Table (via the real <see cref="OneVariableDataTableCommand"/>) whose
    /// body lands at D2/D3 (<paramref name="bodyC2"/>/<paramref name="bodyC3"/>). Mirrors
    /// DataTableCommandCalcTests' layout, with the added A1 multiplier so a precedent OTHER than the
    /// table's own input/header cells feeds the body formulas.
    /// </summary>
    private static (Workbook Workbook, Sheet Sheet, WorkbookCellEditService Service, RecalcEngine RecalcEngine)
        CreateEditServiceWithDataTable(out CellAddress multiplier, out CellAddress bodyC2, out CellAddress bodyC3)
    {
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        var recalcEngine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        var commandBus = new CommandBus(_ => new WorkbookCommandContext(workbook));
        var service = new WorkbookCellEditService(commandBus, recalcEngine);

        multiplier = new CellAddress(sheet.Id, 1, 1); // A1
        var inputCell = new CellAddress(sheet.Id, 1, 2); // B1
        var tableFormula = new CellAddress(sheet.Id, 1, 4); // D1

        sheet.SetCell(multiplier, new NumberValue(2));
        sheet.SetCell(inputCell, new NumberValue(0));
        sheet.SetFormula(tableFormula, "B1*A1");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(1)); // C2
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(2)); // C3
        recalcEngine.RecalculateAllFormulas(workbook);

        var command = new OneVariableDataTableCommand(
            new GridRange(new CellAddress(sheet.Id, 1, 3), new CellAddress(sheet.Id, 3, 4)),
            tableFormula,
            inputCell,
            DataTableInputOrientation.Column);

        var createResult = service.ExecuteEditCommand(workbook, command);
        createResult.Success.Should().BeTrue();

        bodyC2 = new CellAddress(sheet.Id, 2, 4); // D2
        bodyC3 = new CellAddress(sheet.Id, 3, 4); // D3

        return (workbook, sheet, service, recalcEngine);
    }
}
