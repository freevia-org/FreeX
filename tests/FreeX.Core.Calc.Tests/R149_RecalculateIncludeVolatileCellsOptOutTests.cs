using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// R149-formula-volatility-manual-mode-fresh-formula-recalc: RecalcEngine.Recalculate unconditionally
/// folded every registered volatile cell (and its dependents) into every traversal's changed roots,
/// with no way for a caller to opt out. WorkbookCellEditService.RecalculateFreshlyEnteredFormulasOnce
/// -- the Manual-mode path that computes ONLY a just-typed/edited formula cell and is documented to
/// leave everything else in the workbook untouched until the next F9 -- called this same
/// Recalculate overload, so committing any brand-new formula anywhere silently re-rolled every
/// pre-existing volatile cell (RAND/NOW/TODAY/OFFSET/INDIRECT/...) in the whole workbook. The new
/// includeVolatileCells parameter (default true, preserving every other caller's existing
/// behaviour) lets that one call site opt out.
/// </summary>
public sealed class R149_RecalculateIncludeVolatileCellsOptOutTests
{
    private static RecalcEngine Engine() =>
        new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());

    [Fact]
    public void Recalculate_IncludeVolatileCellsFalse_DoesNotReevaluateUnrelatedVolatileCellsOrDependents()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var engine = Engine();

        var d1 = new CellAddress(sheet.Id, 1, 4); // D1 = RAND()
        var e1 = new CellAddress(sheet.Id, 1, 5); // E1 = D1+1
        var c1 = new CellAddress(sheet.Id, 1, 3); // C1 = 1+1 (freshly entered, unrelated)

        sheet.SetFormula(d1, "RAND()");
        sheet.SetFormula(e1, "D1+1");

        // Settle D1/E1 to a baseline value, matching a workbook that already had these formulas
        // evaluated before the user's next edit.
        engine.RecalculateAllFormulas(workbook);
        var d1Baseline = sheet.GetCell(d1)!.Value;
        var e1Baseline = sheet.GetCell(e1)!.Value;

        // Now enter a brand-new formula entirely unrelated to D1/E1, exactly like
        // RecalculateFreshlyEnteredFormulasOnce does for a Manual-mode formula commit.
        sheet.SetFormula(c1, "1+1");
        var report = engine.Recalculate(workbook, [c1], includeVolatileCells: false);

        report.RecalculatedCells.Should().Contain(c1, "the freshly entered formula must still compute once");
        report.RecalculatedCells.Should().NotContain(d1,
            "opting out of volatile cells must stop an unrelated formula entry from re-rolling a pre-existing volatile cell");
        report.RecalculatedCells.Should().NotContain(e1,
            "a volatile cell's dependent must not be swept in either when volatile cells are excluded");
        sheet.GetCell(d1)!.Value.Should().Be(d1Baseline, "D1 must keep its previously-rolled value, not re-roll");
        sheet.GetCell(e1)!.Value.Should().Be(e1Baseline, "E1 must stay stale, matching D1 staying stale");
    }

    // No-regression sibling: every OTHER caller (the default includeVolatileCells: true) must keep
    // folding volatile cells into the traversal exactly as before -- this opt-out must not leak into
    // Automatic-mode edits, F9, Shift+F9, Goal Seek, or Undo/Redo.
    [Fact]
    public void Recalculate_DefaultIncludeVolatileCellsTrue_StillReevaluatesVolatileCellsAndDependents()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var engine = Engine();

        var d1 = new CellAddress(sheet.Id, 1, 4); // D1 = RAND()
        var e1 = new CellAddress(sheet.Id, 1, 5); // E1 = D1+1
        var c1 = new CellAddress(sheet.Id, 1, 3); // C1 = 1+1 (freshly entered, unrelated)

        sheet.SetFormula(d1, "RAND()");
        sheet.SetFormula(e1, "D1+1");
        engine.RecalculateAllFormulas(workbook);

        sheet.SetFormula(c1, "1+1");
        var report = engine.Recalculate(workbook, [c1]);

        report.RecalculatedCells.Should().Contain(c1);
        report.RecalculatedCells.Should().Contain(d1,
            "the default (includeVolatileCells: true) scope must keep re-rolling volatile cells, matching every existing caller's behaviour");
        report.RecalculatedCells.Should().Contain(e1,
            "a volatile cell's dependent must still recompute in the default scope");
    }
}
