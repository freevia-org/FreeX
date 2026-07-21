using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Regression tests for R61-services-recalc-dependency-6-1: RegisterFormulaDependencies' dedicated
/// ANCHORARRAY (A1#:End) case only unioned the full spill-extent rectangle when the anchor argument
/// was a bare same-sheet CellRefNode. A NAMED-RANGE anchor (e.g. Anchor#:C5 where "Anchor" is a
/// workbook-scoped name pointing at a single cell) or a CROSS-SHEET CellRefNode anchor (e.g.
/// Sheet1!A1#:C5 written from another sheet) fell through to the generic per-argument
/// CollectReferences case, which registered only two disjoint exact-cell precedents (the anchor
/// cell/named-range target and the bare end cell) instead of the union rectangle -- so a plain cell
/// strictly inside the rectangle (but not the anchor or end cell) had NO dependency edge at all, and
/// editing it never recalculated the dependent formula. The fix (TryResolveAnchorForDependencies in
/// RecalcEngine.cs) resolves both anchor shapes to a concrete (sheet, row, col) address and reuses
/// the same GridRange union (with live-spill-extent adjustment) the bare-cell case always used.
/// </summary>
public class R61_AnchorArrayNonBareAnchorDependencyTests
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
    public void NamedRangeAnchor_InteriorCellOfUnionRectangle_Edit_RecalculatesDependentFormula()
    {
        // Sheet1: A1 = SEQUENCE(3) spills A1:A3 = {1,2,3}. "Anchor" is a workbook-global named range
        // pointing at the single cell A1. D1 = SUM(Anchor#:C5), whose union rectangle is A1:C5 (the
        // 3-row spill doesn't exceed the end cell's row 5); D1 itself sits in column D, outside that
        // rectangle. B3 sits inside the rectangle but is neither the anchor cell nor the end cell,
        // and is not part of A1's own spill.
        var (engine, wb, sheet) = MakeEngine();

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var d1 = new CellAddress(sheet.Id, 1, 4);
        var b3 = new CellAddress(sheet.Id, 3, 2); // inside A1:C5, not the anchor or end cell

        wb.DefineNamedRange("Anchor", new GridRange(a1, a1));

        sheet.SetFormula(a1, "=SEQUENCE(3)");
        sheet.SetFormula(d1, "=SUM(Anchor#:C5)");
        engine.RebuildFormulaDependencies(wb);
        engine.Recalculate(wb, [a1, d1]);

        sheet.GetValue(1, 4).Should().Be(new NumberValue(6), "initial SUM(Anchor#:C5) = 1+2+3 over the spill");

        // Real Excel recalculates D1 immediately when a plain cell inside the union rectangle
        // (B3) is edited, even though the anchor is a named range rather than a bare cell reference.
        sheet.SetCell(b3, new NumberValue(100));
        engine.Recalculate(wb, [b3]);

        sheet.GetValue(1, 4).Should().Be(new NumberValue(106),
            "B3 lies inside the Anchor#:C5 union rectangle, so editing it must immediately change D1's " +
            "SUM instead of leaving it stale -- the anchor being a NamedRangeNode must not degrade " +
            "dependency registration to two isolated exact-cell edges");
    }

    [Fact]
    public void CrossSheetCellAnchor_InteriorCellOfUnionRectangle_Edit_RecalculatesDependentFormula()
    {
        // Sheet2's B1 = SUM(Sheet1!A1#:C5) -- a cross-sheet CellRefNode anchor. Per
        // FormulaEvaluator.Functions.cs's EvaluateAnchorArray, the end cell (C5) is always parsed
        // unqualified relative to the ANCHOR's own sheet (Sheet1), not the formula's sheet (Sheet2).
        // Sheet1!B3 sits inside the A1:C5 union rectangle on Sheet1.
        var (engine, wb, sheet1) = MakeEngine();
        var sheet2 = wb.AddSheet("Sheet2");

        var a1 = new CellAddress(sheet1.Id, 1, 1);
        var b3 = new CellAddress(sheet1.Id, 3, 2); // inside Sheet1!A1:C5, not the anchor or end cell
        var b1OnSheet2 = new CellAddress(sheet2.Id, 1, 2);

        sheet1.SetFormula(a1, "=SEQUENCE(3)");
        sheet2.SetFormula(b1OnSheet2, "=SUM(Sheet1!A1#:C5)");
        engine.RebuildFormulaDependencies(wb);
        engine.Recalculate(wb, [a1, b1OnSheet2]);

        sheet2.GetValue(1, 2).Should().Be(new NumberValue(6), "initial SUM(Sheet1!A1#:C5) = 1+2+3 over the spill");

        sheet1.SetCell(b3, new NumberValue(1000));
        engine.Recalculate(wb, [b3]);

        sheet2.GetValue(1, 2).Should().Be(new NumberValue(1006),
            "Sheet1!B3 lies inside the cross-sheet Sheet1!A1#:C5 union rectangle, so editing it must " +
            "immediately change Sheet2!B1's SUM -- a cross-sheet CellRefNode anchor must not degrade " +
            "dependency registration to two isolated exact-cell edges");
    }

    [Fact]
    public void NamedRangeAnchor_CellOutsideUnionRectangle_Edit_DoesNotAffectDependentFormula()
    {
        // Sibling no-overreach guard: a cell just outside the Anchor#:C5 (= A1:C5) union rectangle
        // (E5, one column past the end cell) must NOT be wired as a dependency of D1 -- the fix must
        // register exactly the bounding rectangle, not an entire row/column/sheet, even when the
        // anchor is a named range.
        var (engine, wb, sheet) = MakeEngine();

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var d1 = new CellAddress(sheet.Id, 1, 4);
        var e5 = new CellAddress(sheet.Id, 5, 5); // just outside the A1:C5 rectangle

        wb.DefineNamedRange("Anchor", new GridRange(a1, a1));

        sheet.SetFormula(a1, "=SEQUENCE(3)");
        sheet.SetCell(e5, new NumberValue(0));
        sheet.SetFormula(d1, "=SUM(Anchor#:C5)");
        engine.RebuildFormulaDependencies(wb);

        engine.Recalculate(wb, [a1, d1, e5]);
        sheet.GetValue(1, 4).Should().Be(new NumberValue(6));

        sheet.SetCell(e5, new NumberValue(999));
        engine.Recalculate(wb, [e5]);

        sheet.GetValue(1, 4).Should().Be(new NumberValue(6),
            "E5 lies outside the Anchor#:C5 union rectangle, so editing it must not change D1's SUM");
    }
}
