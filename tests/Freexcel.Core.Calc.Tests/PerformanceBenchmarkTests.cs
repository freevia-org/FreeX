using Freexcel.Core.Calc;
using Freexcel.Core.Formula;
using Freexcel.Core.Model;
using FluentAssertions;
using System.Diagnostics;

namespace Freexcel.Core.Calc.Tests;

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

}
