using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R118-calc-except-data-tables + R118-data-table-intermediate-precedent-refresh: the combined case
/// neither of round 118's two independent fix chains ever exercised together. One chain
/// (<see cref="WorkbookCellEditService.RecalculateIfAutomatic"/>) made
/// <see cref="WorkbookCalculationMode.AutomaticExceptDataTables"/> leave a Data Table body frozen at
/// edit time; the other (<see cref="DataTableAutoRefreshEffects"/>'s driver-precedent detection)
/// made editing an INTERMEDIATE precedent cell -- one a driver formula reaches only indirectly,
/// through another formula cell that gets textually inlined into the body -- re-derive the body's
/// formula text. Combined, without a shared decision point, an intermediate-precedent edit in
/// AutomaticExceptDataTables mode used to rewrite the body cell to a brand-new, not-yet-evaluated
/// Cell and then skip evaluating it (the recalc-level freeze), leaving it permanently BLANK -- neither
/// frozen at its previous value nor correctly recomputed. These tests pin the fix: both re-deriving
/// the body's formula TEXT (DataTableAutoRefreshEffects.Apply) and re-evaluating its VALUE
/// (RecalcEngine's skipDataTableBodyCells) are now gated by the SAME CalculationMode check, so an
/// intermediate-precedent edit is frozen in AutomaticExceptDataTables mode exactly like a direct
/// driver-cell edit, and still recomputed live in Automatic mode.
/// </summary>
public sealed class R118_DataTableIntermediatePrecedentCalcModeInteractionTests
{
    [Fact]
    public void AutomaticExceptDataTables_EditingIntermediatePrecedent_LeavesDataTableBodyFrozenUntilF9()
    {
        var (workbook, sheet, service, helperCell, bodyD2, bodyD3) = CreateEditServiceWithIndirectDataTable();

        // Sanity: the table computed normally while still in the default Automatic mode.
        sheet.GetCell(bodyD2)!.FormulaText.Should().Be("(C2*2)");
        sheet.GetCell(bodyD3)!.FormulaText.Should().Be("(C3*2)");
        sheet.GetCell(bodyD2)!.Value.Should().Be(new NumberValue(2));
        sheet.GetCell(bodyD3)!.Value.Should().Be(new NumberValue(4));

        workbook.CalculationMode = WorkbookCalculationMode.AutomaticExceptDataTables;

        // Retype the INTERMEDIATE precedent (E1), never the driver cell (D1) itself -- the master
        // formula "=E1" reaches the input cell only through E1's own formula.
        var result = service.CommitCellText(workbook, sheet.Id, helperCell, "=B1*3");

        result.Success.Should().BeTrue();

        // Nothing about the body -- neither its baked formula text nor its computed value -- may
        // change while frozen: a blank body is not a defensible outcome in any mode.
        sheet.GetCell(bodyD2)!.FormulaText.Should().Be("(C2*2)",
            "a Data Table body must not re-derive its formula text automatically in AutomaticExceptDataTables mode");
        sheet.GetCell(bodyD3)!.FormulaText.Should().Be("(C3*2)",
            "a Data Table body must not re-derive its formula text automatically in AutomaticExceptDataTables mode");
        sheet.GetCell(bodyD2)!.Value.Should().Be(new NumberValue(2),
            "a Data Table body cell must not recalculate automatically in AutomaticExceptDataTables mode");
        sheet.GetCell(bodyD3)!.Value.Should().Be(new NumberValue(4),
            "a Data Table body cell must not recalculate automatically in AutomaticExceptDataTables mode");

        // F9 (Calculate Now) must still force the Data Table to re-derive its formula text from the
        // now-current intermediate precedent AND recompute.
        service.RecalculateAll(workbook);

        sheet.GetCell(bodyD2)!.FormulaText.Should().Be("(C2*3)");
        sheet.GetCell(bodyD3)!.FormulaText.Should().Be("(C3*3)");
        sheet.GetCell(bodyD2)!.Value.Should().Be(new NumberValue(3));
        sheet.GetCell(bodyD3)!.Value.Should().Be(new NumberValue(6));
    }

