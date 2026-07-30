using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R96-commands-undo-affected-cells-1: <see cref="CommandBus.Undo"/> used to report
/// <c>entry.Payload</c> -- the AffectedCells list captured at the ORIGINAL forward
/// Apply/Execute time (i.e. the POST-shift addresses) -- completely unconditionally for
/// Insert/Delete Rows(Columns) commands. Those commands' <c>Revert()</c> physically moves each
/// relocated formula cell back to its ORIGINAL, pre-shift address -- a different address than
/// Apply ever reported. Once something rebuilds the dependency graph purely from CURRENT sheet
/// occupancy in between (Calculate Now / F9, i.e. <see cref="RecalcEngine.RebuildFormulaDependencies"/>),
/// the stale post-shift address Undo reports no longer holds a formula (ClearFormulaDependencies is a
/// no-op there) and the cell's TRUE new location is never registered at all -- permanently
/// disabling its recalculation until the next full Calculate Now.
///
/// These tests exercise the real production entry points end to end: <see cref="WorkbookCellEditService"/>
/// (the same service the WPF/Avalonia shells call for every ribbon action), which wraps the real
/// <see cref="CommandBus"/> and <see cref="RecalcEngine"/> -- no hand-built model or simulated
/// pipeline.
/// </summary>
public sealed class R96_UndoInsertDeleteRowsColumnsAffectedCellsTests
{
    [Fact]
    public void UndoInsertRows_AfterCalculateNow_RecalculatesRelocatedFormulaOnPrecedentEdit()
    {
        var (workbook, sheet, _, service, recalcEngine) = CreateEditService();
        var d1 = new CellAddress(sheet.Id, 1, 4);   // D1: the precedent
        var e5 = new CellAddress(sheet.Id, 5, 5);   // E5: "=D1*2" before the insert
        var e6 = new CellAddress(sheet.Id, 6, 5);   // E5 relocates to E6 after inserting 1 row above it

        sheet.SetCell(d1, new NumberValue(10));
        sheet.SetFormula(e5, "D1*2");
        recalcEngine.RecalculateAllFormulas(workbook);
        sheet.GetCell(e5)!.Value.Should().Be(new NumberValue(20));

        // Insert 1 row above row 5 through the real command-bus entry point -- exactly what the
        // "Insert Sheet Rows" ribbon command reaches.
        var insertResult = service.ExecuteEditCommand(workbook, new InsertRowsCommand(sheet.Id, beforeRow: 5, count: 1));
        insertResult.Success.Should().BeTrue();
        sheet.GetCell(e6)!.FormulaText.Should().Be("D1*2");
        sheet.GetCell(e5).Should().BeNull();

        // Press Calculate Now (F9): rebuilds the whole per-sheet dependency graph strictly from
        // CURRENTLY-occupied formula cells -- E6 only, since E5 is blank at this point.
        recalcEngine.RecalculateAllFormulas(workbook);

        // Undo the row insert through the real command-bus entry point.
        var undoResult = service.UndoLastEdit(workbook);
        undoResult.Success.Should().BeTrue();
        sheet.GetCell(e5)!.FormulaText.Should().Be("D1*2");
        sheet.GetCell(e6).Should().BeNull();

        // THE FIX: Undo must report the address Revert ACTUALLY put the formula at (E5), not the
        // stale forward-Apply payload address (E6) -- otherwise the post-command dependency-graph
        // update visits the wrong (now-blank) cell and E5 is left with no graph registration at all.
        undoResult.AffectedCells.Should().Contain(e5,
            "Undo must report the CURRENT (post-Revert) address of the relocated formula cell, " +
            "not the frozen forward-Apply payload");

        // Edit the precedent through the real cell-edit entry point and confirm the relocated
        // formula recalculates -- proving the dependency graph really has E5 registered against D1
        // (not orphaned).
        var editResult = service.CommitCellText(workbook, sheet.Id, d1, "50");
        editResult.Success.Should().BeTrue();
        sheet.GetCell(e5)!.Value.Should().Be(new NumberValue(100),
            "a formula that relocates back to its original cell on Undo must keep recalculating " +
            "normally on every subsequent edit to its precedent, exactly like real Excel");
    }

