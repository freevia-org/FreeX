using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Regression test for R29-formula-array-eval-deep-2: a A1#:D5 spill-range-union reference
/// (ANCHORARRAY(anchor, end) — see Parser.cs's ':' handling after a '#' spill anchor) reads every
/// cell in the bounding rectangle that unions the anchor's spill extent with the end cell
/// (FormulaEvaluator.Functions.cs EvaluateAnchorArray), not just the anchor and end cells
/// themselves. RecalcEngine.CollectReferences previously had no ANCHORARRAY-specific case, so the
/// generic FunctionCallNode branch only registered single-cell dependency edges on the anchor
/// (A1) and end (D5) arguments — any other cell inside the union rectangle (e.g. C3) had no edge
/// at all, so editing it never marked the dependent formula dirty and it kept a stale cached
/// value until an unrelated trigger (editing A1 or D5, or a full recalc) happened to re-run it.
/// </summary>
public class R29_AnchorArrayUnionRangeDependencyTests
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
    public void InteriorCellOfUnionRectangle_Edit_RecalculatesDependentFormula()
    {
        // A1 = SEQUENCE(2) spills A1:A2 = {1,2}. D5 = 100 (plain value). E1 = SUM(A1#:D5), whose
        // union rectangle is A1:D5 (rows 1-5, cols A-D). C3 sits inside that rectangle but is
        // neither the anchor (A1) nor the end cell (D5), and is not part of A1's own spill.
        var (engine, wb, sheet) = MakeEngine();

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var d5 = new CellAddress(sheet.Id, 5, 4);
        var c3 = new CellAddress(sheet.Id, 3, 3);
        var e1 = new CellAddress(sheet.Id, 1, 5);

        sheet.SetFormula(a1, "=SEQUENCE(2)");
        sheet.SetCell(d5, new NumberValue(100));
        sheet.SetFormula(e1, "=SUM(A1#:D5)");
        engine.RebuildFormulaDependencies(wb);

        engine.Recalculate(wb, [a1, d5, e1]);

        sheet.GetValue(1, 1).Should().Be(new NumberValue(1));
        sheet.GetValue(2, 1).Should().Be(new NumberValue(2));
        sheet.GetValue(1, 5).Should().Be(new NumberValue(103), "initial SUM(A1#:D5) = spill(1+2) + D5(100)");

        // Real Excel: editing C3 (an interior cell of the union rectangle) must immediately
        // change E1's SUM, since EvaluateAnchorArray reads every cell in A1:D5.
        sheet.SetCell(c3, new NumberValue(50));
        engine.Recalculate(wb, [c3]);

        sheet.GetValue(1, 5).Should().Be(new NumberValue(153),
            "E1 = SUM(A1#:D5) must include C3's new value immediately -- C3 is inside the union " +
            "rectangle even though it is neither the anchor nor the end cell");
    }

    [Fact]
    public void EndCellOfUnionRectangle_Edit_AlreadyRecalculatesDependentFormula()
    {
        // Sibling already-working case: editing the end cell (D5) itself must keep working exactly
        // as before this fix -- D5 always had its own single-cell dependency edge.
        var (engine, wb, sheet) = MakeEngine();

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var d5 = new CellAddress(sheet.Id, 5, 4);
        var e1 = new CellAddress(sheet.Id, 1, 5);

        sheet.SetFormula(a1, "=SEQUENCE(2)");
        sheet.SetCell(d5, new NumberValue(100));
        sheet.SetFormula(e1, "=SUM(A1#:D5)");
        engine.RebuildFormulaDependencies(wb);

        engine.Recalculate(wb, [a1, d5, e1]);
        sheet.GetValue(1, 5).Should().Be(new NumberValue(103));

        sheet.SetCell(d5, new NumberValue(200));
        engine.Recalculate(wb, [d5]);

        sheet.GetValue(1, 5).Should().Be(new NumberValue(203),
            "editing the end cell (D5) directly must still recalculate E1, as it did before this fix");
    }

    [Fact]
    public void CellOutsideUnionRectangle_Edit_DoesNotAffectDependentFormula()
    {
        // No-overreach guard: a cell just outside the A1:D5 union rectangle (E5, one column past
        // the end cell) must NOT be wired as a dependency of E1 -- the fix must register exactly
        // the bounding rectangle between the anchor and end cell, not an entire row/column/sheet.
        var (engine, wb, sheet) = MakeEngine();

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var d5 = new CellAddress(sheet.Id, 5, 4);
        var e5 = new CellAddress(sheet.Id, 5, 5); // just outside the A1:D5 rectangle
        var e1 = new CellAddress(sheet.Id, 1, 5);

        sheet.SetFormula(a1, "=SEQUENCE(2)");
        sheet.SetCell(d5, new NumberValue(100));
        sheet.SetCell(e5, new NumberValue(0));
        sheet.SetFormula(e1, "=SUM(A1#:D5)");
        engine.RebuildFormulaDependencies(wb);

        engine.Recalculate(wb, [a1, d5, e1, e5]);
        sheet.GetValue(1, 5).Should().Be(new NumberValue(103));

        sheet.SetCell(e5, new NumberValue(999));
        engine.Recalculate(wb, [e5]);

        sheet.GetValue(1, 5).Should().Be(new NumberValue(103),
            "E5 lies outside the A1:D5 union rectangle, so editing it must not change E1's SUM");
    }
}
