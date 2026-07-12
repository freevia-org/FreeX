using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Regression tests for R30-calc-iterative-circular-1: a cyclic cell that also contains a
/// volatile function (e.g. A1="=IFERROR(B1,0)+NOW()", B1="=A1") was first correctly marked
/// #CIRCULAR!, but a later volatile-driven recalc pass pulled A1 back into a restricted
/// re-evaluation whose dirty set excluded its cyclic partner B1 - making the cycle invisible
/// and silently clobbering #CIRCULAR! with a freshly-computed real number.
/// </summary>
public class VolatileCircularReferenceTests
{
    private static (RecalcEngine engine, Workbook workbook, Sheet sheet) Setup()
    {
        var workbook = new Workbook(); // IterativeCalculation defaults to false
        var sheet = workbook.AddSheet("Sheet1");
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        return (engine, workbook, sheet);
    }

    /// <summary>
    /// Bug case: a volatile function inside a cyclic formula must stay #CIRCULAR! across a
    /// subsequent volatile-only recalc pass (e.g. an idle "recalc with no changed cells" pass
    /// that only exists because a volatile cell is present), not get resurrected and clobbered
    /// with a real number.
    /// </summary>
    [Fact]
    public void NonIterative_VolatileCyclicCell_StaysCircular_AcrossVolatileOnlyPass()
    {
        var (engine, wb, sheet) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);

        sheet.SetFormula(a1, "IFERROR(B1,0)+NOW()");
        sheet.SetFormula(b1, "A1");

        // First full recalc: registers dependencies, discovers the A1<->B1 cycle, marks both
        // cells #CIRCULAR! (the volatile function in A1 does not change this).
        var report = engine.RecalculateAllFormulas(wb);
        report.CyclicCells.Should().NotBeEmpty("A1 and B1 form a circular reference");
        sheet.GetValue(a1.Row, a1.Col).Should().Be(ErrorValue.Circular);
        sheet.GetValue(b1.Row, b1.Col).Should().Be(ErrorValue.Circular);

        // Second recalc pass with an empty changed set: this pass only runs because A1 is
        // volatile. Before the fix, A1 was re-added to a restricted dirty set that did not
        // include B1, so the cycle was invisible, IFERROR(B1,0) silently swallowed the stale
        // #CIRCULAR! value on B1, and A1 was clobbered with a real NOW()-based number.
        engine.Recalculate(wb, []);

        sheet.GetValue(a1.Row, a1.Col).Should().Be(ErrorValue.Circular,
            "a volatile cell that is part of a circular reference must remain #CIRCULAR! " +
            "on subsequent volatile-driven recalc passes, not be resurrected and recomputed");
        sheet.GetValue(b1.Row, b1.Col).Should().Be(ErrorValue.Circular);
    }

    /// <summary>
    /// Sibling case: an unrelated volatile cell (not part of any cycle) must keep recalculating
    /// normally on the same volatile-driven pass that now excludes the cyclic cells.
    /// </summary>
    [Fact]
    public void NonIterative_UnrelatedVolatileCell_StillRecalculates_WhenCyclicCellsArePresent()
    {
        var (engine, wb, sheet) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var c1 = new CellAddress(sheet.Id, 1, 3);

        sheet.SetFormula(a1, "B1");
        sheet.SetFormula(b1, "A1");
        sheet.SetFormula(c1, "NOW()");

        engine.RecalculateAllFormulas(wb);
        sheet.GetValue(a1.Row, a1.Col).Should().Be(ErrorValue.Circular);
        var firstNow = sheet.GetCell(c1)!.Value;
        firstNow.Should().BeOfType<DateTimeValue>();

        Thread.Sleep(50);

        engine.Recalculate(wb, []);

        sheet.GetValue(a1.Row, a1.Col).Should().Be(ErrorValue.Circular,
            "the cycle must remain unaffected by the unrelated volatile cell's recalc");
        var secondNow = sheet.GetCell(c1)!.Value;
        secondNow.Should().BeOfType<DateTimeValue>("the unrelated volatile cell must still recalculate every pass");
    }

    /// <summary>
    /// Sibling case: a plain (non-volatile) circular reference must still be stamped #CIRCULAR!
    /// and stay that way - the fix must not weaken the existing non-volatile cycle handling.
    /// </summary>
    [Fact]
    public void NonIterative_PlainCyclicPair_StillStampsCircular_NoVolatileInvolved()
    {
        var (engine, wb, sheet) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);

        sheet.SetFormula(a1, "B1");
        sheet.SetFormula(b1, "A1");

        var report = engine.RecalculateAllFormulas(wb);

        report.CyclicCells.Should().NotBeEmpty();
        var a1Val = sheet.GetValue(a1.Row, a1.Col);
        var b1Val = sheet.GetValue(b1.Row, b1.Col);
        (a1Val == ErrorValue.Circular || b1Val == ErrorValue.Circular)
            .Should().BeTrue("at least one cell in the cycle must be stamped #CIRCULAR!");
    }
}