    [Fact]
    public void UndoDeleteRows_AfterCalculateNow_RecalculatesShiftedAndRestoredFormulasOnPrecedentEdit()
    {
        var (workbook, sheet, _, service, recalcEngine) = CreateEditService();
        var d1 = new CellAddress(sheet.Id, 1, 4);   // D1: the precedent
        var e5 = new CellAddress(sheet.Id, 5, 5);   // deleted along with row 5, then restored by Undo
        var e6 = new CellAddress(sheet.Id, 6, 5);   // shifts up to E5 after deleting row 5, back after Undo

        sheet.SetCell(d1, new NumberValue(10));
        sheet.SetFormula(e5, "D1*3");
        sheet.SetFormula(e6, "D1*4");
        recalcEngine.RecalculateAllFormulas(workbook);
        sheet.GetCell(e5)!.Value.Should().Be(new NumberValue(30));
        sheet.GetCell(e6)!.Value.Should().Be(new NumberValue(40));

        // Delete row 5 through the real command-bus entry point: E5's formula is removed, E6's
        // formula shifts up to E5.
        var deleteResult = service.ExecuteEditCommand(workbook, new DeleteRowsCommand(sheet.Id, startRow: 5, count: 1));
        deleteResult.Success.Should().BeTrue();
        sheet.GetCell(e5)!.FormulaText.Should().Be("D1*4");

        // Press Calculate Now (F9): rebuilds the graph strictly from current occupancy (E5 holding
        // the shifted "D1*4" formula only).
        recalcEngine.RecalculateAllFormulas(workbook);

        // Undo the row delete through the real command-bus entry point: E5's "D1*4" shifts back
        // down to E6, and E5's original "D1*3" reappears.
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

    [Fact]
    public void UndoInsertColumns_AfterCalculateNow_RecalculatesRelocatedFormulaOnPrecedentEdit()
    {
        var (workbook, sheet, _, service, recalcEngine) = CreateEditService();
        var a1 = new CellAddress(sheet.Id, 1, 1);   // A1: the precedent
        var e1 = new CellAddress(sheet.Id, 1, 5);   // E1: "=A1*2" before the insert
        var f1 = new CellAddress(sheet.Id, 1, 6);   // E1 relocates to F1 after inserting 1 column before it

        sheet.SetCell(a1, new NumberValue(10));
        sheet.SetFormula(e1, "A1*2");
        recalcEngine.RecalculateAllFormulas(workbook);
        sheet.GetCell(e1)!.Value.Should().Be(new NumberValue(20));

        var insertResult = service.ExecuteEditCommand(workbook, new InsertColumnsCommand(sheet.Id, beforeCol: 5, count: 1));
        insertResult.Success.Should().BeTrue();
        sheet.GetCell(f1)!.FormulaText.Should().Be("A1*2");
        sheet.GetCell(e1).Should().BeNull();

        recalcEngine.RecalculateAllFormulas(workbook);

        var undoResult = service.UndoLastEdit(workbook);
        undoResult.Success.Should().BeTrue();
        sheet.GetCell(e1)!.FormulaText.Should().Be("A1*2");
        sheet.GetCell(f1).Should().BeNull();

        undoResult.AffectedCells.Should().Contain(e1,
            "Undo must report the CURRENT (post-Revert) address of the relocated formula cell, " +
            "not the frozen forward-Apply payload");

        var editResult = service.CommitCellText(workbook, sheet.Id, a1, "50");
        editResult.Success.Should().BeTrue();
        sheet.GetCell(e1)!.Value.Should().Be(new NumberValue(100),
            "a formula that relocates back to its original cell on Undo must keep recalculating " +
            "normally on every subsequent edit to its precedent");
    }

    [Fact]
    public void UndoDeleteColumns_AfterCalculateNow_RecalculatesShiftedAndRestoredFormulasOnPrecedentEdit()
    {
        var (workbook, sheet, _, service, recalcEngine) = CreateEditService();
        var a1 = new CellAddress(sheet.Id, 1, 1);   // A1: the precedent
        var e1 = new CellAddress(sheet.Id, 1, 5);   // deleted along with column E, then restored by Undo
        var f1 = new CellAddress(sheet.Id, 1, 6);   // shifts left to E1 after deleting column E, back after Undo

        sheet.SetCell(a1, new NumberValue(10));
        sheet.SetFormula(e1, "A1*3");
        sheet.SetFormula(f1, "A1*4");
        recalcEngine.RecalculateAllFormulas(workbook);

        var deleteResult = service.ExecuteEditCommand(workbook, new DeleteColumnsCommand(sheet.Id, startCol: 5, count: 1));
        deleteResult.Success.Should().BeTrue();
        sheet.GetCell(e1)!.FormulaText.Should().Be("A1*4");

        recalcEngine.RecalculateAllFormulas(workbook);

        var undoResult = service.UndoLastEdit(workbook);
        undoResult.Success.Should().BeTrue();
        sheet.GetCell(e1)!.FormulaText.Should().Be("A1*3");
        sheet.GetCell(f1)!.FormulaText.Should().Be("A1*4");

        undoResult.AffectedCells.Should().Contain(e1,
            "the restored-deleted formula cell's address must be reported so it gets re-registered");
        undoResult.AffectedCells.Should().Contain(f1,
            "the shifted-back formula cell's CURRENT address must be reported, not the stale " +
            "post-delete address");

        var editResult = service.CommitCellText(workbook, sheet.Id, a1, "100");
        editResult.Success.Should().BeTrue();
        sheet.GetCell(e1)!.Value.Should().Be(new NumberValue(300));
        sheet.GetCell(f1)!.Value.Should().Be(new NumberValue(400));
    }

    /// <summary>
    /// No-regression guard: CommandBus.Undo now prefers a command's LIVE
    /// <see cref="IAffectedCellsCommand.AffectedCells"/> over the frozen forward-Apply payload
    /// (see CommandBus.Undo). For the ordinary single-cell edit path (<see cref="EditCellsCommand"/>,
    /// whose AffectedCells field is `readonly` and never mutated by Revert), this must be a
    /// complete no-op -- undoing a plain value edit still recalculates the right dependent, exactly
    /// as before this change.
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
