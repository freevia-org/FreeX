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
        // cells circular in the report (Excel-compatible: the cell VALUE seeds to 0, not a
        // fabricated error, so downstream arithmetic/IFERROR reads a real number).
        var report = engine.RecalculateAllFormulas(wb);
        report.CyclicCells.Should().NotBeEmpty("A1 and B1 form a circular reference");
        sheet.GetValue(a1.Row, a1.Col).Should().Be(new NumberValue(0));
        sheet.GetValue(b1.Row, b1.Col).Should().Be(new NumberValue(0));

        // Second recalc pass with an empty changed set: this pass only runs because A1 is
        // volatile. Before the original fix, A1 was re-added to a restricted dirty set that did
        // not include B1, so the cycle was invisible and A1 was clobbered with a real
        // NOW()-based number instead of staying seeded at 0.
        var secondReport = engine.Recalculate(wb, []);

        secondReport.CyclicCells.Should().NotBeEmpty(
            "a volatile cell that is part of a circular reference must remain classified circular " +
            "on subsequent volatile-driven recalc passes, not be resurrected and recomputed");
        sheet.GetValue(a1.Row, a1.Col).Should().Be(new NumberValue(0),
            "the cyclic cell must stay seeded at 0, not be resurrected into a real NOW()-based number");
        sheet.GetValue(b1.Row, b1.Col).Should().Be(new NumberValue(0));
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
        sheet.GetValue(a1.Row, a1.Col).Should().Be(new NumberValue(0));
        var firstNow = sheet.GetCell(c1)!.Value;
        firstNow.Should().BeOfType<DateTimeValue>();

        Thread.Sleep(50);

        engine.Recalculate(wb, []);

        sheet.GetValue(a1.Row, a1.Col).Should().Be(new NumberValue(0),
            "the cycle must remain unaffected by the unrelated volatile cell's recalc");
        var secondNow = sheet.GetCell(c1)!.Value;
        secondNow.Should().BeOfType<DateTimeValue>("the unrelated volatile cell must still recalculate every pass");
    }

    /// <summary>
    /// Sibling case: a plain (non-volatile) circular reference must still be classified circular
    /// in the report and seed to 0 - the fix must not weaken the existing non-volatile cycle
    /// detection, only the fabricated error VALUE it used to stamp onto the cell.
    /// </summary>
    [Fact]
    public void NonIterative_PlainCyclicPair_StillDetectedCircular_NoVolatileInvolved()
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
        var zero = new NumberValue(0);
        (a1Val == zero && b1Val == zero)
            .Should().BeTrue("Excel seeds every cell in a non-iterative circular reference to 0, not a fabricated error");
    }
}
