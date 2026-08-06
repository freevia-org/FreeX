using System.Diagnostics;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class StatusBarCalculatorTests
{
    [BenchmarkFact]
    public void Benchmark_RepeatedWholeColumnStatusCalculations()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        for (uint row = 1; row <= 100_000; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), Cell.FromValue(new NumberValue(row)));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), Cell.FromValue(new NumberValue(row * 2)));
        }

        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 1));

        var cache = new StatusBarStatsCache();
        _ = cache.GetOrCreate(sheet, range, revision: 1, () => WorkbookSelectionStatsCalculator.Calculate(sheet, range));

        var sw = Stopwatch.StartNew();
        WorkbookSelectionStats stats = default;
        for (var i = 0; i < 25; i++)
            stats = cache.GetOrCreate(sheet, range, revision: 1, () => WorkbookSelectionStatsCalculator.Calculate(sheet, range));
        sw.Stop();

        Console.WriteLine($"Repeated cached whole-column status refreshes: {sw.ElapsedMilliseconds}ms for 25 runs");
        stats.Count.Should().Be(100_000);
        stats.NumericalCount.Should().Be(100_000);
        stats.Sum.Should().Be(5_000_050_000d);
    }

    [BenchmarkFact]
    public void Benchmark_ExpandingStatusSelection_ReusesPreviousStats()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        for (uint row = 1; row <= 50_000; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), Cell.FromValue(new NumberValue(row)));

        var cache = new StatusBarStatsCache();
        const int iterations = 2_000;

        var sw = Stopwatch.StartNew();
        WorkbookSelectionStats stats = default;
        for (uint row = 1; row <= iterations; row++)
        {
            var range = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, row, 1));
            stats = cache.GetOrCalculate(sheet, range, revision: 1);
        }
        sw.Stop();

        Console.WriteLine(
            $"Expanding status selection: {iterations:N0} steps, " +
            $"{sw.Elapsed.TotalMilliseconds:F2}ms, final sum {stats.Sum:N0}");

        stats.Count.Should().Be(iterations);
        stats.NumericalCount.Should().Be(iterations);
        stats.Sum.Should().Be(iterations * (iterations + 1) / 2d);
    }

    [BenchmarkFact]
    public void Benchmark_ClippedSparseStatusSelection()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 10_000, 1), Cell.FromValue(new NumberValue(5)));

        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 10_000, 1));

        var sw = Stopwatch.StartNew();
        WorkbookSelectionStats stats = default;
        for (var i = 0; i < 500; i++)
            stats = WorkbookSelectionStatsCalculator.Calculate(sheet, range);
        sw.Stop();

        Console.WriteLine($"Clipped sparse status selection: {sw.ElapsedMilliseconds}ms for 500 runs");
        stats.Should().Be(new WorkbookSelectionStats(5, 1, 1, 5, 5, 5));
    }

    [BenchmarkFact]
    public void Benchmark_BoundedStatusSelectionInLargeOccupiedSheet()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        for (uint row = 1; row <= 100_000; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), Cell.FromValue(new NumberValue(row)));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), Cell.FromValue(new NumberValue(row * 2)));
        }

        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 20_000, 1));

        var sw = Stopwatch.StartNew();
        WorkbookSelectionStats stats = default;
        for (var i = 0; i < 50; i++)
            stats = WorkbookSelectionStatsCalculator.Calculate(sheet, range);
        sw.Stop();

        Console.WriteLine($"Bounded status selection in large occupied sheet: {sw.ElapsedMilliseconds}ms for 50 runs");
        stats.Count.Should().Be(20_000);
        stats.NumericalCount.Should().Be(20_000);
        stats.Sum.Should().Be(200_010_000d);
    }

    [BenchmarkFact]
    public void Benchmark_BoundedStatusSelection_AvoidsAddressIteratorAllocation()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        for (uint row = 1; row <= 1_000; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), Cell.FromValue(new NumberValue(row)));

        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1_000, 1));

        WorkbookSelectionStatsCalculator.Calculate(sheet, range);

        const int iterations = 500;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        WorkbookSelectionStats stats = default;
        for (var i = 0; i < iterations; i++)
            stats = WorkbookSelectionStatsCalculator.Calculate(sheet, range);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Console.WriteLine(
            $"Bounded status allocation: {allocated:N0} bytes for {iterations:N0} runs, " +
            $"{allocated / iterations:N0} bytes/run");

        stats.Sum.Should().Be(500_500d);
        (allocated / iterations).Should().BeLessThan(
            64,
            "bounded status scans should not allocate a Stats object or range iterators");
    }
}
