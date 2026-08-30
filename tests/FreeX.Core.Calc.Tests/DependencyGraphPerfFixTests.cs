using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using System.Diagnostics;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Correctness and performance tests for the two dependency-graph performance fixes:
///   Fix 1 (P2): CountPrecedentsWithin now uses a CandidateIndex, avoiding O(dirty² × ranges).
///   Fix 2 (P3): Dependent storage is HashSet&lt;CellAddress&gt; for O(1) Remove.
/// </summary>
public class DependencyGraphPerfFixCorrectnessTests
{
    [BenchmarkFact]
    public void Benchmark_AllDeprioritizedReadyCells_UsesLinearQueueProcessing()
    {
        const int cellCount = 20_000;
        var graph = new DependencyGraph();
        var sheet = SheetId.New();
        var cells = Enumerable.Range(1, cellCount)
            .Select(index => new CellAddress(sheet, (uint)index, 1))
            .ToArray();
        var deprioritized = cells.ToHashSet();

        var stopwatch = Stopwatch.StartNew();
        var plan = graph.GetEvaluationOrder(cells, deprioritized);
        stopwatch.Stop();

        plan.OrderedCells.Should().HaveCount(cellCount);
        plan.CyclicCells.Should().BeEmpty();
        stopwatch.Elapsed.Should().BeLessThan(
            TimeSpan.FromSeconds(2),
            "ready cells should be dequeued once instead of rotating the remaining queue for every cell");
    }

    // ------------------------------------------------------------------
    // Fix 1: CountPrecedentsWithin / GetRecalcOrder with range precedents
    // ------------------------------------------------------------------

    [Fact]
    public void GetRecalcOrder_WithRangePrecedents_ReturnsSameOrderAsExactPrecedentEquivalent()
    {
        // Arrange: C3 = SUM(A1:B2), where each cell in A1:B2 has a dependent.
        // Build via exact deps (oracle) and via range deps, compare order sets.
        var sheetId = SheetId.New();
        var a1 = new CellAddress(sheetId, 1, 1);
        var a2 = new CellAddress(sheetId, 2, 1);
        var b1 = new CellAddress(sheetId, 1, 2);
        var b2 = new CellAddress(sheetId, 2, 2);
        var c3 = new CellAddress(sheetId, 3, 3); // depends on A1:B2

        // Oracle graph using exact deps
        var oracleGraph = new DependencyGraph();
        oracleGraph.SetDependencies(c3, [a1, a2, b1, b2]);

        // Test graph using compact range dep
        var rangeGraph = new DependencyGraph();
        var setDependencies = typeof(DependencyGraph).GetMethod(
            "SetDependencies",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            null,
            [typeof(CellAddress), typeof(HashSet<CellAddress>), typeof(IReadOnlyList<GridRange>)],
            null);
        setDependencies.Should().NotBeNull();
        var range = new GridRange(a1, b2);
        setDependencies!.Invoke(rangeGraph, [c3, new HashSet<CellAddress>(), new[] { range }]);

        // Both should recalc c3 when any cell in the range changes
        foreach (var changed in new[] { a1, a2, b1, b2 })
        {
            var oraclePlan = oracleGraph.GetRecalcOrder([changed]);
            var rangePlan = rangeGraph.GetRecalcOrder([changed]);

            oraclePlan.CyclicCells.Should().BeEmpty();
            rangePlan.CyclicCells.Should().BeEmpty();
            oraclePlan.OrderedCells.Should().BeEquivalentTo(new[] { c3 },
                because: $"oracle graph should trigger c3 when {changed} changes");
            rangePlan.OrderedCells.Should().BeEquivalentTo(new[] { c3 },
                because: $"range graph should trigger c3 when {changed} changes");
        }
    }

    [Fact]
    public void GetRecalcOrder_MultipleRangeDependents_TopologicalOrderMatchesExactEquivalent()
    {
        // D1 = SUM(A1:C1), E1 = D1 + 1
        // So A1 change -> D1 -> E1
        var sheetId = SheetId.New();
        var a1 = new CellAddress(sheetId, 1, 1);
        var b1 = new CellAddress(sheetId, 1, 2);
        var c1 = new CellAddress(sheetId, 1, 3);
        var d1 = new CellAddress(sheetId, 1, 4);
        var e1 = new CellAddress(sheetId, 1, 5);

        var graph = new DependencyGraph();
        var setDependencies = typeof(DependencyGraph).GetMethod(
            "SetDependencies",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            null,
            [typeof(CellAddress), typeof(HashSet<CellAddress>), typeof(IReadOnlyList<GridRange>)],
            null);
        setDependencies!.Invoke(graph, [
            d1,
            new HashSet<CellAddress>(),
            new[] { new GridRange(a1, c1) }
        ]);
        graph.SetDependencies(e1, [d1]);

        var plan = graph.GetRecalcOrder([a1]);

        plan.CyclicCells.Should().BeEmpty();
        plan.OrderedCells.Should().HaveCount(2);
        var d1Idx = plan.OrderedCells.ToList().IndexOf(d1);
        var e1Idx = plan.OrderedCells.ToList().IndexOf(e1);
        d1Idx.Should().BeGreaterThanOrEqualTo(0, "d1 must be in the plan");
        e1Idx.Should().BeGreaterThanOrEqualTo(0, "e1 must be in the plan");
        d1Idx.Should().BeLessThan(e1Idx, "D1 must be evaluated before E1");
    }

