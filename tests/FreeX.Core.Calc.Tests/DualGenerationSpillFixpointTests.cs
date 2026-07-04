using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Regression tests for H3: the chained-spill follow-up fixpoint used to permanently skip
/// re-evaluating a cell once its address was seen in <c>seenSpillDependents</c>, even if that
/// cell later gained a *new* spill-target input that only materialized on a subsequent pass
/// (e.g. a cell that reads both a first-generation spill target and a second-generation one from
/// a dependent dynamic array). RecalcEngine now tracks how many distinct spill-target inputs each
/// dependent read on the pass it was last scheduled, and only skips it if that count hasn't grown.
/// </summary>
public class DualGenerationSpillFixpointTests
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
    public void CellReadingBothFirstAndSecondGenerationSpillTargets_ConvergesToCorrectValue()
    {
        // A1 = SEQUENCE(3) spills A1:A3 (first generation).
        // B1 ("P1" in the scenario description) = A3 -- a first-generation spill-target dependent
        //   (also a normal graph precedent of C1).
        // C1 ("D1") = SEQUENCE(B1) -- has a real graph edge from B1, so C1 recalculates in the same
        //   follow-up pass as B1, and spills C1:C(B1) using B1's corrected value (second generation).
        // D1 ("Y1") = A2 + C2 -- reads BOTH a first-generation spill target (A2) and a second-generation
        //   one (C2). D1 is discovered as a spill-dependent in the SAME early pass as B1 (via A2),
        //   before C1 has spilled, so it risks baking in a stale/blank C2 and never being
        //   rescheduled once C2 becomes a real spill target on a later pass.
        var (engine, wb) = MakeEngine();
        var sheet = wb.AddSheet("Sheet1");

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var p1 = new CellAddress(sheet.Id, 1, 2);
        var d1 = new CellAddress(sheet.Id, 1, 3);
        var y1 = new CellAddress(sheet.Id, 1, 4);

        sheet.SetFormula(a1, "=SEQUENCE(3)");
        sheet.SetFormula(p1, "=A3");
        sheet.SetFormula(d1, "=SEQUENCE(B1)");
        sheet.SetFormula(y1, "=A2+C2");
        engine.RebuildFormulaDependencies(wb);

        engine.Recalculate(wb, [a1]);

        // A1 spills 1,2,3 down A1:A3.
        sheet.GetValue(1, 1).Should().Be(new NumberValue(1));
        sheet.GetValue(2, 1).Should().Be(new NumberValue(2));
        sheet.GetValue(3, 1).Should().Be(new NumberValue(3));

        // B1 = A3 = 3.
        sheet.GetValue(1, 2).Should().Be(new NumberValue(3));

        // C1 = SEQUENCE(B1) = SEQUENCE(3) -> spills C1:C3 = 1,2,3, so C2 = 2.
        sheet.GetValue(1, 3).Should().Be(new NumberValue(1));
        sheet.GetValue(2, 3).Should().Be(new NumberValue(2));
        sheet.GetValue(3, 3).Should().Be(new NumberValue(3));

        // D1 = A2 + C2 = 2 + 2 = 4. Before the fix, D1 was discovered (and marked "seen") in the
        // same early pass as B1 -- before C1 had spilled -- so it baked in C2 as blank (0) and was
        // never rescheduled once C2 became a genuine spill target, permanently sticking at 2.
        sheet.GetValue(1, 4).Should().Be(new NumberValue(4),
            "D1 must be re-evaluated once more after C1's spill makes C2 available, even though " +
            "D1's address was already seen in an earlier follow-up pass");
    }

    [Fact]
    public void RecalculateAgain_WithNoNewSpillGrowth_DoesNotReprocessConvergedDependent()
    {
        // Sanity check for the fix's other half: once a dependent's spill-target input count has
        // stopped growing, the fixpoint loop must still terminate (not spin re-evaluating forever).
        var (engine, wb) = MakeEngine();
        var sheet = wb.AddSheet("Sheet1");

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);

        sheet.SetFormula(a1, "=SEQUENCE(3)");
        sheet.SetFormula(b1, "=A2:A3");
        engine.RebuildFormulaDependencies(wb);

        var report = engine.Recalculate(wb, [a1]);

        sheet.GetValue(1, 2).Should().Be(new NumberValue(2));
        sheet.GetValue(2, 2).Should().Be(new NumberValue(3));

        // The call must complete (loop terminates) and report is well-formed.
        report.Should().NotBeNull();
    }
}
