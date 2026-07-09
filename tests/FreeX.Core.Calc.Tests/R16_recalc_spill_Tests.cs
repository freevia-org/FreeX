using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Regression coverage for R16-array-spill-edges-3: the #SPILL! anchor retry pass (RecalcEngine,
/// triggered when a cell that was BLOCKING a dynamic array is cleared/edited, without any
/// dependency-graph edge back to the blocked anchor) re-spills the anchor via
/// <c>Recalculate(workbook, retryAnchors, resolveSpillDependents:false)</c>. The spill-target
/// dependent follow-up fixpoint (the only path that refreshes formulas reading a spill's
/// non-anchor target cells) was gated on <c>resolveSpillDependents == true</c>, so it never ran
/// for cells the retry itself re-spilled. A formula that reads one of those freshly-populated
/// target cells therefore stayed stale (baked in the pre-retry blank/blocked value) until the next
/// full recalc / F9, instead of reflecting the fresh value in the same recalc pass like Excel does.
/// </summary>
public sealed class R16RecalcSpillTests
{
    private static (RecalcEngine engine, Workbook wb) MakeEngine()
    {
        var graph = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        var engine = new RecalcEngine(graph, evaluator);
        var wb = new Workbook();
        wb.AddSheet("Sheet1");
        return (engine, wb);
    }

    [Fact]
    public void ReSpilledAnchorViaRetry_ReaderOfSpillTarget_RefreshesInSamePass()
    {
        var (engine, wb) = MakeEngine();
        var sheet = wb.Sheets.First();

        // A1 = SEQUENCE(3) -- would spill A1:A3, but A2 starts occupied by a plain value, so it
        // shows #SPILL! and gets tracked in RecalcEngine's _spillBlockedAnchors set.
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var blocker = new CellAddress(sheet.Id, 2, 1); // A2
        var reader = new CellAddress(sheet.Id, 1, 4);  // D1 = A2, a spill-target reader

        sheet.SetCell(blocker, new NumberValue(99));
        sheet.SetFormula(a1, "SEQUENCE(3)");
        sheet.SetFormula(reader, "A2");
        engine.RebuildFormulaDependencies(wb);

        // Initial recalc: A1 is blocked (#SPILL!), D1 reads the blocker's own value (99).
        engine.Recalculate(wb, [a1, reader]);
        sheet.GetValue(1, 1).Should().Be(ErrorValue.Spill);
        sheet.GetValue(1, 4).Should().Be(new NumberValue(99));

        // Clear the blocker and recalc from the blocker alone (mirrors the real trigger: an edit
        // to the blocking cell has no dependency-graph edge back to the A1 anchor's formula, only
        // to D1 via D1's own "=A2" reference). A1 is picked up solely via the #SPILL! anchor retry.
        sheet.ClearCell(blocker);
        engine.Recalculate(wb, [blocker]);

        // A1 must have re-spilled (blocker cleared) ...
        sheet.GetValue(1, 1).Should().Be(new NumberValue(1));
        sheet.GetValue(2, 1).Should().Be(new NumberValue(2));
        sheet.GetValue(3, 1).Should().Be(new NumberValue(3));

        // ... and D1, which reads A2 (a non-anchor spill-target cell of A1's re-spill), must
        // reflect the freshly-spilled value (2) in this SAME recalc pass, not the stale value it
        // was evaluated with before the retry re-populated A2.
        sheet.GetValue(1, 4).Should().Be(new NumberValue(2),
            "D1 reads a spill-target cell that only became populated by the #SPILL! anchor retry " +
            "re-spilling A1 in this same Recalculate call, and must not stay stale until the next " +
            "full recalc/F9");
    }

    [Fact]
    public void ReSpilledAnchorViaRetry_ChainedSpillDependent_ConvergesInSamePass()
    {
        // Same shape as the first test, but the spill-target reader is itself a dynamic-array
        // formula (SEQUENCE(A2)), confirming the retry's follow-up re-evaluates -- and reshapes --
        // a chained spill-dependent, not just a plain scalar reader. The blocker's initial value
        // (5) deliberately differs from the value A2 gets once A1 re-spills (2), so a stale C1
        // (still SEQUENCE(5), i.e. C1:C5 populated) is distinguishable from a fresh one (SEQUENCE(2),
        // i.e. only C1:C2 populated and C3:C5 cleared).
        var (engine, wb) = MakeEngine();
        var sheet = wb.Sheets.First();

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var blocker = new CellAddress(sheet.Id, 2, 1); // A2
        var c1 = new CellAddress(sheet.Id, 1, 3);      // C1 = SEQUENCE(A2)

        sheet.SetCell(blocker, new NumberValue(5));
        sheet.SetFormula(a1, "SEQUENCE(3)");
        sheet.SetFormula(c1, "SEQUENCE(A2)");
        engine.RebuildFormulaDependencies(wb);

        engine.Recalculate(wb, [a1, c1]);
        sheet.GetValue(1, 1).Should().Be(ErrorValue.Spill);
        // C1 = SEQUENCE(5) -> spills C1:C5 = 1,2,3,4,5 using the blocker's own value.
        sheet.GetValue(1, 3).Should().Be(new NumberValue(1));
        sheet.GetValue(5, 3).Should().Be(new NumberValue(5));

        sheet.ClearCell(blocker);
        engine.Recalculate(wb, [blocker]);

        // A1 re-spills 1,2,3 down A1:A3, so A2 becomes 2.
        sheet.GetValue(1, 1).Should().Be(new NumberValue(1));
        sheet.GetValue(2, 1).Should().Be(new NumberValue(2));
        sheet.GetValue(3, 1).Should().Be(new NumberValue(3));

        // C1 must re-evaluate off the fresh, freshly-spilled A2 (=2) in this same pass: SEQUENCE(2)
        // shrinks the spill to C1:C2 and clears the now-stale C3:C5 tail.
        sheet.GetValue(1, 3).Should().Be(new NumberValue(1));
        sheet.GetValue(2, 3).Should().Be(new NumberValue(2));
        sheet.GetValue(3, 3).Should().Be(new BlankValue(),
            "C1's spill must shrink to SEQUENCE(A2=2) once A2 is refreshed by the retry's " +
            "spill-target follow-up, not remain stale at SEQUENCE(5)");
        sheet.GetValue(5, 3).Should().Be(new BlankValue());
    }
}
