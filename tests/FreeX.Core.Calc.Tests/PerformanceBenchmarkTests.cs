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
    [Fact]
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
    [Fact]
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

    [Fact]
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
            2_250,
            "exact-only recalc traversal should not allocate a HashSet for every dependency step");
    }

    [Fact]
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
            240_000,
            "exact-only formula chains should not allocate a dedupe HashSet for each formula precedent");
    }

    [Fact]
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

    [Fact]
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
        rebuildAllocated.Should().BeLessThan(10_000_000);
    }

    [Fact]
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

        (allocated / iterations).Should().BeLessThan(
            850,
            "single-section number formats should skip List/StringBuilder section-splitting scaffolding");
    }

    [Fact]
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

    [Fact]
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
            1_600,
            "single-section date/time formats should skip section array and parsed-section scaffolding");
    }

    [Fact]
    public void Benchmark_DynamicArraySort_AvoidsLinqIndexListScaffolding()
    {
        var source = File.ReadAllText(FindRepoFile("src", "FreeX.Core.Formula", "BuiltInFunctions.DynamicArrays.cs"));

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

    [Fact]
    public void Benchmark_SplitPaneViewportCells_LazilyAllocatesDedupeSet()
    {
        var source = File.ReadAllText(FindRepoFile("src", "FreeX.Core.Calc", "ViewportService.cs"));
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

    /// <summary>
    /// Benchmark: Memory usage for 1,000,000 cells (values only, no formulas).
    /// Target: <200MB
    /// </summary>
    [Fact]
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

    private static string FindRepoFile(params string[] relativeParts)
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        return Path.Combine(new[] { Directory.GetCurrentDirectory() }.Concat(relativeParts).ToArray());
    }
}
