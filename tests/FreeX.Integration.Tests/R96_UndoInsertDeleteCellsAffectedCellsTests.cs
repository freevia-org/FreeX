using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R96-commands-undo-affected-cells-2: sibling of R96_UndoInsertDeleteRowsColumnsAffectedCellsTests,
/// for the band-scoped, shift-direction-parameterized InsertCellsCommand/DeleteCellsCommand (Excel's
/// "Insert Cells..."/"Delete Cells..." dialog, as opposed to whole-row/whole-column insert/delete).
/// These commands captured their moved cells via CellShiftSnapshot (raw (CellAddress, Cell) pairs)
/// rather than CellStateSnapshot, and did not implement IAffectedCellsCommand at all -- so
/// CommandBus.Undo always fell back to the frozen forward-Apply payload (the POST-shift address) for
/// them, carrying the exact same bug as the just-fixed row/column commands: once something rebuilds
/// the dependency graph purely from CURRENT sheet occupancy in between (Calculate Now / F9), the stale
/// post-shift address Undo reports no longer holds a formula and the cell's TRUE new location is
/// never registered -- permanently disabling its recalculation.
///
/// These tests exercise the real production entry points end to end: <see cref="WorkbookCellEditService"/>
/// wraps the real <see cref="CommandBus"/> and <see cref="RecalcEngine"/> -- no hand-built model.
/// </summary>
public sealed class R96_UndoInsertDeleteCellsAffectedCellsTests
{
    [Fact]
    public void UndoInsertCellsShiftRight_AfterCalculateNow_RecalculatesRelocatedFormulaOnPrecedentEdit()
    {
        var (workbook, sheet, _, service, recalcEngine) = CreateEditService();
        var precedent = new CellAddress(sheet.Id, 10, 1);   // A10: the precedent, outside the shift band
        var insertAt = new CellAddress(sheet.Id, 1, 1);     // A1: the insert point (NOT the formula cell)
        var c1 = new CellAddress(sheet.Id, 1, 3);           // C1: "=A10*2" before the insert
        var d1 = new CellAddress(sheet.Id, 1, 4);           // C1 relocates to D1 after inserting at A1, shift right

        sheet.SetCell(precedent, new NumberValue(10));
        sheet.SetFormula(c1, "A10*2");
        recalcEngine.RecalculateAllFormulas(workbook);
        sheet.GetCell(c1)!.Value.Should().Be(new NumberValue(20));

        // Insert range = A1:A1 (row 1, NOT C1 itself) -- the shift-right region spans row 1, columns
        // A onward, so C1 still relocates to D1, but the insert range's own AllCells() (A1) no longer
        // coincides with the moved cell's original address (C1). This decoupling matters: if the
        // range and the moved cell's original address were the same, the pre-fix stale forward-Apply
        // payload (which always includes _range.AllCells()) would accidentally already contain the
        // right address and mask the bug.
        var range = new GridRange(insertAt, insertAt);
        var insertResult = service.ExecuteEditCommand(
            workbook, new InsertCellsCommand(sheet.Id, range, InsertCellsShiftDirection.Right));
        insertResult.Success.Should().BeTrue();
        sheet.GetCell(d1)!.FormulaText.Should().Be("A10*2");
        sheet.GetCell(c1).Should().BeNull();

        // Press Calculate Now (F9): rebuilds the whole per-sheet dependency graph strictly from
        // CURRENTLY-occupied formula cells -- D1 only, since C1 is blank at this point.
        recalcEngine.RecalculateAllFormulas(workbook);

        var undoResult = service.UndoLastEdit(workbook);
        undoResult.Success.Should().BeTrue();
        sheet.GetCell(c1)!.FormulaText.Should().Be("A10*2");
        sheet.GetCell(d1).Should().BeNull();

        // THE FIX: Undo must report the address Revert ACTUALLY put the formula at (C1), not the
        // stale forward-Apply payload address (D1).
        undoResult.AffectedCells.Should().Contain(c1,
            "Undo must report the CURRENT (post-Revert) address of the relocated formula cell, " +
            "not the frozen forward-Apply payload");

        var editResult = service.CommitCellText(workbook, sheet.Id, precedent, "50");
        editResult.Success.Should().BeTrue();
        sheet.GetCell(c1)!.Value.Should().Be(new NumberValue(100),
            "a formula that relocates back to its original cell on Undo must keep recalculating " +
            "normally on every subsequent edit to its precedent");
    }

