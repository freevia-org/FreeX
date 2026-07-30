using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// R92-calc-iterative-convergence-5-1: plain F9 "Calculate Now" (RecalcEngine.Recalculate with an
/// EMPTY changedCells list -- see MainWindow.WorkbookUiState.RecalculateDirtyCells, the App.Host
/// F9 handler) must still re-run an ACTIVE iterative circular-reference group, exactly like real
/// Excel re-iterates the classic "A1=A1+1, Max Iterations=1" counter idiom on every calculation
/// pass. Before the fix, Recalculate's very first guard (changedCells.Count==0 &amp;&amp;
/// _volatileCells.Count==0) returned an EmptyReport immediately whenever nothing else in the
/// workbook was dirty, so RunIterativeCalc was never invoked and the accumulator was stuck at its
/// first-pass value forever.
/// </summary>
public class R92_IterativeCalcFNineReiterationTests
{
    private static (RecalcEngine engine, Workbook workbook, Sheet sheet) Setup(
        bool iterative = true,
        int? maxIterations = 1,
        double? maxChange = 0.0)
    {
        var workbook = new Workbook();
        workbook.IterativeCalculation = iterative;
        if (maxIterations.HasValue) workbook.MaxCalculationIterations = maxIterations.Value;
        if (maxChange.HasValue) workbook.MaxCalculationChange = maxChange.Value;
        var sheet = workbook.AddSheet("Sheet1");
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        return (engine, workbook, sheet);
    }

    private static double NumberAt(Sheet sheet, CellAddress addr) =>
        ((NumberValue)sheet.GetValue(addr.Row, addr.Col)).Value;

    /// <summary>
    /// THE FIX: a bare F9 (Recalculate with an empty changedCells list) must advance the classic
    /// "Max Iterations = 1" accumulator idiom by one more step every time it is pressed, just like
    /// the initial formula-entry edit (which supplies its own changedCells) already does.
    /// </summary>
    [Fact]
    public void Recalculate_EmptyChangedCells_ReRunsActiveIterativeCycle_AdvancesAccumulator()
    {
        var (engine, wb, sheet) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetFormula(a1, "A1+1");

        // The formula-entry edit itself drives the first iterative pass via its own changedCells
        // (matching MainWindow's CommitEdit -> RecalculateIfAutomatic path).
        engine.Recalculate(wb, [a1]);
        NumberAt(sheet, a1).Should().BeApproximately(1.0, 0.0001, "the entry edit runs one pass");

        // F9 "Calculate Now" passes an EMPTY changedCells list (RecalculateDirtyCells). Real Excel
        // still re-runs the active iterative circular group on every such pass.
        engine.Recalculate(wb, []);
        NumberAt(sheet, a1).Should().BeApproximately(2.0, 0.0001,
            "F9 with no other dirty cells must still advance an active iterative accumulator");

        engine.Recalculate(wb, []);
        NumberAt(sheet, a1).Should().BeApproximately(3.0, 0.0001,
            "each subsequent F9 press advances the accumulator by one more iteration");

        engine.Recalculate(wb, []);
        NumberAt(sheet, a1).Should().BeApproximately(4.0, 0.0001,
            "the fourth consecutive bare F9 still advances the counter");
    }

    /// <summary>
    /// If iterative calculation is turned back OFF, a later bare F9 must re-mark the still-circular
    /// formula as a genuine circular reference (Excel's non-iterative behavior) instead of silently
    /// continuing to reiterate it forever -- the tracked active-cycle seed must age out the moment
    /// the fresh traversal reports it via the non-iterative path rather than RunIterativeCalc.
    /// </summary>
    [Fact]
    public void Recalculate_AfterIterativeCalculationTurnedOff_EmptyChangedCells_MarksCircularInsteadOfReiterating()
    {
        var (engine, wb, sheet) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetFormula(a1, "A1+1");

        engine.Recalculate(wb, [a1]);
        NumberAt(sheet, a1).Should().BeApproximately(1.0, 0.0001);

        engine.Recalculate(wb, []);
        NumberAt(sheet, a1).Should().BeApproximately(2.0, 0.0001, "still iterating while the option is on");

        // Turn iterative calculation off -- Excel would now flag this as a genuine circular
        // reference rather than keep silently reiterating it.
        wb.IterativeCalculation = false;
        var report = engine.Recalculate(wb, []);

        report.CyclicCells.Should().Contain(a1,
            "with iterative calc off, a bare F9 must re-mark the still-circular formula instead of " +
            "continuing to reiterate it forever via the stale active-cycle seed");
    }

    /// <summary>
    /// No-regression sibling: a bare F9 in a workbook with NO iterative calculation active at all
    /// (the overwhelmingly common case) must remain a true no-op, exactly as before this fix.
    /// </summary>
    [Fact]
    public void Recalculate_EmptyChangedCells_NoActiveCycle_RemainsNoOp()
    {
        var (engine, wb, sheet) = Setup(iterative: true, maxIterations: null, maxChange: null);
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, new NumberValue(5));
        sheet.SetFormula(b1, "A1+10");

        // Ordinary (non-cyclic) edit path.
        engine.Recalculate(wb, [b1]);
        NumberAt(sheet, b1).Should().Be(15);

        var report = engine.Recalculate(wb, []);
        report.RecalculatedCells.Should().BeEmpty();
        report.CyclicCells.Should().BeEmpty();
        report.Errors.Should().BeEmpty();
        NumberAt(sheet, b1).Should().Be(15, "a bare F9 with nothing dirty and no active cycle must not recalculate anything");
    }
}
