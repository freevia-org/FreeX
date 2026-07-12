using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Regression tests for R33-formula-array-spill-internals-2-1: the A1#:B5 (2-arg ANCHORARRAY)
/// dependency edge registered by RecalcEngine.CollectReferences used to be the LITERAL anchor..end
/// rectangle (new GridRange(anchorCell, endCell)) — but FormulaEvaluator.Functions.cs's
/// EvaluateAnchorArray actually reads the UNION of the anchor's live spill extent with the end
/// cell. So whenever the anchor's spill extent grows past the end cell (e.g. A1=SEQUENCE(3) growing
/// to SEQUENCE(10) while a formula reads A1#:B5), cells that fall inside the true (grown) union but
/// outside the literal anchor..end rectangle had no dependency edge at all — editing them never
/// marked the dependent formula dirty. The fix consults sheet.TryGetSpillExtent for the anchor (like
/// the evaluator does) and unions that with the end cell when registering the edge, and marks the
/// dependency plan non-cacheable so a later re-registration (RebuildFormulaDependencies) picks up
/// the anchor's current live extent instead of reusing a stale cached rectangle.
/// </summary>
public class R33_AnchorArraySpillGrowthDependencyTests
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
    public void GrowingAnchorSpill_AfterRebuild_DependentRecalculatesOnEditWithinNewExtent()
    {
        // A1 = SEQUENCE(3) spills A1:A3 = {1,2,3}. D1 = SUM(A1#:B5), whose union rectangle is
        // initially A1:B5 (rows 1-5, cols A-B) since the 3-row spill extent doesn't exceed the end
        // cell's row 5. B7 sits well outside that initial rectangle.
        var (engine, wb, sheet) = MakeEngine();

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var d1 = new CellAddress(sheet.Id, 1, 4);
        var b7 = new CellAddress(sheet.Id, 7, 2);

        sheet.SetFormula(a1, "=SEQUENCE(3)");
        sheet.SetFormula(d1, "=SUM(A1#:B5)");
        engine.RebuildFormulaDependencies(wb);
        engine.Recalculate(wb, [a1, d1]);

        sheet.GetValue(1, 4).Should().Be(new NumberValue(6), "initial SUM(A1#:B5) = 1+2+3 over the 3-row spill");

        // Grow the anchor's spill from 3 rows to 10 rows. EvaluateAnchorArray always reads the
        // live union, so D1's VALUE already reflects the larger extent on its own next recalc.
        sheet.SetFormula(a1, "=SEQUENCE(10)");
        engine.Recalculate(wb, [a1]);

        sheet.GetValue(1, 4).Should().Be(new NumberValue(55), "SUM(A1#:B5) must follow A1's grown 10-row spill (1+..+10=55)");

        // Real Excel: once the sheet's dependency graph is refreshed (e.g. Calculate Now / Shift+F9,
        // modeled here by RebuildFormulaDependencies), D1's registered edge must be re-derived from
        // A1's CURRENT (10-row) live extent, covering B7 -- which lies inside the grown union
        // (A1:B10) even though it was outside the original literal A1:B5 rectangle.
        engine.RebuildFormulaDependencies(wb);

        sheet.SetCell(b7, new NumberValue(1000));
        engine.Recalculate(wb, [b7]);

        sheet.GetValue(1, 4).Should().Be(new NumberValue(1055),
            "B7 lies inside the grown union rectangle (A1:B10) once dependencies are refreshed, so " +
            "editing it must immediately change D1's SUM, not stay stale until an unrelated recalc");
    }

    [Fact]
    public void AnchorExtentAlreadyExceedsEndCellAtRegistrationTime_UnionIncludesFullExtentImmediately()
    {
        // Sibling case exercising the CollectReferences fix directly (no later rebuild involved):
        // if the anchor has ALREADY spilled past the end cell's row by the time the dependent
        // formula's dependencies are first registered, the very first registration must include the
        // anchor's full live extent, not just the literal anchor..end rectangle.
        var (engine, wb, sheet) = MakeEngine();

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var d1 = new CellAddress(sheet.Id, 1, 4);
        var b8 = new CellAddress(sheet.Id, 8, 2); // inside the 10-row spill extent, outside literal A1:B5

        sheet.SetFormula(a1, "=SEQUENCE(10)");
        engine.RebuildFormulaDependencies(wb);
        engine.Recalculate(wb, [a1]);

        // A1 has now spilled 10 rows *before* D1's formula (and its dependencies) exist at all.
        sheet.SetFormula(d1, "=SUM(A1#:B5)");
        engine.RebuildFormulaDependencies(wb);
        engine.Recalculate(wb, [d1]);

        sheet.GetValue(1, 4).Should().Be(new NumberValue(55), "initial SUM(A1#:B5) must already follow the 10-row spill (1+..+10=55)");

        sheet.SetCell(b8, new NumberValue(1000));
        engine.Recalculate(wb, [b8]);

        sheet.GetValue(1, 4).Should().Be(new NumberValue(1055),
            "B8 is inside the anchor's live extent at registration time (A1:B10), so it must be wired " +
            "as a dependency on the very first registration, without needing any later rebuild");
    }

    [Fact]
    public void SmallExtent_InteriorCellOfLiteralRectangle_StillRecalculatesDependentFormula()
    {
        // Already-working sibling case (matches R29's original coverage): when the anchor's spill
        // extent does NOT exceed the end cell (the common case), the union rectangle collapses to
        // the literal anchor..end rectangle, and an interior cell of it must keep recalculating the
        // dependent formula exactly as before this fix.
        var (engine, wb, sheet) = MakeEngine();

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var d1 = new CellAddress(sheet.Id, 1, 4);
        var b3 = new CellAddress(sheet.Id, 3, 2); // inside literal A1:B5, not the anchor or end cell

        sheet.SetFormula(a1, "=SEQUENCE(2)");
        sheet.SetCell(b3, new NumberValue(9));
        sheet.SetFormula(d1, "=SUM(A1#:B5)");
        engine.RebuildFormulaDependencies(wb);
        engine.Recalculate(wb, [a1, b3, d1]);

        sheet.GetValue(1, 4).Should().Be(new NumberValue(12), "initial SUM(A1#:B5) = spill(1+2) + B3(9)");

        sheet.SetCell(b3, new NumberValue(20));
        engine.Recalculate(wb, [b3]);

        sheet.GetValue(1, 4).Should().Be(new NumberValue(23),
            "B3 is an interior cell of the (unshrunk) literal A1:B5 rectangle, so editing it must keep " +
            "recalculating D1 exactly as it did before this fix");
    }
}