    [Fact]
    public void UndoInsertCellsShiftDown_AfterCalculateNow_RecalculatesRelocatedFormulaOnPrecedentEdit()
    {
        var (workbook, sheet, _, service, recalcEngine) = CreateEditService();
        var precedent = new CellAddress(sheet.Id, 1, 10);   // J1: the precedent, outside the shift band
        var insertAt = new CellAddress(sheet.Id, 1, 5);     // E1: the insert point (NOT the formula cell)
        var e5 = new CellAddress(sheet.Id, 5, 5);           // E5: "=J1*2" before the insert
        var e6 = new CellAddress(sheet.Id, 6, 5);           // E5 relocates to E6 after inserting at E1, shift down

        sheet.SetCell(precedent, new NumberValue(10));
        sheet.SetFormula(e5, "J1*2");
        recalcEngine.RecalculateAllFormulas(workbook);
        sheet.GetCell(e5)!.Value.Should().Be(new NumberValue(20));

        // Insert range = E1:E1 (column E, NOT E5 itself) -- the shift-down region spans column E,
        // rows 1 onward, so E5 still relocates to E6, but the insert range's own AllCells() (E1) no
        // longer coincides with the moved cell's original address (E5). See the Shift-Right test
        // above for why this decoupling is required to actually exercise the bug.
        var range = new GridRange(insertAt, insertAt);
        var insertResult = service.ExecuteEditCommand(
            workbook, new InsertCellsCommand(sheet.Id, range, InsertCellsShiftDirection.Down));
        insertResult.Success.Should().BeTrue();
        sheet.GetCell(e6)!.FormulaText.Should().Be("J1*2");
        sheet.GetCell(e5).Should().BeNull();

        recalcEngine.RecalculateAllFormulas(workbook);

        var undoResult = service.UndoLastEdit(workbook);
        undoResult.Success.Should().BeTrue();
        sheet.GetCell(e5)!.FormulaText.Should().Be("J1*2");
        sheet.GetCell(e6).Should().BeNull();

        undoResult.AffectedCells.Should().Contain(e5,
            "Undo must report the CURRENT (post-Revert) address of the relocated formula cell, " +
            "not the frozen forward-Apply payload");

        var editResult = service.CommitCellText(workbook, sheet.Id, precedent, "50");
        editResult.Success.Should().BeTrue();
        sheet.GetCell(e5)!.Value.Should().Be(new NumberValue(100),
            "a formula that relocates back to its original cell on Undo must keep recalculating " +
            "normally on every subsequent edit to its precedent");
    }

    [Fact]
    public void UndoDeleteCellsShiftLeft_AfterCalculateNow_RecalculatesShiftedAndRestoredFormulasOnPrecedentEdit()
    {
        var (workbook, sheet, _, service, recalcEngine) = CreateEditService();
        var a1 = new CellAddress(sheet.Id, 1, 1);   // A1: the precedent
        var b1 = new CellAddress(sheet.Id, 1, 2);   // deleted, then restored by Undo
        var c1 = new CellAddress(sheet.Id, 1, 3);   // shifts left to B1 after delete, back after Undo

        sheet.SetCell(a1, new NumberValue(10));
        sheet.SetFormula(b1, "A1*3");
        sheet.SetFormula(c1, "A1*4");
        recalcEngine.RecalculateAllFormulas(workbook);
        sheet.GetCell(b1)!.Value.Should().Be(new NumberValue(30));
        sheet.GetCell(c1)!.Value.Should().Be(new NumberValue(40));

        var range = new GridRange(b1, b1);
        var deleteResult = service.ExecuteEditCommand(
            workbook, new DeleteCellsCommand(sheet.Id, range, DeleteCellsShiftDirection.Left));
        deleteResult.Success.Should().BeTrue();
        sheet.GetCell(b1)!.FormulaText.Should().Be("A1*4");

        // Press Calculate Now (F9): rebuilds the graph strictly from current occupancy (B1 holding
        // the shifted "A1*4" formula only).
        recalcEngine.RecalculateAllFormulas(workbook);

        var undoResult = service.UndoLastEdit(workbook);
        undoResult.Success.Should().BeTrue();
        sheet.GetCell(b1)!.FormulaText.Should().Be("A1*3");
        sheet.GetCell(c1)!.FormulaText.Should().Be("A1*4");

        undoResult.AffectedCells.Should().Contain(b1,
            "the restored-deleted formula cell's address must be reported so it gets re-registered");
        undoResult.AffectedCells.Should().Contain(c1,
            "the shifted-back formula cell's CURRENT address must be reported, not the stale " +
            "post-delete address");

        var editResult = service.CommitCellText(workbook, sheet.Id, a1, "100");
        editResult.Success.Should().BeTrue();
        sheet.GetCell(b1)!.Value.Should().Be(new NumberValue(300),
            "the restored-deleted formula must keep recalculating on every subsequent precedent edit");
        sheet.GetCell(c1)!.Value.Should().Be(new NumberValue(400),
            "the shifted-back formula must keep recalculating on every subsequent precedent edit");
    }