    [Fact]
    public void GetRecalcOrder_FullColumnRange_RecalculatesWhenCellInColumnChanges()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("S");
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        var sumCell = new CellAddress(sheet.Id, 1, 2);  // B1 = SUM(A:A)
        var insideA = new CellAddress(sheet.Id, 5000, 1);
        var outsideB = new CellAddress(sheet.Id, 1, 3);

        sheet.SetFormula(sumCell, "SUM(A:A)");
        engine.RecalculateAllFormulas(workbook);

        // Change inside the column -> should recalc
        var insidePlan = engine.Recalculate(workbook, [insideA]);
        insidePlan.RecalculatedCells.Should().Contain(sumCell);

        // Change outside (col C) -> should not recalc
        var outsidePlan = engine.Recalculate(workbook, [outsideB]);
        outsidePlan.RecalculatedCells.Should().NotContain(sumCell);
    }

    [Fact]
    public void GetRecalcOrder_CrossSheetRange_ProducesCorrectOrder()
    {
        var workbook = new Workbook("Test");
        var dataSheet = workbook.AddSheet("Data");
        var formulaSheet = workbook.AddSheet("Formula");
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        var sumCell = new CellAddress(formulaSheet.Id, 1, 1);  // =SUM(Data!A1:A100)
        var inRange = new CellAddress(dataSheet.Id, 50, 1);
        var outOfRange = new CellAddress(dataSheet.Id, 200, 1);

        formulaSheet.SetFormula(sumCell, "SUM(Data!A1:A100)");
        engine.RecalculateAllFormulas(workbook);

        engine.Recalculate(workbook, [inRange]).RecalculatedCells.Should().Contain(sumCell);
        engine.Recalculate(workbook, [outOfRange]).RecalculatedCells.Should().NotContain(sumCell);
    }

    [Fact]
    public void GetEvaluationOrder_WithRangePrecedents_MatchesBruteForceTopologicalOrder()
    {
        // Build a small grid: rows 1..5, col B has =SUM(A1:A5). All 6 cells are dirty.
        // Brute-force oracle: A1..A5 have in-degree 0; B1 has in-degree 5.
        var sheetId = SheetId.New();
        var aCells = Enumerable.Range(1, 5)
            .Select(r => new CellAddress(sheetId, (uint)r, 1))
            .ToArray();
        var b1 = new CellAddress(sheetId, 1, 2);

        var graph = new DependencyGraph();
        var setDependencies = typeof(DependencyGraph).GetMethod(
            "SetDependencies",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            null,
            [typeof(CellAddress), typeof(HashSet<CellAddress>), typeof(IReadOnlyList<GridRange>)],
            null);
        var range = new GridRange(aCells[0], aCells[^1]);
        setDependencies!.Invoke(graph, [b1, new HashSet<CellAddress>(), new[] { range }]);

        var dirtyCells = new HashSet<CellAddress>(aCells) { b1 };
        var plan = graph.GetEvaluationOrder(dirtyCells);

        plan.CyclicCells.Should().BeEmpty();
        plan.OrderedCells.Should().HaveCount(6);
        // b1 must come after all A cells
        var b1Idx = plan.OrderedCells.ToList().IndexOf(b1);
        b1Idx.Should().Be(5, "b1 depends on all A cells and must be last");
    }

    [Fact]
    public void GetRecalcOrder_RangeAndExactPrecedentSameCell_CountedOnce()
    {
        // B1 has exact dep on A1 AND range dep on A1:A3. A1 should only count once.
        var sheetId = SheetId.New();
        var a1 = new CellAddress(sheetId, 1, 1);
        var a2 = new CellAddress(sheetId, 2, 1);
        var a3 = new CellAddress(sheetId, 3, 1);
        var b1 = new CellAddress(sheetId, 1, 2);

        var graph = new DependencyGraph();
        var setDependencies = typeof(DependencyGraph).GetMethod(
            "SetDependencies",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            null,
            [typeof(CellAddress), typeof(HashSet<CellAddress>), typeof(IReadOnlyList<GridRange>)],
            null);
        // B1 has both an exact dep on A1 and a range dep covering A1:A3
        setDependencies!.Invoke(graph, [
            b1,
            new HashSet<CellAddress> { a1 },
            new[] { new GridRange(a1, a3) }
        ]);
        // Also set up a chain: C1 depends on B1 to verify topological order
        var c1 = new CellAddress(sheetId, 1, 3);
        graph.SetDependencies(c1, [b1]);

        var plan = graph.GetRecalcOrder([a1]);
        plan.CyclicCells.Should().BeEmpty();
        plan.OrderedCells.Should().Contain(b1);
        plan.OrderedCells.Should().Contain(c1);
        var b1Idx = plan.OrderedCells.ToList().IndexOf(b1);
        var c1Idx = plan.OrderedCells.ToList().IndexOf(c1);
        b1Idx.Should().BeLessThan(c1Idx);
    }

    // ------------------------------------------------------------------
    // Randomized property test: range-precedent graph vs exact-dep oracle
    // ------------------------------------------------------------------

    [Fact]
    public void GetRecalcOrder_RandomSmallGrids_RangeEquivalentMatchesExactEquivalent()
    {
        // 100 small random grids: compare range-dep graph output set against exact-dep oracle.
        var rng = new Random(20260612);
        const int trials = 100;
        const int maxGridSize = 8;
        const int maxFormulas = 6;

        for (var trial = 0; trial < trials; trial++)
        {
            var sheetId = SheetId.New();
            var gridRows = rng.Next(2, maxGridSize + 1);
            var gridCols = rng.Next(2, maxGridSize + 1);
            var formulaCount = rng.Next(1, Math.Min(maxFormulas, gridRows) + 1);

            // All data cells: row 1..gridRows, col 1
            // Formula cells: col 2, rows 1..formulaCount, each references a random subrange of col 1
            var oracleGraph = new DependencyGraph();
            var rangeGraph = new DependencyGraph();
            var setDependencies = typeof(DependencyGraph).GetMethod(
                "SetDependencies",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                null,
                [typeof(CellAddress), typeof(HashSet<CellAddress>), typeof(IReadOnlyList<GridRange>)],
                null)!;

            for (var f = 0; f < formulaCount; f++)
            {
                var formulaRow = (uint)(f + 1);
                var formulaCell = new CellAddress(sheetId, formulaRow, 2);
                var rangeStart = (uint)(rng.Next(1, gridRows) );
                var rangeEnd = (uint)(rng.Next((int)rangeStart, gridRows + 1));
                var startCell = new CellAddress(sheetId, rangeStart, 1);
                var endCell = new CellAddress(sheetId, rangeEnd, 1);
                var range = new GridRange(startCell, endCell);

                // Exact oracle: expand the range into individual cells
                var exactPrecs = new HashSet<CellAddress>();
                for (var r = rangeStart; r <= rangeEnd; r++)
                    exactPrecs.Add(new CellAddress(sheetId, r, 1));
                oracleGraph.SetDependencies(formulaCell, exactPrecs);

                // Range graph: compact range
                setDependencies.Invoke(rangeGraph, [
                    formulaCell,
                    new HashSet<CellAddress>(),
                    (IReadOnlyList<GridRange>)new[] { range }
                ]);
            }

            // For each data cell, the set of triggered formulas must match
            for (var row = 1; row <= gridRows; row++)
            {
                var changedCell = new CellAddress(sheetId, (uint)row, 1);
                var oraclePlan = oracleGraph.GetRecalcOrder([changedCell]);
                var rangePlan = rangeGraph.GetRecalcOrder([changedCell]);

                oraclePlan.CyclicCells.Should().BeEmpty($"trial {trial}, row {row}: oracle has no cycles");
                rangePlan.CyclicCells.Should().BeEmpty($"trial {trial}, row {row}: range graph has no cycles");

                var oracleSet = new HashSet<CellAddress>(oraclePlan.OrderedCells);
                var rangeSet = new HashSet<CellAddress>(rangePlan.OrderedCells);
                rangeSet.Should().BeEquivalentTo(oracleSet,
                    $"trial {trial}, row {row}: range graph must trigger same cells as oracle");
            }
        }
    }

    // ------------------------------------------------------------------
    // Fix 2: O(1) Remove for dependent links
    // ------------------------------------------------------------------

    [Fact]
    public void RemoveDependentLink_DoesNotLeaveStaleDependents_AfterBulkRewrite()
    {
        var graph = new DependencyGraph();
        var sheetId = SheetId.New();
        var precedent = new CellAddress(sheetId, 1, 1);
        const int formulaCount = 500;

        // Register formulaCount formulas all referencing the same precedent
        var formulaCells = new List<CellAddress>();
        for (var i = 0; i < formulaCount; i++)
        {
            var f = new CellAddress(sheetId, (uint)(i + 2), 1);
            formulaCells.Add(f);
            graph.SetDependencies(f, [precedent]);
        }

        graph.GetDirectDependents(precedent).Count.Should().Be(formulaCount);

        // Rewrite all formulas (clears + re-registers each)
        foreach (var f in formulaCells)
        {
            graph.ClearDependencies(f);
            graph.SetDependencies(f, [precedent]);
        }

        // After rewrite, each formula should appear exactly once in dependents
        graph.GetDirectDependents(precedent).Count.Should().Be(formulaCount,
            "each formula should appear exactly once even after bulk clear+re-register");
    }

    [Fact]
    public void ClearDependencies_RemovesExactDependent_WithoutLeavingEmptyEntry()
    {
        var graph = new DependencyGraph();
        var sheetId = SheetId.New();
        var a1 = new CellAddress(sheetId, 1, 1);
        var b1 = new CellAddress(sheetId, 1, 2);

        graph.SetDependencies(b1, [a1]);
        graph.GetDirectDependents(a1).Should().Contain(b1);

        graph.ClearDependencies(b1);
        graph.GetDirectDependents(a1).Should().NotContain(b1);
    }
}

