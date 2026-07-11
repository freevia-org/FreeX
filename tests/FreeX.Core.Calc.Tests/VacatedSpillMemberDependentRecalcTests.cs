using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Regression test for R22-calc-engine-dependency-1: shrinking or clearing a dynamic-array spill
/// left formulas that directly reference the vacated spill-member cells (not the spill anchor
/// itself) permanently stale, because <c>Sheet.ClearSpillRange</c> removes those cells from the
/// spill-value table *before* <c>ResolveSpillTargetDependentsFixpoint</c> runs its follow-up scan,
/// and that scan only discovers dependents by enumerating cells still present in the spill-value
/// table (<c>Sheet.EnumerateSpillTargetCells</c>). A cell like B1 = A2+1 has a real
/// dependency-graph edge to A2, but that edge is never walked by the normal recalc BFS from the
/// changed root (D1) — only the spill follow-up pass discovers spill-target readers, and that pass
/// could no longer find A2 once it was vacated. RecalcEngine now captures the set of cells about
/// to be vacated (before/around the clearing calls) and feeds them into the follow-up fixpoint so
/// their direct dependents get one more evaluation, exactly matching real Excel.
/// </summary>
public class VacatedSpillMemberDependentRecalcTests
{
    private static (RecalcEngine engine, Workbook wb) MakeEngine()
    {
        var graph = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        var engine = new RecalcEngine(graph, evaluator);
        var wb = new Workbook();
        return (engine, wb);
    }

    [Fact]
    public void SpillCollapsingToScalar_DirectReferenceToVacatedMember_RecalculatesImmediately()
    {
        // D1 = 1; A1 = IF(D1>0,SEQUENCE(3),99) spills A1:A3 = {1,2,3} while D1>0.
        // B1 = A2+1 directly references a spill member (not the anchor), so it has a real
        // dependency-graph edge to A2 and shows 2+1 = 3.
        var (engine, wb) = MakeEngine();
        var sheet = wb.AddSheet("Sheet1");

        var d1 = new CellAddress(sheet.Id, 1, 4);
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);

        sheet.SetCell(d1, new NumberValue(1));
        sheet.SetFormula(a1, "=IF(D1>0,SEQUENCE(3),99)");
        sheet.SetFormula(b1, "=A2+1");
        engine.RebuildFormulaDependencies(wb);

        engine.Recalculate(wb, [d1, a1]);

        sheet.GetValue(1, 1).Should().Be(new NumberValue(1));
        sheet.GetValue(2, 1).Should().Be(new NumberValue(2));
        sheet.GetValue(3, 1).Should().Be(new NumberValue(3));
        sheet.GetValue(1, 2).Should().Be(new NumberValue(3), "B1 = A2+1 = 2+1 while A1 is spilling");

        // User sets D1 = 0. A1 collapses to the scalar 99 and its spill (A2:A3) is cleared, so A2
        // is now blank (0). Real Excel immediately updates B1 to reflect that in the same recalc.
        sheet.SetCell(d1, new NumberValue(0));
        engine.Recalculate(wb, [d1]);

        sheet.GetValue(1, 1).Should().Be(new NumberValue(99));
        sheet.HasSpillValues.Should().BeFalse("A1's spill must be fully cleared once it collapses to a scalar");
        sheet.GetValue(1, 2).Should().Be(new NumberValue(1),
            "B1 = A2+1 must recompute using A2's now-blank value (0+1=1) instead of staying stale " +
            "at 3 from before the spill collapsed -- ClearSpillRange removes A2 from the spill-value " +
            "table before the follow-up scan runs, so B1's real graph edge to A2 must be driven " +
            "explicitly via the vacated-cell capture, not discovered by enumerating current spill targets");
    }

    [Fact]
    public void SpillShrinking_DirectReferenceToNoLongerSpilledRow_RecalculatesImmediately()
    {
        // A1 = SEQUENCE(D1) spills A1:A(D1). B1 = A3+1 directly references a row that is only a
        // spill member while D1 >= 3.
        var (engine, wb) = MakeEngine();
        var sheet = wb.AddSheet("Sheet1");

        var d1 = new CellAddress(sheet.Id, 1, 4);
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);

        sheet.SetCell(d1, new NumberValue(5));
        sheet.SetFormula(a1, "=SEQUENCE(D1)");
        sheet.SetFormula(b1, "=A3+1");
        engine.RebuildFormulaDependencies(wb);

        engine.Recalculate(wb, [d1, a1]);

        sheet.GetValue(3, 1).Should().Be(new NumberValue(3));
        sheet.GetValue(1, 2).Should().Be(new NumberValue(4), "B1 = A3+1 = 3+1 while A1 spills 5 rows");

        // Shrink the spill to 2 rows: A3 is now vacated (no longer a spill member) and blank.
        sheet.SetCell(d1, new NumberValue(2));
        engine.Recalculate(wb, [d1]);

        sheet.GetValue(1, 1).Should().Be(new NumberValue(1));
        sheet.GetValue(2, 1).Should().Be(new NumberValue(2));
        sheet.TryGetSpillExtent(a1, out var rows, out _).Should().BeTrue();
        rows.Should().Be(2u, "A1's spill must shrink to exactly 2 rows");
        sheet.GetValue(1, 2).Should().Be(new NumberValue(1),
            "B1 = A3+1 must recompute using A3's now-blank value (0+1=1) instead of staying stale " +
            "at 4 from before the spill shrank past row 3");
    }
}
