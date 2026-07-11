using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Regression coverage for R25-spill-dynamic-deep-2: RecalcEngine.Recalculate's very first guard
/// (<c>if (changedCells.Count == 0 &amp;&amp; _volatileCells.Count == 0) return EmptyReport;</c>)
/// short-circuited before the code ever reached the <c>_spillBlockedAnchors</c>-aware retry pass
/// further down, so a caller invoking Recalculate with an EMPTY changed-cells list (e.g.
/// UnmergeCellsCommand.Apply, which never populates CommandOutcome.AffectedCells) could never
/// re-trigger a blocked anchor's #SPILL! recovery, even though that retry pass exists precisely to
/// handle edits with no dependency-graph edge back to the anchor. The guard must fall through
/// (mirror the existing plan-empty guard a few lines below) whenever there are tracked
/// spill-blocked anchors and resolveSpillDependents is true.
/// </summary>
public sealed class R25SpillBlockedAnchorEmptyChangedCellsTests
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
    public void UnmergeBlockingRegion_RecalculateWithEmptyChangedCells_ReSpillsAnchorImmediately()
    {
        var (engine, wb) = MakeEngine();
        var sheet = wb.Sheets.First();

        // A1 = SEQUENCE(3,1) would spill A1:A3, but A2:B2 is merged, so IsSpillBlocked rejects it
        // and A1 shows #SPILL!, tracked in RecalcEngine._spillBlockedAnchors.
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var mergedRegion = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, 2));
        sheet.AddMergedRegion(mergedRegion);
        sheet.SetFormula(a1, "SEQUENCE(3,1)");
        engine.RebuildFormulaDependencies(wb);

        engine.Recalculate(wb, [a1]);
        sheet.GetValue(1, 1).Should().Be(ErrorValue.Spill);

        // Unmerge the blocking region. This mirrors UnmergeCellsCommand.Apply, which only calls
        // sheet.RemoveMergedRegion and never populates CommandOutcome.AffectedCells, so the real
        // caller (WorkbookCellEditService.ApplyHistoryOutcome) ends up invoking Recalculate with an
        // EMPTY changed-cells list here (no volatile cells exist in this workbook either).
        sheet.RemoveMergedRegion(mergedRegion);
        engine.Recalculate(wb, []);

        // A1 must re-spill immediately in this same call, matching Excel, instead of staying stuck
        // at #SPILL! until some unrelated edit or a full Calculate Now/F9.
        sheet.GetValue(1, 1).Should().Be(new NumberValue(1));
        sheet.GetValue(2, 1).Should().Be(new NumberValue(2));
        sheet.GetValue(3, 1).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void NoBlockedAnchors_RecalculateWithEmptyChangedCells_StillShortCircuitsToEmptyReport()
    {
        // Sibling/opposite case: when there is nothing to do at all (no changed cells, no volatile
        // cells, and no tracked spill-blocked anchors), Recalculate must still take the fast
        // EmptyReport path rather than doing unnecessary traversal work. This proves the fix did not
        // remove the original short-circuit for the ordinary "nothing changed" case.
        var (engine, wb) = MakeEngine();
        var sheet = wb.Sheets.First();

        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new NumberValue(42));
        engine.RebuildFormulaDependencies(wb);

        var report = engine.Recalculate(wb, []);

        report.RecalculatedCells.Should().BeEmpty();
        report.Errors.Should().BeEmpty();
        report.CyclicCells.Should().BeEmpty();
        sheet.GetValue(1, 1).Should().Be(new NumberValue(42));
    }
}
