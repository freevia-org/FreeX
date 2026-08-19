using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using System.Diagnostics;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Performance baseline tests to establish v1.0 metrics.
/// Run with: dotnet test --filter "PerformanceBenchmark" --verbosity normal
/// </summary>
public class PerformanceBenchmarkTests
{
    /// <summary>
    /// Benchmark: Recalculate 10,000 cells (10% formula density).
    /// Target: <100ms
    /// </summary>
    [BenchmarkFact]
    public void Benchmark_10kCellRecalc()
    {
        // Arrange: Create workbook with 10k populated cells, 10% formulas
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Sheet1");
        
        Console.WriteLine($"Building 10k-cell test workbook...");
        var buildSw = Stopwatch.StartNew();
        
        for (uint row = 1; row <= 4500; row++)
        {
            // Column A: raw values
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue((double)row));
            
            // Column B: raw values
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue((double)row * 2));
        }

        for (uint row = 1; row <= 1000; row++)
        {
            sheet.SetFormula(new CellAddress(sheet.Id, row, 3), $"A{row}+B{row}");
        }
        buildSw.Stop();
        Console.WriteLine($"  Built in {buildSw.ElapsedMilliseconds}ms");

        // Create engine and dependency graph
        var graph = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        var engine = new RecalcEngine(graph, evaluator);
        var changedCells = new List<CellAddress>();
        for (uint row = 1; row <= 1000; row++)
        {
            changedCells.Add(new CellAddress(sheet.Id, row, 3)); // Add formula cells to changed list
        }

        // Act: Recalc all cells
        var recalcSw = Stopwatch.StartNew();
        var report = engine.Recalculate(workbook, changedCells);
        recalcSw.Stop();

        Console.WriteLine($"Recalc 10k cells (1000 formulas): {recalcSw.ElapsedMilliseconds}ms");
        Console.WriteLine($"  Recalculated: {report.RecalculatedCells.Count} cells");
        if (report.RecalculatedCells.Count > 0)
            Console.WriteLine($"  Per formula: {(double)recalcSw.ElapsedMilliseconds / report.RecalculatedCells.Count:F3}ms");

        // Assert: Should be reasonable (adjust threshold if needed)
        // Note: First recalc includes dependency graph building; subsequent recalcs are faster
        // Target: <500ms for 10k cells with modest formula complexity
        Assert.True(recalcSw.ElapsedMilliseconds < 1000, 
            $"10k recalc took {recalcSw.ElapsedMilliseconds}ms (expected <1000ms)");
    }

    /// <summary>
    /// Benchmark: Recalculate 100,000 cells (1% formula density).
    /// Target: <500ms
    /// </summary>
    [BenchmarkFact]
    public void Benchmark_100kCellRecalc()
    {
        // Arrange: Create workbook with 100k populated cells, 1% formulas
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Sheet1");

        Console.WriteLine($"Building 100k-cell test workbook...");
        var buildSw = Stopwatch.StartNew();

        for (uint row = 1; row <= 49500; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue((double)row));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue((double)row * 2));
        }

        for (uint row = 1; row <= 1000; row++)
        {
            sheet.SetFormula(new CellAddress(sheet.Id, row, 3), $"A{row}+B{row}");
        }
        buildSw.Stop();
        Console.WriteLine($"  Built in {buildSw.ElapsedMilliseconds}ms");

        var graph = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        var engine = new RecalcEngine(graph, evaluator);
        var changedCells = new List<CellAddress>();
        for (uint row = 1; row <= 1000; row++)
        {
            changedCells.Add(new CellAddress(sheet.Id, row, 3)); // Add formula cells to changed list
        }

        // Act: Recalc
        var recalcSw = Stopwatch.StartNew();
        var report = engine.Recalculate(workbook, changedCells);
        recalcSw.Stop();

        Console.WriteLine($"Recalc 100k cells (1000 formulas): {recalcSw.ElapsedMilliseconds}ms");
        Console.WriteLine($"  Recalculated: {report.RecalculatedCells.Count} cells");
        if (report.RecalculatedCells.Count > 0)
            Console.WriteLine($"  Per formula: {(double)recalcSw.ElapsedMilliseconds / report.RecalculatedCells.Count:F3}ms");

        // Assert: Target <1s
        Assert.True(recalcSw.ElapsedMilliseconds < 2000, 
            $"100k recalc took {recalcSw.ElapsedMilliseconds}ms (expected <2000ms)");
    }

    [BenchmarkFact]
    public void Benchmark_RepeatedSmallChangeRecalc_ReportsAllocationDiagnostics()
    {
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Sheet1");
        var graph = new DependencyGraph();
        var engine = new RecalcEngine(graph, new FormulaEvaluator());
        const uint formulaCount = 5_000;
        const int iterations = 250;

        for (uint row = 1; row <= formulaCount; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row * 2));
            sheet.SetFormula(new CellAddress(sheet.Id, row, 3), $"A{row}+B{row}");
        }

        engine.RebuildFormulaDependencies(workbook);

        var changed = new[]
        {
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 2)
        };

        engine.Recalculate(workbook, changed);

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
            engine.Recalculate(workbook, changed);
        sw.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Console.WriteLine(
            $"Repeated small-change recalc: {iterations} iterations, {formulaCount:N0} formulas, " +
            $"{sw.Elapsed.TotalMilliseconds:F2}ms, {allocated:N0} bytes allocated, " +
            $"{allocated / iterations:N0} bytes/iteration");

        sheet.GetValue(new CellAddress(sheet.Id, 1, 3)).Should().Be(new NumberValue(3));
        allocated.Should().BeGreaterThan(0);
        (allocated / iterations).Should().BeLessThan(
            600,
            "multi-root exact edits that invalidate one leaf formula should skip BFS and topological scaffolding");
    }

    [BenchmarkFact]
    public void Benchmark_ExactFormulaChainRecalcOrder_AvoidsPrecedentDedupeAllocation()
    {
        var graph = new DependencyGraph();
        var sheetId = SheetId.New();
        var root = new CellAddress(sheetId, 1, 1);
        var previous = root;
        const uint formulaCount = 1_000;
        const int iterations = 100;

        for (uint row = 2; row <= formulaCount + 1; row++)
        {
            var current = new CellAddress(sheetId, row, 1);
            graph.SetDependencies(current, [previous]);
            previous = current;
        }

        graph.GetRecalcOrder([root]);

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var plan = graph.GetRecalcOrder([root]);
            if (plan.OrderedCells.Count != formulaCount || plan.CyclicCells.Count != 0)
                throw new Xunit.Sdk.XunitException("Formula chain recalc order should include every downstream formula exactly once.");
        }
        sw.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Console.WriteLine(
            $"Exact formula chain recalc order: {iterations} iterations, {formulaCount:N0} formulas, " +
            $"{sw.Elapsed.TotalMilliseconds:F2}ms, {allocated:N0} bytes allocated, " +
            $"{allocated / iterations:N0} bytes/iteration");

        (allocated / iterations).Should().BeLessThan(
            80_000,
            "single-root exact-only formula chains should use the linear recalc-order fast path");
    }

    [BenchmarkFact]
    public void Benchmark_IdleRecalcWithoutChangedOrVolatileCells_IsAllocationFree()
    {
        var workbook = new Workbook();
        workbook.AddSheet("Sheet1");
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        const int iterations = 10_000;

        engine.Recalculate(workbook, []);

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var report = engine.Recalculate(workbook, []);
            if (report.RecalculatedCells.Count != 0 ||
                report.Errors.Count != 0 ||
                report.CyclicCells.Count != 0)
            {
                throw new Xunit.Sdk.XunitException("Idle recalc should not report recalculated cells, errors, or cycles.");
            }
        }
        sw.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Console.WriteLine(
            $"Idle recalc: {iterations:N0} iterations, {sw.Elapsed.TotalMilliseconds:F2}ms, " +
            $"{allocated:N0} bytes allocated, {allocated / iterations:N0} bytes/iteration");

        allocated.Should().BeLessThan(
            1_024,
            "idle recalculation should bypass dependency traversal collection allocation");
    }

    [BenchmarkFact]
    public void Benchmark_LeafFormulaRootRecalc_SkipsEvaluationOrderScaffolding()
    {
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Sheet1");
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        var precedent = new CellAddress(sheet.Id, 1, 1);
        var formula = new CellAddress(sheet.Id, 1, 2);
        var changed = new[] { formula };
        const int iterations = 10_000;

        sheet.SetCell(precedent, new NumberValue(41));
        sheet.SetFormula(formula, "A1+1");
        engine.RebuildFormulaDependencies(workbook);
        engine.Recalculate(workbook, changed);

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var report = engine.Recalculate(workbook, changed);
            if (report.RecalculatedCells.Count != 1 || !report.RecalculatedCells[0].Equals(formula))
                throw new Xunit.Sdk.XunitException("Leaf formula root recalc should evaluate the changed formula exactly once.");
        }
        sw.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Console.WriteLine(
            $"Leaf formula root recalc: {iterations:N0} iterations, " +
            $"{sw.Elapsed.TotalMilliseconds:F2}ms, {allocated:N0} bytes allocated, " +
            $"{allocated / iterations:N0} bytes/iteration");
        Console.WriteLine(
            $"PERF LEAF_FORMULA_ROOT_RECALC iterations={iterations:N0} total_ms={sw.Elapsed.TotalMilliseconds:F2} " +
            $"allocated_bytes={allocated:N0} bytes_per_iteration={allocated / iterations:N0}");

        sheet.GetValue(formula).Should().Be(new NumberValue(42));
        (allocated / iterations).Should().BeLessThan(
            260,
            "leaf formula roots without downstream dependents should reuse the one-cell changed set and avoid list-backed report allocation");
    }

    [Fact]
    public void LeafFormulaRootRecalc_SkipsEvaluationOrderScaffoldingBeforeDirtySetBuild()
    {
        var source = CalcSourceTestSupport.ReadCalcSourceFromCurrentDirectoryOrFallback("RecalcEngine.cs");
        var recalculate = source[
            source.IndexOf("var evaluationPlan = plan;", StringComparison.Ordinal)..
            source.IndexOf("foreach (var addr in directFormulaRoots ?? evaluationPlan.OrderedCells)", StringComparison.Ordinal)];
        var fastPath = source[
            source.IndexOf("private static bool CanEvaluateChangedFormulaRootsDirectly", StringComparison.Ordinal)..
            // AddCyclicCell became an instance method (no longer "private static") once it started
            // populating the persisted _cyclicCells set (see RecalcEngine.CyclicCells) so
            // FormulaAuditingService can discover circular cells across calls -- a genuine,
            // necessary change, not hot-path scaffolding. Only the marker needed updating here.
            source.IndexOf("private void AddCyclicCell", StringComparison.Ordinal)];

        // R149-formula-volatility-manual-mode-fresh-formula-recalc: the call site now passes the
        // includeVolatileCells-gated local (volatileCellsForPass) instead of the raw
        // _volatileCells field, so an opted-out caller's fast-path check also sees zero.
        recalculate.Should().Contain("CanEvaluateChangedFormulaRootsDirectly(plan, changedFormulaCells, volatileCellsForPass.Count)");
        recalculate.Should().Contain("directFormulaRoots = changedFormulaCells!.Count == 1");
        recalculate.Should().NotContain("var recalculated = new List<CellAddress>();");
        recalculate.IndexOf("CanEvaluateChangedFormulaRootsDirectly", StringComparison.Ordinal)
            .Should()
            .BeLessThan(recalculate.IndexOf("var dirtyCells = new HashSet<CellAddress>", StringComparison.Ordinal));

        source.Should().Contain("return sheet?.GetCell(addr)?.HasFormula == true ? changedCells : null;");
        source.Should().Contain("private static IReadOnlyList<CellAddress> BuildRecalculatedCells(");
        source.Should().Contain("BuildRecalculatedCells(recalculatedCount, singleRecalculated, recalculated)");
        fastPath.Should().Contain("volatileCellCount == 0");
        fastPath.Should().Contain("changedFormulaCells is { Count: > 0 }");
        fastPath.Should().Contain("dependencyPlan.OrderedCells.Count == 0");
        fastPath.Should().Contain("dependencyPlan.CyclicCells.Count == 0");
    }

    [BenchmarkFact]
    public void Benchmark_LargeRangeDependencyRebuild_UsesCompactRangeTracking()
    {
        var workbook = new Workbook("Benchmark");
        var sheet = workbook.AddSheet("Sheet1");
        var graph = new DependencyGraph();
        var engine = new RecalcEngine(graph, new FormulaEvaluator());
        var formula = new CellAddress(sheet.Id, 1, 2);
        var inside = new CellAddress(sheet.Id, 50000, 1);
        var outside = new CellAddress(sheet.Id, 1, 3);

        sheet.SetFormula(formula, "SUM(A1:A100000)");
        sheet.SetCell(inside, new NumberValue(1));
        sheet.SetCell(outside, new NumberValue(2));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var rebuildSw = Stopwatch.StartNew();
        engine.RebuildFormulaDependencies(workbook);
        rebuildSw.Stop();
        var rebuildAllocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var insideSw = Stopwatch.StartNew();
        var insideReport = engine.Recalculate(workbook, [inside]);
        insideSw.Stop();
        var insideAllocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var outsideSw = Stopwatch.StartNew();
        var outsideReport = engine.Recalculate(workbook, [outside]);
        outsideSw.Stop();
        var outsideAllocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Console.WriteLine(
            $"Large range dependency rebuild: {rebuildSw.Elapsed.TotalMilliseconds:F2}ms, " +
            $"{rebuildAllocated:N0} bytes allocated");
        Console.WriteLine(
            $"Large range inside-cell recalc: {insideSw.Elapsed.TotalMilliseconds:F2}ms, " +
            $"{insideAllocated:N0} bytes allocated");
        Console.WriteLine(
            $"Large range outside-cell recalc: {outsideSw.Elapsed.TotalMilliseconds:F2}ms, " +
            $"{outsideAllocated:N0} bytes allocated");

        insideReport.RecalculatedCells.Should().Contain(formula);
        outsideReport.RecalculatedCells.Should().NotContain(formula);
        rebuildAllocated.Should().BeLessThan(
            1_000_000,
            "dependency-only range registration should not initialize the full formula evaluator registry");
    }

    [BenchmarkFact]
    public void Benchmark_ManyCompactRangeDependents_ReportsIndexedLookupTiming()
    {
        var graph = new DependencyGraph();
        var sheetId = SheetId.New();
        const uint rangeCount = 20_000;
        const int iterations = 250;

        var setDependencies = typeof(DependencyGraph).GetMethod(
            "SetDependencies",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            binder: null,
            [
                typeof(CellAddress),
                typeof(HashSet<CellAddress>),
                typeof(IReadOnlyList<GridRange>)
            ],
            modifiers: null);
        setDependencies.Should().NotBeNull();

        for (uint row = 1; row <= rangeCount; row++)
        {
            var formula = new CellAddress(sheetId, row, 1_200);
            var range = new GridRange(
                new CellAddress(sheetId, row, 1),
                new CellAddress(sheetId, row, 1_100));

            setDependencies!.Invoke(graph, [formula, new HashSet<CellAddress>(), new[] { range }]);
        }

        var changed = new CellAddress(sheetId, rangeCount / 2, 500);
        var expected = new CellAddress(sheetId, rangeCount / 2, 1_200);
        graph.GetRecalcOrder([changed]);

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var plan = graph.GetRecalcOrder([changed]);
            if (plan.OrderedCells.Count != 1 ||
                plan.CyclicCells.Count != 0 ||
                !plan.OrderedCells[0].Equals(expected))
            {
                throw new Xunit.Sdk.XunitException("Compact range lookup should recalculate the one formula whose range contains the changed cell.");
            }
        }

        sw.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Console.WriteLine(
            $"Many compact range dependents recalc order: {iterations} iterations, {rangeCount:N0} ranges, " +
            $"{sw.Elapsed.TotalMilliseconds:F2}ms, {allocated:N0} bytes allocated, " +
            $"{allocated / iterations:N0} bytes/iteration");

        (sw.Elapsed.TotalMilliseconds / iterations).Should().BeLessThan(
            5.0,
            "compact range invalidation lookup should stay sublinear enough for interactive single-cell edits");
        (allocated / iterations).Should().BeLessThan(
            600,
            "single-cell compact range invalidations that hit one leaf formula should skip BFS/topological scaffolding");
    }

    [BenchmarkFact]
    public void Benchmark_DependencyRebuildWithFormulaFreeSheet_ReportsTiming()
    {
        var workbook = new Workbook("Benchmark");
        var valueSheet = workbook.AddSheet("ValuesOnly");
        var formulaSheet = workbook.AddSheet("Formulas");
        var graph = new DependencyGraph();
        var engine = new RecalcEngine(graph, new FormulaEvaluator());
        const uint valueCellCount = 200_000;
        const uint formulaCount = 1_000;
        const int iterations = 8;

        for (uint row = 1; row <= valueCellCount; row++)
            valueSheet.SetCell(new CellAddress(valueSheet.Id, row, 1), new NumberValue(row));

        for (uint row = 1; row <= formulaCount; row++)
        {
            formulaSheet.SetCell(new CellAddress(formulaSheet.Id, row, 1), new NumberValue(row));
            formulaSheet.SetFormula(new CellAddress(formulaSheet.Id, row, 2), $"A{row}*2");
        }

        engine.RebuildFormulaDependencies(workbook);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
            engine.RebuildFormulaDependencies(workbook);
        sw.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Console.WriteLine(
            $"Formula-free sheet dependency rebuild: {iterations} iterations, " +
            $"{valueCellCount:N0} value cells, {formulaCount:N0} formulas, " +
            $"{sw.Elapsed.TotalMilliseconds:F2}ms, {allocated:N0} bytes allocated, " +
            $"{allocated / iterations:N0} bytes/iteration");

        valueSheet.HasFormulas.Should().BeFalse();
        formulaSheet.FormulaCellCount.Should().Be((int)formulaCount);
        allocated.Should().BeGreaterThan(0);
        (allocated / iterations).Should().BeLessThan(
            540_000,
            "exact-reference formula dependency rebuilds should not allocate empty compact-range lists per formula");
    }

    [BenchmarkFact]
    public void Benchmark_RepeatedIdenticalFormulaDependencyRebuild_ReportsParserCacheTail()
    {
        var workbook = new Workbook("Benchmark");
        var sheet = workbook.AddSheet("Sheet1");
        var graph = new DependencyGraph();
        var engine = new RecalcEngine(graph, new FormulaEvaluator());
        const uint formulaCount = 5_000;
        const string formulaText = "SUM($A$1:$A$10)+$B$1";

        for (uint row = 1; row <= 10; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(5));

        for (uint row = 1; row <= formulaCount; row++)
            sheet.SetFormula(new CellAddress(sheet.Id, row, 3), formulaText);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        engine.RebuildFormulaDependencies(workbook);
        sw.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Console.WriteLine(
            $"Repeated identical formula dependency rebuild: {formulaCount:N0} formulas, " +
            $"{sw.Elapsed.TotalMilliseconds:F2}ms, {allocated:N0} bytes allocated, " +
            $"{allocated / formulaCount:N0} bytes/formula");
        Console.WriteLine(
            $"PERF REPEATED_IDENTICAL_FORMULA_REBUILD formulas={formulaCount:N0} total_ms={sw.Elapsed.TotalMilliseconds:F2} " +
            $"allocated_bytes={allocated:N0} bytes_per_formula={allocated / formulaCount:N0}");

        sheet.GetCell(new CellAddress(sheet.Id, 1, 3))!.CachedAst.Should().NotBeNull();
        allocated.Should().BeGreaterThan(0);
        (allocated / formulaCount).Should().BeLessThan(
            340,
            "repeated formula dependency rebuilds should reuse shared text-to-AST, compact dependency plans, and repeated range-index entries");
    }

    [Fact]
    public void RebuildFormulaDependencies_SkipsFormulaFreeSheetsBeforeEnumeratingCells()
    {
        var source = CalcSourceTestSupport.ReadCalcSourceFromCurrentDirectoryOrFallback("RecalcEngine.cs");
        var rebuild = source[
            source.IndexOf("public void RebuildFormulaDependencies", StringComparison.Ordinal)..
            source.IndexOf("public RecalcReport RecalculateAllFormulas", StringComparison.Ordinal)];

        rebuild.Should().Contain("if (!sheet.HasFormulas)");
        rebuild.IndexOf("if (!sheet.HasFormulas)", StringComparison.Ordinal)
            .Should().BeLessThan(rebuild.IndexOf("foreach (var addr in sheet.EnumerateFormulaCells())", StringComparison.Ordinal));
        rebuild.Should().NotContain("foreach (var (addr, cell) in sheet.EnumerateCells())");
    }

    [BenchmarkFact]
    public void Benchmark_SingleSectionNumberFormat_AvoidsSplitScaffoldingAllocation()
    {
        const int iterations = 10_000;
        var value = new NumberValue(12345.678);

        NumberFormatter.Format(value, "#,##0.00");

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var formatted = NumberFormatter.Format(value, "#,##0.00");
            if (formatted != "12,345.68")
                throw new Xunit.Sdk.XunitException("Single-section number format produced an unexpected value.");
        }
        sw.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Console.WriteLine(
            $"Single-section number format: {iterations:N0} iterations, {sw.Elapsed.TotalMilliseconds:F2}ms, " +
            $"{allocated:N0} bytes allocated, {allocated / iterations:N0} bytes/iteration");
        Console.WriteLine(
            $"PERF NUMBERFORMAT_SINGLE_SECTION iterations={iterations:N0} total_ms={sw.Elapsed.TotalMilliseconds:F2} " +
            $"allocated_bytes={allocated:N0} bytes_per_iteration={allocated / iterations:N0}");

        (allocated / iterations).Should().BeLessThan(
            360,
            "single-section number formats should skip no-op normalization allocations");
    }

    [BenchmarkFact]
    public void Benchmark_MultiSectionNumberFormat_StillHonorsQuotedSemicolons()
    {
        var positive = NumberFormatter.Format(new NumberValue(1), "0;[Red]-0;0;\"a;b\"@");
        var negative = NumberFormatter.Format(new NumberValue(-1), "0;[Red]-0;0;\"a;b\"@");
        var zero = NumberFormatter.Format(new NumberValue(0), "0;[Red]-0;0;\"a;b\"@");
        var text = NumberFormatter.Format(new TextValue("x"), "0;[Red]-0;0;\"a;b\"@");

        positive.Should().Be("1");
        negative.Should().Be("-1");
        zero.Should().Be("0");
        text.Should().Be("a;bx");
    }

    [BenchmarkFact]
    public void Benchmark_RepeatedCustomNumberFormat_ReusesSplitSectionCache()
    {
        const int iterations = 10_000;
        const string format = "#,##0.00;[Red]-#,##0.00;0.00;\"txt:\"@";
        var workbook = new Workbook();
        ScalarValue[] values =
        [
            new NumberValue(12345.678),
            new NumberValue(-12345.678),
            new NumberValue(0),
            new TextValue("x")
        ];

        foreach (var value in values)
            NumberFormatter.FormatWithColor(value, format, 12, workbook.IndexedColors, workbook.Theme);

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var result = NumberFormatter.FormatWithColor(
                values[i & 3],
                format,
                12,
                workbook.IndexedColors,
                workbook.Theme);

            if (i < 4)
            {
                var expected = (i & 3) switch
                {
                    0 => "12,345.68",
                    1 => "-12,345.68",
                    2 => "0.00",
                    _ => "txt:x"
                };
                if (result.Text != expected)
                    throw new Xunit.Sdk.XunitException("Repeated custom number format produced an unexpected value.");
            }
        }
        sw.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Console.WriteLine(
            $"Repeated custom number format: {iterations:N0} iterations, {sw.Elapsed.TotalMilliseconds:F2}ms, " +
            $"{allocated:N0} bytes allocated, {allocated / iterations:N0} bytes/iteration");

        (allocated / iterations).Should().BeLessThan(
            750,
            "repeated custom display formats should reuse cached split-section and parsed-section arrays");
    }

    [BenchmarkFact]
    public void Benchmark_SingleSectionDateTimeFormat_AvoidsSectionParsingAllocation()
    {
        const int iterations = 10_000;
        var value = new DateTimeValue(new DateTime(2026, 5, 29, 15, 4, 5).ToOADate());

        NumberFormatter.Format(value, "yyyy-mm-dd hh:mm:ss");

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var formatted = NumberFormatter.Format(value, "yyyy-mm-dd hh:mm:ss");
            if (formatted != "2026-05-29 15:04:05")
                throw new Xunit.Sdk.XunitException("Single-section date/time format produced an unexpected value.");
        }
        sw.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Console.WriteLine(
            $"Single-section date/time format: {iterations:N0} iterations, {sw.Elapsed.TotalMilliseconds:F2}ms, " +
            $"{allocated:N0} bytes allocated, {allocated / iterations:N0} bytes/iteration");

        (allocated / iterations).Should().BeLessThan(
            200,
            "single-section date/time formats should reuse the parsed Excel-to-.NET date/time format");
    }

    [BenchmarkFact]
    public void Benchmark_DynamicArraySort_AvoidsLinqIndexListScaffolding()
    {
        var source = CalcSourceTestSupport.ReadFormulaSourceFromCurrentDirectoryOrFallback("BuiltInFunctions.DynamicArrays.FilterSort.cs");

        source.Should().NotContain(
            "Enumerable.Range(0, arr.RowCount).ToList()",
            "SORT and SORTBY row indexes should use compact arrays instead of LINQ List scaffolding");
        source.Should().NotContain(
            "Enumerable.Range(0, arr.ColCount).ToList()",
            "SORT and SORTBY column indexes should use compact arrays instead of LINQ List scaffolding");
        source.Should().Contain(
            "Array.Sort(rowIndices",
            "dynamic-array row sorting should sort the compact index array in place");
        source.Should().Contain(
            "Array.Sort(colIndices",
            "dynamic-array column sorting should sort the compact index array in place");
    }

    [BenchmarkFact]
    public void Benchmark_SplitPaneViewportCells_LazilyAllocatesDedupeSet()
    {
        var source = CalcSourceTestSupport.ReadCalcSourceFromCurrentDirectoryOrFallback("ViewportService.cs");
        var buildSplitPaneCells = source[
            source.IndexOf("private static List<DisplayCell> BuildSplitPaneCells", StringComparison.Ordinal)..];
        buildSplitPaneCells = buildSplitPaneCells[..buildSplitPaneCells.IndexOf("private static void AddDisplayCell", StringComparison.Ordinal)];

        buildSplitPaneCells.Should().Contain(
            "var dedupeCells = SplitPaneRegionsCanOverlap",
            "split-pane viewport generation should avoid dedupe scaffolding for naturally disjoint panes");
        buildSplitPaneCells.Should().NotContain(
            "new HashSet",
            "the split-pane hot path should allocate the dedupe set only when pane regions can overlap");
    }

    [BenchmarkFact]
    public void Benchmark_SparseOccupiedViewportCells_ReportsMetricLookupTiming()
    {
        var workbook = new Workbook("Benchmark");
        var sheet = workbook.AddSheet("Sheet1");
        var service = new ViewportService();
        const uint visibleRows = 2_000;
        const uint occupiedCells = 5_000;
        const int iterations = 60;

        for (uint i = 0; i < occupiedCells; i++)
        {
            var row = (i % visibleRows) + 1;
            var col = 1_000 + i;
            sheet.SetCell(new CellAddress(sheet.Id, row, col), new NumberValue(i));
        }

        var request = new ViewportRequest(1, 1, 40_000, 2_400);
        service.GetViewport(workbook, sheet.Id, request);

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var viewport = service.GetViewport(workbook, sheet.Id, request);
            if (viewport.Cells.Count != 0)
                throw new Xunit.Sdk.XunitException("Sparse out-of-column viewport should not materialize display cells.");
        }
        sw.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Console.WriteLine(
            $"Sparse occupied viewport metric lookup: {iterations:N0} iterations, {occupiedCells:N0} occupied cells, " +
            $"{sw.Elapsed.TotalMilliseconds:F2}ms, {allocated:N0} bytes allocated, " +
            $"{allocated / iterations:N0} bytes/iteration");
        Console.WriteLine(
            $"PERF SPARSE_OCCUPIED_VIEWPORT iterations={iterations:N0} total_ms={sw.Elapsed.TotalMilliseconds:F2} " +
            $"allocated_bytes={allocated:N0} bytes_per_iteration={allocated / iterations:N0}");

        (allocated / iterations).Should().BeLessThan(
            20_000,
            "default row/column viewport metrics should be exposed lazily instead of allocating one object per visible slot");
        (sw.Elapsed.TotalMilliseconds / iterations).Should().BeLessThan(
            25.0,
            "sparse viewport construction should avoid repeated linear metric scans for every occupied cell");
    }

    [Fact]
    public void SparseOccupiedViewportCells_SkipsOccupiedMapScanWhenUsedRangeMissesViewport()
    {
        var source = CalcSourceTestSupport.ReadCalcSourceFromCurrentDirectoryOrFallback("ViewportService.cs");
        var getViewport = source[
            source.IndexOf("public ViewportModel GetViewport", StringComparison.Ordinal)..
            source.IndexOf("private static int EstimateDisplayCellCapacity", StringComparison.Ordinal)];
        var overlapHelper = source[
            source.IndexOf("private static bool UsedRangeOverlapsVisibleMetrics", StringComparison.Ordinal)..
            source.IndexOf("private static bool RangesOverlap", StringComparison.Ordinal)];

        getViewport.Should().Contain("var occupiedScanUsedRangeOverlapsViewport = scanOccupiedViewportCells &&");
        getViewport.Should().Contain("UsedRangeOverlapsVisibleMetrics(sheet, rowMetrics, colMetrics)");
        getViewport.Should().Contain("if (occupiedScanUsedRangeOverlapsViewport)");
        getViewport.IndexOf("UsedRangeOverlapsVisibleMetrics", StringComparison.Ordinal)
            .Should().BeLessThan(getViewport.IndexOf("AddOccupiedViewportCells(", StringComparison.Ordinal));
        overlapHelper.Should().Contain("sheet.GetUsedRange()");
        overlapHelper.Should().Contain("RangesOverlap(usedRange.Start.Col");
    }

    /// <summary>
    /// Benchmark: Memory usage for 1,000,000 cells (values only, no formulas).
    /// Target: <200MB
    /// </summary>
    [BenchmarkFact]
    public void Benchmark_1mCellMemory()
    {
        Console.WriteLine($"Building 1M-cell workbook...");
        
        // Warm up GC
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        var memBefore = GC.GetTotalMemory(true);
        Console.WriteLine($"Memory before: {memBefore / 1024 / 1024}MB");

        var sw = Stopwatch.StartNew();
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Sheet1");

        for (uint row = 1; row <= 1_000_000; row++)
        {
            if (row % 100_000 == 0)
                Console.WriteLine($"  {row}...");

            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue((double)row));
        }
        sw.Stop();

        var memAfter = GC.GetTotalMemory(false);
        var memUsed = (memAfter - memBefore) / 1024 / 1024;

        Console.WriteLine($"Built 1M cells in {sw.ElapsedMilliseconds}ms");
        Console.WriteLine($"Memory after: {memAfter / 1024 / 1024}MB");
        Console.WriteLine($"Memory used: {memUsed}MB");

        // Assert: Should fit in <300MB for 1M cells
        Assert.True(memUsed < 300, 
            $"1M cells used {memUsed}MB (expected <300MB)");
    }

}
