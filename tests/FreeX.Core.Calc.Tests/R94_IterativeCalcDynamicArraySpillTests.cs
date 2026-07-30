using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Regression tests for the finding at RecalcEngine.cs:1032 (RunIterativeCalc): when a cell that is
/// part of an active iterative circular-reference group is itself a dynamic-array (spilling)
/// formula, the iterative-calc pass evaluated it with EvaluateSpilling but only ever kept the
/// top-left scalar (rv.Cells[0,0]) -- it never called Sheet.SetSpillRange/ClearSpillRange the way
/// the ordinary (non-cyclic) evaluation loop does a few hundred lines above (RecalcEngine.cs:363-424).
/// Consequently a dynamic-array formula's non-anchor spill members (e.g. A2/A3 for an anchor at A1)
/// went stale/blank the instant the formula became part of an active iterative cycle, with no
/// #SPILL! or other indication anything was wrong. Real Excel re-spills the whole array on every
/// iterative-calc pass, converging every member cell together with the anchor.
/// </summary>
public class R94_IterativeCalcDynamicArraySpillTests
{
    private static (RecalcEngine engine, Workbook workbook, Sheet sheet) Setup(
        int maxIterations = 20,
        double maxChange = 0.001)
    {
        var workbook = new Workbook();
        workbook.MaxCalculationIterations = maxIterations;
        workbook.MaxCalculationChange = maxChange;
        var sheet = workbook.AddSheet("Sheet1");
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        return (engine, workbook, sheet);
    }

    /// <summary>
    /// A1 = SEQUENCE(3,1,10) first spills normally (A1:A3 = 10,11,12). The formula is then edited to
    /// self-reference (A1 = SEQUENCE(3,1,A1)) -- a single-cell circular-reference group through a
    /// function argument, exactly the pattern the finding describes. Sheet.SetFormula immediately
    /// clears the old spill on the edited anchor (Sheet.cs SetFormula -> ClearSpillRange), so A2/A3
    /// go blank the moment the formula is edited; only a spill-reconciling iterative-calc pass can
    /// bring them back. The identity formula converges trivially on its first pass (A1's own scalar
    /// value never changes), which isolates the assertion to the spill-member reconciliation the
    /// finding is about, independent of the convergence loop itself.
    /// </summary>
    [Fact]
    public void SelfReferencingDynamicArray_RespillsMemberCellsUnderIterativeCalc()
    {
        var (engine, wb, sheet) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var a3 = new CellAddress(sheet.Id, 3, 1);

        // Establish a genuine natural spill first (non-cyclic), matching the finding's own
        // ground-truth scenario ("previously spilled to A1:A3 with values 1,2,3, then edited to
        // reference itself").
        sheet.SetFormula(a1, "SEQUENCE(3,1,10)");
        wb.IterativeCalculation = false;
        engine.RecalculateAllFormulas(wb);

        sheet.GetValue(a1.Row, a1.Col).Should().BeOfType<NumberValue>();
        ((NumberValue)sheet.GetValue(a1.Row, a1.Col)).Value.Should().Be(10);
        ((NumberValue)sheet.GetValue(a2.Row, a2.Col)).Value.Should().Be(11);
        ((NumberValue)sheet.GetValue(a3.Row, a3.Col)).Value.Should().Be(12);

        // Edit A1 to self-reference through a function argument and turn on iterative calculation.
        sheet.SetFormula(a1, "SEQUENCE(3,1,A1)");
        wb.IterativeCalculation = true;

        var report = engine.RecalculateAllFormulas(wb);

        report.CyclicCells.Should().BeEmpty("iterative mode resolves the cycle instead of flagging #CIRCULAR!");
        report.Errors.Should().NotContain(e => e.Error == "#CIRCULAR!");

        // The anchor's own scalar is an immediate fixed point (SEQUENCE(3,1,A1) reading A1=10
        // returns {10,11,12}, whose [0,0] is again 10), so this assertion holds under both the
        // buggy and fixed code -- the real assertion is on the spill MEMBER cells below.
        ((NumberValue)sheet.GetValue(a1.Row, a1.Col)).Value.Should().Be(10);

        // These are the cells the bug leaves stale/blank: RunIterativeCalc never called
        // SetSpillRange, so A2/A3 (cleared to blank by the SetFormula edit above) were never
        // repopulated.
        sheet.GetValue(a2.Row, a2.Col).Should().BeOfType<NumberValue>(
            "the fix must re-spill A2 from the converged array, not leave it blank");
        ((NumberValue)sheet.GetValue(a2.Row, a2.Col)).Value.Should().Be(11);
        sheet.GetValue(a3.Row, a3.Col).Should().BeOfType<NumberValue>(
            "the fix must re-spill A3 from the converged array, not leave it blank");
        ((NumberValue)sheet.GetValue(a3.Row, a3.Col)).Value.Should().Be(12);

        sheet.TryGetSpillExtent(a1, out var rows, out var cols).Should().BeTrue(
            "the sheet's spill table must reflect the anchor's converged array so downstream " +
            "readers (e.g. a formula referencing A2 directly) and the saver see a consistent spill");
        rows.Should().Be(3);
        cols.Should().Be(1);
    }

    /// <summary>
    /// No-regression sibling: an ordinary numeric circular reference with no dynamic-array formula
    /// anywhere in the cycle (A1=B1+1, B1=A1*0.5) must still converge to the same fixed point as
    /// before -- the new spill-reconciliation branch in RunIterativeCalc must be a no-op for
    /// non-array cyclic cells.
    /// </summary>
    [Fact]
    public void NumericTwoCellCycle_StillConvergesWithoutAnyDynamicArrayInvolved()
    {
        var (engine, wb, sheet) = Setup(maxIterations: 1000, maxChange: 0.0001);
        wb.IterativeCalculation = true;
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);

        sheet.SetFormula(a1, "B1+1");
        sheet.SetFormula(b1, "A1*0.5");

        var report = engine.RecalculateAllFormulas(wb);

        report.CyclicCells.Should().BeEmpty();
        ((NumberValue)sheet.GetValue(a1.Row, a1.Col)).Value.Should().BeApproximately(2.0, 0.001);
        ((NumberValue)sheet.GetValue(b1.Row, b1.Col)).Value.Should().BeApproximately(1.0, 0.001);
    }
}