/// <summary>
/// Performance smoke tests for the two dependency-graph perf fixes.
/// Gated behind FREEX_RUN_BENCHMARK_TESTS to match existing benchmark conventions.
/// </summary>
public class DependencyGraphPerfFixBenchmarkTests
{
    /// <summary>
    /// 10k formula cells each referencing =SUM(A1:A10). Edit A1 — GetRecalcOrder must
    /// complete well within a generous time budget (verifies CandidateIndex avoids
    /// O(dirty² × ranges) quadratic behaviour).
    /// </summary>
    [BenchmarkFact]
    public void Benchmark_GetRecalcOrder_10kRangeDependentFormulas_CompletesWithinTimeBudget()
    {
        var workbook = new Workbook("Benchmark");
        var sheet = workbook.AddSheet("Sheet1");
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        const int formulaCount = 10_000;

        // A1..A10 are the data cells; B1..B10000 each have =SUM(A1:A10)
        for (uint row = 1; row <= 10; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));

        for (uint row = 1; row <= formulaCount; row++)
            sheet.SetFormula(new CellAddress(sheet.Id, row, 2), "SUM(A1:A10)");

        engine.RebuildFormulaDependencies(workbook);

        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new NumberValue(99));

        var sw = Stopwatch.StartNew();
        var report = engine.Recalculate(workbook, [a1]);
        sw.Stop();

        Console.WriteLine(
            $"GetRecalcOrder 10k range-dependent formulas: {sw.ElapsedMilliseconds}ms, " +
            $"{report.RecalculatedCells.Count} cells recalculated");

        report.RecalculatedCells.Count.Should().Be(formulaCount,
            "all formula cells must be recalculated when A1 changes");
        sw.ElapsedMilliseconds.Should().BeLessThan(3_000,
            "GetRecalcOrder with 10k range dependents must complete in under 3s even on slow CI");
    }

    [BenchmarkFact]
    public void Benchmark_BulkFormulaRewrite_RemoveDependentLink_CompletesWithinTimeBudget()
    {
        var graph = new DependencyGraph();
        var sheetId = SheetId.New();
        var hotCell = new CellAddress(sheetId, 1, 1);
        const int formulaCount = 10_000;
        const int iterations = 5;

        var formulaCells = new List<CellAddress>(formulaCount);
        for (var i = 0; i < formulaCount; i++)
        {
            var f = new CellAddress(sheetId, (uint)(i + 2), 1);
            formulaCells.Add(f);
            graph.SetDependencies(f, [hotCell]);
        }

        var sw = Stopwatch.StartNew();
        for (var iter = 0; iter < iterations; iter++)
        {
            foreach (var f in formulaCells)
            {
                graph.ClearDependencies(f);
                graph.SetDependencies(f, [hotCell]);
            }
        }
        sw.Stop();

        var msPerIter = sw.ElapsedMilliseconds / iterations;
        Console.WriteLine(
            $"Bulk formula rewrite (10k formulas × {iterations} iterations): " +
            $"{sw.ElapsedMilliseconds}ms total, {msPerIter}ms/iter");

        msPerIter.Should().BeLessThan(500,
            "O(1) HashSet Remove must keep bulk formula rewrite sub-quadratic");
    }
}