    // No-regression sibling: the same intermediate-precedent edit, in plain Automatic mode, must keep
    // re-deriving and recomputing the Data Table body immediately -- AutomaticExceptDataTables is the
    // only mode that gets the freeze carve-out.
    [Fact]
    public void Automatic_EditingIntermediatePrecedent_StillRefreshesDataTableBodyImmediately()
    {
        var (workbook, sheet, service, helperCell, bodyD2, bodyD3) = CreateEditServiceWithIndirectDataTable();

        workbook.CalculationMode.Should().Be(WorkbookCalculationMode.Automatic);

        var result = service.CommitCellText(workbook, sheet.Id, helperCell, "=B1*3");

        result.Success.Should().BeTrue();
        sheet.GetCell(bodyD2)!.FormulaText.Should().Be("(C2*3)");
        sheet.GetCell(bodyD3)!.FormulaText.Should().Be("(C3*3)");
        sheet.GetCell(bodyD2)!.Value.Should().Be(new NumberValue(3));
        sheet.GetCell(bodyD3)!.Value.Should().Be(new NumberValue(6));
    }

    /// <summary>
    /// Builds: B1 = the Data Table's input cell, E1 = <paramref name="helperCell"/> = "B1*2" (an
    /// intermediate precedent), D1 = the master formula "=E1" (an INDIRECT reference to the input
    /// cell, reached only through E1), C2/C3 = trial input header values 1 and 2, and a one-variable,
    /// column-oriented Data Table (via the real <see cref="OneVariableDataTableCommand"/>) whose body
    /// lands at D2/D3 (<paramref name="bodyD2"/>/<paramref name="bodyD3"/>). At creation time,
    /// InlineAndSubstitute cannot match B1 directly in "E1", so it recursively inlines E1's formula
    /// text -- exactly the shape R118_DataTableIntermediatePrecedentRefreshTests exercises at the
    /// model level, routed here instead through the real product entry point
    /// (<see cref="WorkbookCellEditService"/>) so the CalculationMode interaction can be observed.
    /// </summary>
    private static (Workbook Workbook, Sheet Sheet, WorkbookCellEditService Service, CellAddress HelperCell, CellAddress BodyD2, CellAddress BodyD3)
        CreateEditServiceWithIndirectDataTable()
    {
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        var recalcEngine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        var commandBus = new CommandBus(_ => new WorkbookCommandContext(workbook));
        var service = new WorkbookCellEditService(commandBus, recalcEngine);

        var inputCell = new CellAddress(sheet.Id, 1, 2); // B1
        var helperCell = new CellAddress(sheet.Id, 1, 5); // E1 -- NOT part of the table range at all
        var tableFormula = new CellAddress(sheet.Id, 1, 4); // D1

        sheet.SetCell(inputCell, new NumberValue(10));
        sheet.SetFormula(helperCell, "B1*2");
        sheet.SetFormula(tableFormula, "E1");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(1)); // C2
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(2)); // C3
        recalcEngine.RecalculateAllFormulas(workbook);

        var command = new OneVariableDataTableCommand(
            new GridRange(new CellAddress(sheet.Id, 1, 3), new CellAddress(sheet.Id, 3, 4)), // C1:D3
            tableFormula,
            inputCell,
            DataTableInputOrientation.Column);

        var createResult = service.ExecuteEditCommand(workbook, command);
        createResult.Success.Should().BeTrue();

        var bodyD2 = new CellAddress(sheet.Id, 2, 4); // D2
        var bodyD3 = new CellAddress(sheet.Id, 3, 4); // D3

        return (workbook, sheet, service, helperCell, bodyD2, bodyD3);
    }
}
