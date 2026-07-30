using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Regression tests for R92-calc-iterative-convergence-5-2: RunIterativeCalc's per-cell
/// convergence check computed its delta purely via ToNumericForConvergence, which collapses
/// EVERY non-numeric value (Boolean/Text/Error/Blank) to the identical 0.0 sentinel. A cyclic cell
/// whose value keeps flipping every pass but is never numeric -- e.g. the Boolean toggle
/// A1=NOT(A1), the non-numeric analogue of the already-correctly-handled numeric oscillator
/// A1=-A1 -- therefore always reported delta=0 (TRUE and FALSE both map to 0.0), making the loop
/// declare false convergence and stop after exactly one iteration instead of running toward
/// MaxCalculationIterations like the numeric case does. Fixed by comparing the actual ScalarValue
/// on either side of a pass whenever either value isn't a finite number/date, treating an actual
/// change as a large-but-finite delta (so it behaves exactly like a genuine numeric change) and an
/// unchanged value as a true zero delta.
/// </summary>
public class R92_IterativeCalcNonNumericConvergenceTests
{
    private static (RecalcEngine engine, Workbook workbook, Sheet sheet) Setup(
        int maxIterations,
        double maxChange = 0.001)
    {
        var workbook = new Workbook();
        workbook.IterativeCalculation = true;
        workbook.MaxCalculationIterations = maxIterations;
        workbook.MaxCalculationChange = maxChange;
        var sheet = workbook.AddSheet("Sheet1");
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        return (engine, workbook, sheet);
    }

    [Fact]
    public void BooleanToggleSelfReference_RunsToMaxIterationsInsteadOfConvergingAfterOnePass()
    {
        // A1 = NOT(A1), seeded TRUE. Every single pass flips the value (TRUE/FALSE/TRUE/...), so it
        // never truly converges -- Excel-equivalent behaviour is to keep iterating all the way to
        // MaxCalculationIterations, exactly like the numeric oscillator A1=-A1 already does. With an
        // EVEN maxIterations count, the deterministic parity of "keeps flipping every pass" means the
        // final value must land back on the TRUE seed. The (buggy) premature-convergence behaviour
        // instead stops after exactly 1 flip, landing on FALSE regardless of maxIterations -- so this
        // assertion distinguishes the two deterministically, with no timing involved.
        const int maxIterations = 4;
        var (engine, wb, sheet) = Setup(maxIterations);
        var a1 = new CellAddress(sheet.Id, 1, 1);

        sheet.SetCell(a1, new BoolValue(true));
        sheet.SetFormula(a1, "NOT(A1)");

        var report = engine.RecalculateAllFormulas(wb);

        report.CyclicCells.Should().BeEmpty("iterative mode should not produce #CIRCULAR! cells");
        report.Errors.Should().NotContain(e => e.Error == "#CIRCULAR!");

        var finalValue = sheet.GetValue(a1.Row, a1.Col);
        finalValue.Should().BeOfType<BoolValue>("the last iterate must be a real Boolean, not an error");
        ((BoolValue)finalValue).Value.Should().BeTrue(
            $"A1=NOT(A1) seeded TRUE must flip every one of the {maxIterations} (even) passes and " +
            "land back on TRUE -- a false-convergence bug that stops after 1 flip would instead " +
            "leave it FALSE");
    }

    /// <summary>
    /// No-regression sibling: the pre-existing numeric two-cell cycle (A1=B1+1, B1=A1*0.5,
    /// converging to A1=2, B1=1) must still converge to the same fixed point through the refactored
    /// ComputeConvergenceDelta helper -- the finite-numeric fast path must behave identically to the
    /// original inline Math.Abs(newNumeric - prevNumeric) computation.
    /// </summary>
    [Fact]
    public void NumericTwoCellCycle_StillConvergesToFixedPointThroughRefactoredDelta()
    {
        var (engine, wb, sheet) = Setup(maxIterations: 1000, maxChange: 0.0001);
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);

        sheet.SetFormula(a1, "B1+1");
        sheet.SetFormula(b1, "A1*0.5");

        var report = engine.RecalculateAllFormulas(wb);

        report.CyclicCells.Should().BeEmpty("iterative mode should not produce #CIRCULAR! cells");

        var a1Val = ((NumberValue)sheet.GetValue(a1.Row, a1.Col)).Value;
        var b1Val = ((NumberValue)sheet.GetValue(b1.Row, b1.Col)).Value;
        a1Val.Should().BeApproximately(2.0, 0.01, "A1=B1+1 converges to 2");
        b1Val.Should().BeApproximately(1.0, 0.01, "B1=A1*0.5 converges to 1");
    }
}
