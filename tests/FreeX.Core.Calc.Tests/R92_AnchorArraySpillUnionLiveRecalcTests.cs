using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Regression tests for R92-calc-array-recalc-order-5-1: the A1#:B5 (2-arg ANCHORARRAY)
/// dependency edge R33 taught CollectReferences to derive from the anchor's LIVE spill extent was
/// still only ever (re-)computed at REGISTRATION time — once per formula cell, per the CachedAst
/// gate in the main evaluation loop, or whenever something explicitly called
/// RebuildFormulaDependencies (F9 / Shift+F9 / workbook load). An ordinary per-cell edit
/// (Recalculate(wb, [cell])) never calls RebuildFormulaDependencies, so once the anchor's spill
/// extent changed size AFTER the dependent formula's dependencies were first registered, the
/// registered union rectangle went stale until some LATER unrelated full recalc happened to touch
/// it. R33's own tests never caught this because every one of them calls
/// RebuildFormulaDependencies between the growth and the follow-up edit. Fixed by having the main
/// evaluation loop re-register every dependent tracked against an anchor cell immediately after
/// that anchor cell finishes (re-)evaluating, so the graph edge tracks the anchor's current live
/// extent without needing any explicit rebuild.
/// </summary>
public class R92_AnchorArraySpillUnionLiveRecalcTests
{
    private static (RecalcEngine engine, Workbook wb, Sheet sheet) MakeEngine()
    {
        var graph = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        var engine = new RecalcEngine(graph, evaluator);
        var wb = new Workbook();
        var sheet = wb.AddSheet("Sheet1");
        return (engine, wb, sheet);
    }

    [Fact]
    public void GrowingAnchorSpill_NoRebuildBetweenGrowthAndEdit_DependentRecalculatesImmediately()
    {
        // A1 = SEQUENCE(3) spills A1:A3. D1 = SUM(A1#:B5), registered union = A1:B5 (the 3-row
        // spill doesn't exceed the end cell's row 5). B8 sits inside the union once A1 grows to a
        // 10-row spill (A1:B10), but outside the ORIGINAL literal A1:B5 rectangle.
        var (engine, wb, sheet) = MakeEngine();

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var d1 = new CellAddress(sheet.Id, 1, 4);
        var b8 = new CellAddress(sheet.Id, 8, 2);

        sheet.SetFormula(a1, "=SEQUENCE(3)");
        sheet.SetFormula(d1, "=SUM(A1#:B5)");
        engine.RebuildFormulaDependencies(wb);
        engine.Recalculate(wb, [a1, d1]);

        sheet.GetValue(1, 4).Should().Be(new NumberValue(6), "initial SUM(A1#:B5) = 1+2+3 over the 3-row spill");

        // Grow the anchor's spill via an ORDINARY edit -- deliberately NOT calling
        // RebuildFormulaDependencies here, matching the real per-cell-edit path (Shift+F9/F9 are
        // the only callers of RebuildFormulaDependencies; a plain edit never triggers one).
        sheet.SetFormula(a1, "=SEQUENCE(10)");
        engine.Recalculate(wb, [a1]);

        sheet.GetValue(1, 4).Should().Be(new NumberValue(55), "D1's value already follows A1's live extent");

        // Edit B8 -- a plain data cell (never itself spilled into) that lies inside the TRUE
        // current union (A1:B10) but outside the STALE registered A1:B5 rectangle, with NO
        // RebuildFormulaDependencies call anywhere in between.
        sheet.SetCell(b8, new NumberValue(1000));
        engine.Recalculate(wb, [b8]);

        sheet.GetValue(1, 4).Should().Be(new NumberValue(1055),
            "B8 lies inside the grown union (A1:B10); the anchor's own growth recalc must already " +
            "have re-derived D1's dependency edges from the new live extent, without requiring any " +
            "later explicit RebuildFormulaDependencies call");
    }

    [Fact]
    public void UnchangedAnchorExtent_PlainEditWithinLiteralRectangle_StillRecalculatesDependent()
    {
        // No-regression sibling: when the anchor's spill extent never changes size after
        // registration, an ordinary edit to an interior cell of the (unshrunk) literal rectangle
        // must keep recalculating the dependent exactly as before this fix -- with no
        // RebuildFormulaDependencies call anywhere, and no anchor resize to trigger the new
        // refresh path at all.
        var (engine, wb, sheet) = MakeEngine();

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var d1 = new CellAddress(sheet.Id, 1, 4);
        var b3 = new CellAddress(sheet.Id, 3, 2);

        sheet.SetFormula(a1, "=SEQUENCE(2)");
        sheet.SetCell(b3, new NumberValue(9));
        sheet.SetFormula(d1, "=SUM(A1#:B5)");
        engine.RebuildFormulaDependencies(wb);
        engine.Recalculate(wb, [a1, b3, d1]);

        sheet.GetValue(1, 4).Should().Be(new NumberValue(12), "initial SUM(A1#:B5) = spill(1+2) + B3(9)");

        sheet.SetCell(b3, new NumberValue(20));
        engine.Recalculate(wb, [b3]);

        sheet.GetValue(1, 4).Should().Be(new NumberValue(23),
            "an interior cell of an unchanged literal rectangle must keep recalculating the dependent");
    }
}
