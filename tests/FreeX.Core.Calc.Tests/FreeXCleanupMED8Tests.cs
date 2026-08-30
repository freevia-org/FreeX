using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Regression test for FreeX cleanup batch MED8, finding P78:
///
/// A volatile function (OFFSET/INDIRECT/CELL/...) can dynamically read a cell that has no
/// registered dependency edge back to it - only the function's static ARGUMENT cells get an edge
/// (see RecalcEngine.CollectReferences' FunctionCallNode case). When such a volatile cell and an
/// unrelated dirty cell it dynamically reads both reach in-degree 0 in the same recalc pass,
/// DependencyGraph.GetEvaluationOrder's Kahn's-algorithm ready-queue (backed by HashSet
/// enumeration) has no basis to order them - the volatile cell can run first and observe the other
/// cell's stale pre-edit value for that pass.
///
/// The fix adds a "deprioritized" tie-break to GetEvaluationOrder: a deprioritized cell always
/// loses a readiness tie against a non-deprioritized one, while every REAL registered edge (a
/// non-deprioritized cell that actually depends on a deprioritized one) is still honored exactly
/// as before. RecalcEngine passes its tracked volatile-cell set as "deprioritized" for exactly this
/// reason.
/// </summary>
public class FreeXCleanupMED8Tests
{
    // ── Direct DependencyGraph.GetEvaluationOrder contract (deterministic root-cause check) ────

    [Fact]
    public void GetEvaluationOrder_DeprioritizedCell_NeverPrecedesUnrelatedReadyCell()
    {
        // Five unrelated cells (no edges between any of them - exactly the "ambiguous" scenario
        // from P78, where OFFSET/INDIRECT's dynamically-read target has no edge back to it) plus
        // one deprioritized cell that also has no edge to any of them. Without the tie-break fix,
        // Kahn's ready queue could legally place the deprioritized cell anywhere among the six.
        var graph = new DependencyGraph();
        var sheet = SheetId.New();
        var n1 = new CellAddress(sheet, 1, 1);
        var n2 = new CellAddress(sheet, 1, 2);
        var n3 = new CellAddress(sheet, 1, 3);
        var n4 = new CellAddress(sheet, 1, 4);
        var n5 = new CellAddress(sheet, 1, 5);
        var deprioritizedCell = new CellAddress(sheet, 9, 9);

        var dirtyCells = new HashSet<CellAddress> { n1, n2, n3, n4, n5, deprioritizedCell };
        var deprioritized = new HashSet<CellAddress> { deprioritizedCell };

        var plan = graph.GetEvaluationOrder(dirtyCells, deprioritized);

        plan.CyclicCells.Should().BeEmpty();
        plan.OrderedCells.Should().HaveCount(6);
        var orderedList = plan.OrderedCells.ToList();
        var deprioritizedIndex = orderedList.IndexOf(deprioritizedCell);
        deprioritizedIndex.Should().Be(
            5,
            "the deprioritized cell has no real edge to any other candidate, so it must lose every " +
            "readiness tie and end up last, instead of an arbitrary HashSet-enumeration position");
    }

    [Fact]
    public void GetEvaluationOrder_DeprioritizedPrecedent_StillOrdersItsRealDependentAfterIt()
    {
        // A non-deprioritized cell that genuinely DEPENDS ON a deprioritized cell (a real registered
        // edge - e.g. a non-volatile formula that statically references a volatile cell) must still
        // be evaluated strictly after it. The tie-break only resolves ties between UNRELATED cells;
        // it must never reorder a real precedent/dependent pair.
        var graph = new DependencyGraph();
        var sheet = SheetId.New();
        var deprioritizedPrecedent = new CellAddress(sheet, 1, 1);
        var realDependent = new CellAddress(sheet, 1, 2);

        graph.SetDependencies(realDependent, [deprioritizedPrecedent]);

        var dirtyCells = new HashSet<CellAddress> { deprioritizedPrecedent, realDependent };
        var deprioritized = new HashSet<CellAddress> { deprioritizedPrecedent };

        var plan = graph.GetEvaluationOrder(dirtyCells, deprioritized);

        plan.CyclicCells.Should().BeEmpty();
        plan.OrderedCells.Should().Equal(deprioritizedPrecedent, realDependent);
    }

