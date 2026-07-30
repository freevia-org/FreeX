using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Integration.Tests;

/// <summary>
/// R98-commands-dependency-vacated-1: InsertRowsCommand.MoveCellsForInsert physically relocates a
/// formula cell's Cell object to its new (shifted) address, leaving the OLD (pre-shift) address
/// blank -- but CommandOutcome.AffectedCells only ever reported the NEW address (via
/// RelocatedFormulaCellsPendingDependencyRefresh / _formulaSnapshot). The real-product pipeline,
/// WorkbookCellEditService.UpdateFormulaDependencies, drives RecalcEngine.RegisterFormulaDependencies
/// / ClearFormulaDependencies purely off AffectedCells: because the OLD address never appeared there,
/// ClearFormulaDependencies was never called for it, leaving a phantom precedent/dependent edge in
/// DependencyGraph keyed at an address that no longer holds any formula. This test drives the real
/// InsertRowsCommand and then replays the exact UpdateFormulaDependencies logic (the nearest
/// available seam -- that method lives in FreeX.App.Services, which cannot depend back on
/// FreeX.Integration.Tests) against a real DependencyGraph/RecalcEngine, mirroring the existing
/// R24_InsertRowsVolatileRelocationTests convention.
/// </summary>
public sealed class R98_InsertRowsDependencyVacatedAddressTests
{
    [Fact]
    public void InsertRowsRelocatesFormulaCell_PurgesStaleDependencyGraphEdgeAtVacatedOldAddress()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var graph = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        var engine = new RecalcEngine(graph, evaluator);
        var ctx = new TestCommandContext(workbook);

        // Precedent cell, well above the shifted band (_beforeRow = 5) so it is never itself moved.
        var precedent = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(precedent, Cell.FromValue(new NumberValue(42)));

        // Formula cell at row 10 (>= beforeRow, so it relocates) whose formula references a cell
        // OUTSIDE the shifted band -- FormulaRewriter therefore leaves its formula TEXT untouched by
        // the row-insert rewrite, exactly the "text does not need rewriting" scenario the defect
        // describes.
        var oldAddr = new CellAddress(sheet.Id, 10, 1);
        sheet.SetFormula(oldAddr, "=$A$1*2");
        var ast = FormulaEvaluator.ParseFormula("=$A$1*2");
        engine.RegisterFormulaDependencies(oldAddr, ast, sheet.Id, workbook);

        // Sanity: the dependency graph has a real, non-empty precedent edge at the OLD address before
        // the insert -- this is the edge that must NOT survive as a phantom after the move.
        graph.GetDirectPrecedents(oldAddr).Should().Contain(precedent);

        var command = new InsertRowsCommand(sheet.Id, beforeRow: 5, count: 3);
        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue();

        var newAddr = new CellAddress(sheet.Id, 13, 1);
        sheet.GetCell(newAddr)!.FormulaText.Should().Be("=$A$1*2",
            "the formula text references a cell outside the shifted band and must be left untouched");
        sheet.GetCell(oldAddr).Should().BeNull("the row-10 cell physically relocated to row 13");

        // The fix under test: AffectedCells must report BOTH the new address (so RecalcEngine
        // re-registers the formula there) AND the old, now-vacated address (so RecalcEngine purges
        // the stale entry there).
        outcome.AffectedCells.Should().Contain(newAddr);
        outcome.AffectedCells.Should().Contain(oldAddr,
            "the vacated pre-shift address must be surfaced so the post-command pipeline purges its " +
            "now-stale dependency-graph entry");

        // Simulate WorkbookCellEditService.UpdateFormulaDependencies, the standard post-command
        // pipeline step that drives RecalcEngine purely off CommandOutcome.AffectedCells.
        foreach (var affected in outcome.AffectedCells!)
        {
            var cell = sheet.GetCell(affected);
            if (cell?.FormulaText is null)
            {
                engine.ClearFormulaDependencies(affected);
                continue;
            }

            engine.RegisterFormulaDependencies(
                affected, FormulaEvaluator.ParseFormula(cell.FormulaText), affected.Sheet, workbook);
        }

        // The core assertion: no phantom precedent edge survives at the vacated old address.
        graph.HasDependencies(oldAddr).Should().BeFalse(
            "the dependency graph must not retain a stale edge at an address that no longer holds a formula");
        graph.GetDirectPrecedents(oldAddr).Should().BeEmpty();

        // And the new address correctly picked up the live edge instead.
        graph.GetDirectPrecedents(newAddr).Should().Contain(precedent);
    }

    /// <summary>
    /// No-regression sibling: Undo (Revert) must be symmetric. The relocated formula cell moves back
    /// from its post-shift address to its original pre-shift address, so the post-shift address
    /// (which Undo just vacated) must also be purged from the dependency graph -- and the restored
    /// original address must be the one carrying the live edge, exactly mirroring the forward
    /// direction above.
    /// </summary>
    [Fact]
    public void UndoInsertRows_PurgesStaleDependencyGraphEdgeAtAddressVacatedByTheUndo()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var graph = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        var engine = new RecalcEngine(graph, evaluator);
        var ctx = new TestCommandContext(workbook);

        var precedent = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(precedent, Cell.FromValue(new NumberValue(42)));

        var originalAddr = new CellAddress(sheet.Id, 10, 1);
        sheet.SetFormula(originalAddr, "=$A$1*2");
        engine.RegisterFormulaDependencies(
            originalAddr, FormulaEvaluator.ParseFormula("=$A$1*2"), sheet.Id, workbook);

        var command = new InsertRowsCommand(sheet.Id, beforeRow: 5, count: 3);
        var applyOutcome = command.Apply(ctx);
        applyOutcome.Success.Should().BeTrue();

        var postShiftAddr = new CellAddress(sheet.Id, 13, 1);

        // Bring the dependency graph to the state the real post-Apply pipeline would leave it in.
        foreach (var affected in applyOutcome.AffectedCells!)
        {
            var cell = sheet.GetCell(affected);
            if (cell?.FormulaText is null)
                engine.ClearFormulaDependencies(affected);
            else
                engine.RegisterFormulaDependencies(
                    affected, FormulaEvaluator.ParseFormula(cell.FormulaText), affected.Sheet, workbook);
        }

        graph.GetDirectPrecedents(postShiftAddr).Should().Contain(precedent, "sanity: registered after Apply");

        command.Revert(ctx);

        sheet.GetCell(originalAddr)!.FormulaText.Should().Be("=$A$1*2");
        sheet.GetCell(postShiftAddr).Should().BeNull("the cell physically moved back to its original row");

        command.AffectedCells.Should().Contain(originalAddr);
        command.AffectedCells.Should().Contain(postShiftAddr,
            "the address vacated by the undo's move-back must be surfaced so the dependency graph's " +
            "stale entry there is purged");

        foreach (var affected in command.AffectedCells)
        {
            var cell = sheet.GetCell(affected);
            if (cell?.FormulaText is null)
                engine.ClearFormulaDependencies(affected);
            else
                engine.RegisterFormulaDependencies(
                    affected, FormulaEvaluator.ParseFormula(cell.FormulaText), affected.Sheet, workbook);
        }

        graph.HasDependencies(postShiftAddr).Should().BeFalse(
            "no stale edge should survive at the address the undo just vacated");
        graph.GetDirectPrecedents(originalAddr).Should().Contain(precedent);
    }
}
