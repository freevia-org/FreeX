using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Integration.Tests;

/// <summary>
/// R24-volatile-recalc-deep-3: InsertRowsCommand.MoveCellsForInsert relocates a formula cell's Cell
/// object to its new (shifted) address, but RowColumnShiftHelpers.RewriteAllFormulas only records an
/// address in _formulaSnapshot -- and therefore in CommandOutcome.AffectedCells -- when the formula
/// TEXT actually changes. A volatile 0-arg function like NOW()/RAND() has no cell references, so its
/// formula text never changes on a row shift; it was silently dropped from AffectedCells, so the
/// standard post-command pipeline (RecalcEngine.RegisterFormulaDependencies/ClearFormulaDependencies,
/// driven off AffectedCells) never re-registered it at its new address. Its volatile-cell tracking
/// entry at the old (now-blank) address went stale and no entry existed at the new one, freezing the
/// relocated NOW() cell until a full RebuildFormulaDependencies (F9).
/// </summary>
public sealed class R24_InsertRowsVolatileRelocationTests
{
    [Fact]
    public void InsertRowsAboveVolatileFormula_IncludesRelocatedCellInAffectedCellsAndKeepsItVolatile()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var graph = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        var engine = new RecalcEngine(graph, evaluator);
        var ctx = new TestCommandContext(workbook);

        var original = new CellAddress(sheet.Id, 10, 1);
        sheet.SetFormula(original, "NOW()");
        engine.RegisterFormulaDependencies(original, FormulaEvaluator.ParseFormula("NOW()"), sheet.Id, workbook);
        engine.Recalculate(workbook, [original]);

        // Insert 3 rows above row 5: A10's Cell object relocates to A13, but "NOW()" has no cell
        // references so its formula text is untouched by the row-shift rewrite.
        var command = new InsertRowsCommand(sheet.Id, beforeRow: 5, count: 3);
        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue();

        var relocated = new CellAddress(sheet.Id, 13, 1);
        sheet.GetCell(relocated)!.FormulaText.Should().Be("NOW()");

        // The fix under test: the relocated volatile-formula cell must be surfaced as an affected
        // cell so the app's post-command pipeline re-registers its dependencies/volatile tracking at
        // the new address.
        outcome.AffectedCells.Should().Contain(relocated,
            "a relocated formula whose text needed no rewrite must still be reported as affected " +
            "so RecalcEngine re-registers it (and its volatile tracking) at its new address");

        // Simulate WorkbookCellEditService.UpdateFormulaDependencies, the standard post-command
        // pipeline step that drives RecalcEngine off CommandOutcome.AffectedCells.
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

        // A subsequent Automatic-mode recalc pass with NO cells directly changed must still pick up
        // the relocated NOW() cell purely because it is tracked as volatile -- exactly like real
        // Excel, where NOW()/RAND()/etc. recompute on every calculation regardless of any prior
        // row/column shift.
        var report = engine.Recalculate(workbook, Array.Empty<CellAddress>());
        report.RecalculatedCells.Should().Contain(relocated,
            "the relocated NOW() cell must keep recalculating on every automatic pass, not freeze " +
            "at its pre-insert timestamp");
    }
}