    [Fact]
    public void GetEvaluationOrder_NewlyReadyNormalCell_PreemptsOlderAndNewerDeprioritizedCells()
    {
        var graph = new DependencyGraph();
        var sheet = SheetId.New();
        var normalRoot = new CellAddress(sheet, 1, 1);
        var normalDependent = new CellAddress(sheet, 1, 2);
        var olderDeprioritizedRoot = new CellAddress(sheet, 2, 1);
        var newerDeprioritizedDependent = new CellAddress(sheet, 2, 2);

        graph.SetDependencies(normalDependent, [normalRoot]);
        graph.SetDependencies(newerDeprioritizedDependent, [normalRoot]);

        var plan = graph.GetEvaluationOrder(
            [normalRoot, normalDependent, olderDeprioritizedRoot, newerDeprioritizedDependent],
            [olderDeprioritizedRoot, newerDeprioritizedDependent]);

        plan.CyclicCells.Should().BeEmpty();
        plan.OrderedCells.Should().Equal(
            normalRoot,
            normalDependent,
            olderDeprioritizedRoot,
            newerDeprioritizedDependent);
    }

    // ── End-to-end RecalcEngine scenario matching the finding's exact repro ─────────────────────

    [Fact]
    public void Recalc_OffsetVolatileCell_ReadsFreshValue_OfUnrelatedDirtyCellInSamePass()
    {
        // A2 = B1*2 (dirtied by editing B1); C1 = OFFSET(A1,1,0), which dynamically reads A2 but has
        // no registered dependency edge to it (only its argument A1 gets an edge). Both A2 and C1
        // become dirty/volatile in the same recalc pass triggered by editing B1.
        var graph = new DependencyGraph();
        var evaluator = new FreeX.Core.Formula.FormulaEvaluator();
        var engine = new RecalcEngine(graph, evaluator);
        var wb = new Workbook();
        var sheet = wb.AddSheet("Sheet1");

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var c1 = new CellAddress(sheet.Id, 1, 3);

        sheet.SetCell(b1, new NumberValue(10));
        sheet.SetFormula(a2, "B1*2");
        sheet.SetFormula(c1, "OFFSET(A1,1,0)");
        engine.RebuildFormulaDependencies(wb);
        engine.Recalculate(wb, [a2, c1]);

        sheet.GetValue(2, 1).Should().Be(new NumberValue(20), "A2 = B1*2 = 10*2");
        sheet.GetValue(1, 3).Should().Be(new NumberValue(20), "C1 = OFFSET(A1,1,0) dynamically reads A2");

        // Edit B1 repeatedly and recalculate the same dirty set every time (mirrors RecalcEngine's
        // real call pattern when B1 changes: A2 is dirtied as B1's dependent, C1 re-runs because it
        // is volatile). Before the fix this could intermittently show C1 one edit "behind" A2 within
        // a single pass, since nothing ordered the volatile OFFSET cell after A2. Repeating several
        // edits makes any such race very likely to surface if the ordering bias were absent.
        for (var iteration = 1; iteration <= 20; iteration++)
        {
            var newB1 = 10 + iteration;
            sheet.SetCell(b1, new NumberValue(newB1));
            engine.Recalculate(wb, [a2]);

            var expected = new NumberValue(newB1 * 2);
            sheet.GetValue(2, 1).Should().Be(expected, $"A2 = B1*2 after iteration {iteration}");
            sheet.GetValue(1, 3).Should().Be(
                expected,
                $"C1 = OFFSET(A1,1,0) must reflect A2's POST-edit value within the same recalc pass " +
                $"(iteration {iteration}), not a stale pre-edit value");
        }
    }
}
