using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// R124-calc-indirect-iterative: a circular reference created dynamically through INDIRECT's
/// string argument (e.g. A1=INDIRECT("A1")+1, or the common INDIRECT(ADDRESS(ROW(),COLUMN()))
/// idiom -- see R86_IndirectSelfReferenceCircularTests) has no static edge in the dependency
/// graph, so it never appears in plan.CyclicCells and is never routed through RunIterativeCalc,
/// the fixed-point iteration loop used for statically-detected cycles (see IterativeCalcTests).
/// Before this fix, RecalcEngine.cs's sole consumer of INDIRECT's RuntimeCircularSelfReference
/// sentinel unconditionally called AddCyclicCell -- the NON-iterative handler -- even when
/// Workbook.IterativeCalculation was on, so this class of circular reference was permanently
/// stuck at 0 and permanently flagged "#CIRCULAR!" no matter how Max Iterations/Max Change were
/// configured. In real Excel, INDIRECT-based self-references iterate exactly like a direct
/// A1=A1+1 self-loop under Iterative Calculation.
/// </summary>
public sealed class R124_IndirectSelfReferenceIterativeCalcTests
{
    private static (RecalcEngine engine, Workbook workbook, Sheet sheet) Setup(
        bool iterative, int? maxIterations = null, double? maxChange = null)
    {
        var workbook = new Workbook("Test");
        workbook.IterativeCalculation = iterative;
        if (maxIterations.HasValue) workbook.MaxCalculationIterations = maxIterations.Value;
        if (maxChange.HasValue) workbook.MaxCalculationChange = maxChange.Value;
        var sheet = workbook.AddSheet("Sheet1");
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        return (engine, workbook, sheet);
    }

    /// <summary>
    /// THE BUG: A1=INDIRECT("A1")+1 diverges (no fixed point) exactly like the direct A1=A1+1
    /// self-loop covered by IterativeCalcTests.IterativeCalc_DivergentCycle_TerminatesAfterMaxIterations.
    /// With Iterative Calculation ON, it must terminate after MaxCalculationIterations passes at
    /// the same converged/last-iterate value the direct self-loop reaches (maxIter, starting from
    /// 0) -- NOT be permanently pinned at 0 with "#CIRCULAR!" recorded on every recalc.
    /// </summary>
    [Fact]
    public void IndirectSelfReference_DivergentCycle_IterativeCalcOn_TerminatesAtMaxIterationsValue()
    {
        const int maxIter = 10;
        var (engine, wb, sheet) = Setup(iterative: true, maxIterations: maxIter, maxChange: 0.0);
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetFormula(a1, "INDIRECT(\"A1\")+1");

        var report = engine.RecalculateAllFormulas(wb);

        report.CyclicCells.Should().BeEmpty("iterative mode should not produce #CIRCULAR! cells, mirroring the direct self-loop case");
        report.Errors.Should().NotContain(e => e.Error == "#CIRCULAR!");
        engine.CyclicCells.Should().BeEmpty();

        var cellVal = sheet.GetValue(a1.Row, a1.Col);
        cellVal.Should().BeOfType<NumberValue>("last iterate must be a converged number, not an error or a fabricated 0");
        var numericVal = ((NumberValue)cellVal).Value;
        double.IsFinite(numericVal).Should().BeTrue();
        numericVal.Should().BeApproximately(maxIter, 0.5,
            $"INDIRECT(\"A1\")+1 starting at 0 reaches {maxIter} after {maxIter} passes, exactly like the direct A1=A1+1 self-loop");
    }

    /// <summary>
    /// Convergent sibling: A1=INDIRECT("A1")/2+1 has fixed point A1=2 (2 = 2/2+1), the same shape
    /// IterativeCalcTests.IterativeCalc_SelfReference_ConvergesToFixedPoint already proves for the
    /// direct A1=A1/2+1 form. Confirms the fix isn't merely bounding iteration count but actually
    /// performs genuine fixed-point convergence for the INDIRECT-routed case too.
    /// </summary>
    [Fact]
    public void IndirectSelfReference_ConvergentCycle_IterativeCalcOn_ConvergesToFixedPoint()
    {
        var (engine, wb, sheet) = Setup(iterative: true, maxIterations: 500, maxChange: 0.0001);
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetFormula(a1, "INDIRECT(\"A1\")/2+1");

        var report = engine.RecalculateAllFormulas(wb);

        report.CyclicCells.Should().BeEmpty();
        report.Errors.Should().NotContain(e => e.Error == "#CIRCULAR!");
        var val = ((NumberValue)sheet.GetValue(a1.Row, a1.Col)).Value;
        val.Should().BeApproximately(2.0, 0.01, "A1=INDIRECT(\"A1\")/2+1 converges to 2, same as the direct form");
    }