    [Fact]
    public void UndoDeleteCellsShiftUp_AfterCalculateNow_RecalculatesShiftedAndRestoredFormulasOnPrecedentEdit()
    {
        var (workbook, sheet, _, service, recalcEngine) = CreateEditService();
        var d1 = new CellAddress(sheet.Id, 1, 4);   // D1: the precedent
        var e5 = new CellAddress(sheet.Id, 5, 5);   // deleted, then restored by Undo
        var e6 = new CellAddress(sheet.Id, 6, 5);   // shifts up to E5 after delete, back after Undo

        sheet.SetCell(d1, new NumberValue(10));
        sheet.SetFormula(e5, "D1*3");
        sheet.SetFormula(e6, "D1*4");
        recalcEngine.RecalculateAllFormulas(workbook);
        sheet.GetCell(e5)!.Value.Should().Be(new NumberValue(30));
        sheet.GetCell(e6)!.Value.Should().Be(new NumberValue(40));

        var range = new GridRange(e5, e5);
        var deleteResult = service.ExecuteEditCommand(
            workbook, new DeleteCellsCommand(sheet.Id, range, DeleteCellsShiftDirection.Up));
        deleteResult.Success.Should().BeTrue();
        sheet.GetCell(e5)!.FormulaText.Should().Be("D1*4");

        recalcEngine.RecalculateAllFormulas(workbook);

        var undoResult = service.UndoLastEdit(workbook);
        undoResult.Success.Should().BeTrue();
        sheet.GetCell(e5)!.FormulaText.Should().Be("D1*3");
        sheet.GetCell(e6)!.FormulaText.Should().Be("D1*4");

        undoResult.AffectedCells.Should().Contain(e5,
            "the restored-deleted formula cell's address must be reported so it gets re-registered");
        undoResult.AffectedCells.Should().Contain(e6,
            "the shifted-back formula cell's CURRENT address must be reported, not the stale " +
            "post-delete address");

        var editResult = service.CommitCellText(workbook, sheet.Id, d1, "100");
        editResult.Success.Should().BeTrue();
        sheet.GetCell(e5)!.Value.Should().Be(new NumberValue(300),
            "the restored-deleted formula must keep recalculating on every subsequent precedent edit");
        sheet.GetCell(e6)!.Value.Should().Be(new NumberValue(400),
            "the shifted-back formula must keep recalculating on every subsequent precedent edit");
    }

    /// <summary>
    /// No-regression guard: with InsertCellsCommand/DeleteCellsCommand now implementing
    /// IAffectedCellsCommand, an ordinary single-cell edit's undo path (EditCellsCommand, unaffected
    /// by this change) must be unchanged.
    /// </summary>
    [Fact]
    public void UndoPlainCellEdit_StillRecalculatesDependentNormally()
    {
        var (workbook, sheet, _, service, recalcEngine) = CreateEditService();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, new NumberValue(1));
        sheet.SetFormula(b1, "A1+1");
        recalcEngine.RecalculateAllFormulas(workbook);

        var commitResult = service.CommitCellText(workbook, sheet.Id, a1, "4");
        commitResult.Success.Should().BeTrue();
        sheet.GetCell(b1)!.Value.Should().Be(new NumberValue(5));

        var undoResult = service.UndoLastEdit(workbook);
        undoResult.Success.Should().BeTrue();
        undoResult.AffectedCells.Should().Contain(a1);
        sheet.GetCell(a1)!.Value.Should().Be(new NumberValue(1));
        sheet.GetCell(b1)!.Value.Should().Be(new NumberValue(2),
            "undoing a plain value edit must still recalculate its dependent normally");
    }

    private static (
        Workbook Workbook,
        Sheet Sheet,
        CommandBus CommandBus,
        WorkbookCellEditService Service,
        RecalcEngine RecalcEngine) CreateEditService()
    {
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        var recalcEngine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        var commandBus = new CommandBus(_ => new WorkbookCommandContext(workbook));
        var service = new WorkbookCellEditService(commandBus, recalcEngine);
        return (workbook, sheet, commandBus, service, recalcEngine);
    }
}