    /// <summary>
    /// Sibling idiom: INDIRECT(ADDRESS(ROW(),COLUMN())) (the common self-reference idiom, also
    /// covered non-iteratively by R86) must be routed through iterative calc the same way as the
    /// plain string-literal INDIRECT("A1") form.
    /// </summary>
    [Fact]
    public void IndirectAddressIdiomSelfReference_IterativeCalcOn_Converges()
    {
        var (engine, wb, sheet) = Setup(iterative: true, maxIterations: 500, maxChange: 0.0001);
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetFormula(a1, "INDIRECT(ADDRESS(ROW(),COLUMN()))/2+1");

        var report = engine.RecalculateAllFormulas(wb);

        report.CyclicCells.Should().BeEmpty();
        var val = ((NumberValue)sheet.GetValue(a1.Row, a1.Col)).Value;
        val.Should().BeApproximately(2.0, 0.01);
    }

    /// <summary>
    /// A second recalc pass (bare F9-equivalent, no changed cells) must keep re-iterating from the
    /// converged value rather than resetting or drifting, mirroring
    /// IterativeCalcTests.IterativeCalc_TwoCellCycle_ConvergesToFixedPoint's stability expectation
    /// and R86's "stays stable across a second recalc" check for the non-iterative case.
    /// </summary>
    [Fact]
    public void IndirectSelfReference_IterativeCalcOn_StableAcrossSecondRecalc()
    {
        var (engine, wb, sheet) = Setup(iterative: true, maxIterations: 500, maxChange: 0.0001);
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetFormula(a1, "INDIRECT(\"A1\")/2+1");

        engine.RecalculateAllFormulas(wb);
        var firstVal = ((NumberValue)sheet.GetValue(a1.Row, a1.Col)).Value;

        var secondReport = engine.Recalculate(wb, []);
        var secondVal = ((NumberValue)sheet.GetValue(a1.Row, a1.Col)).Value;

        secondReport.CyclicCells.Should().BeEmpty();
        secondVal.Should().BeApproximately(firstVal, 0.01, "already converged, so a second pass with nothing changed must stay stable");
    }

    /// <summary>
    /// NO-REGRESSION: the pre-existing NON-iterative behaviour (R86) must be completely unaffected
    /// by this fix -- IterativeCalculation defaults to false, so A1=INDIRECT("A1")+1 must still
    /// seed to 0 and be flagged "#CIRCULAR!" exactly as before.
    /// </summary>
    [Fact]
    public void IndirectSelfReference_IterativeCalcOff_StillSeedsZeroAndFlagsCircular()
    {
        var (engine, wb, sheet) = Setup(iterative: false);
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetFormula(a1, "INDIRECT(\"A1\")+1");

        var report = engine.RecalculateAllFormulas(wb);

        report.CyclicCells.Should().Contain(a1);
        engine.CyclicCells.Should().Contain(a1);
        sheet.GetValue(a1.Row, a1.Col).Should().Be(new NumberValue(0));
        report.Errors.Should().Contain(e => e.Cell.Equals(a1) && e.Error == "#CIRCULAR!");
    }

    /// <summary>
    /// NO-REGRESSION: a plain (non-INDIRECT) direct self-loop A1=A1+1 under Iterative Calculation
    /// must still converge via the pre-existing static-graph RunIterativeCalc routing, unaffected
    /// by adding the new INDIRECT-sentinel routing alongside it.
    /// </summary>
    [Fact]
    public void PlainDirectSelfLoop_IterativeCalcOn_StillConvergesNormally()
    {
        const int maxIter = 10;
        var (engine, wb, sheet) = Setup(iterative: true, maxIterations: maxIter, maxChange: 0.0);
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetFormula(a1, "A1+1");

        var report = engine.RecalculateAllFormulas(wb);

        report.CyclicCells.Should().BeEmpty();
        var numericVal = ((NumberValue)sheet.GetValue(a1.Row, a1.Col)).Value;
        numericVal.Should().BeApproximately(maxIter, 0.5);
    }

    /// <summary>
    /// NO-REGRESSION: INDIRECT referencing a DIFFERENT cell (not a self-reference) must keep
    /// evaluating normally under Iterative Calculation, not get swept into the new routing just
    /// because IterativeCalculation happens to be on.
    /// </summary>
    [Fact]
    public void IndirectReferencingAnotherCell_IterativeCalcOn_StillEvaluatesNormally()
    {
        var (engine, wb, sheet) = Setup(iterative: true);
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetFormula(a1, "5");
        sheet.SetFormula(b1, "INDIRECT(\"A1\")+1");

        var report = engine.RecalculateAllFormulas(wb);

        report.CyclicCells.Should().BeEmpty();
        sheet.GetValue(b1.Row, b1.Col).Should().Be(new NumberValue(6));
    }
}
