using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using System.Diagnostics;

namespace FreeX.Core.Model.Tests;

public sealed class WatchWindowServiceTests
{
    [Fact]
    public void AddWatch_AddsCellOnceAndGetEntriesReportsCurrentValueAndFormula()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, new Cell { FormulaText = "B1*2", Value = new NumberValue(10) });

        WatchWindowService.AddWatch(workbook, address).Should().BeTrue();
        WatchWindowService.AddWatch(workbook, address).Should().BeFalse();

        var entry = WatchWindowService.GetEntries(workbook).Should().ContainSingle().Subject;
        entry.SheetName.Should().Be("Sheet1");
        entry.Address.Should().Be(address);
        entry.FormulaText.Should().Be("=B1*2");
        entry.ValueText.Should().Be("10");
    }

    [Fact]
    public void RemoveWatch_RemovesWatchedCell()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        WatchWindowService.AddWatch(workbook, address);

        WatchWindowService.RemoveWatch(workbook, address).Should().BeTrue();

        WatchWindowService.GetEntries(workbook).Should().BeEmpty();
    }

    [Fact]
    public void RemoveWatches_RemovesEveryWatchedCellInSelection()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var first = new CellAddress(sheet.Id, 1, 1);
        var second = new CellAddress(sheet.Id, 1, 2);
        var third = new CellAddress(sheet.Id, 2, 1);
        var fourth = new CellAddress(sheet.Id, 2, 2);
        var outside = new CellAddress(sheet.Id, 3, 1);
        foreach (var address in new[] { first, second, third, outside })
            WatchWindowService.AddWatch(workbook, address);

        var removed = WatchWindowService.RemoveWatches(workbook, new GridRange(first, fourth));

        removed.Should().Be(3);
        workbook.WatchedCells.Should().ContainSingle().Which.Should().Be(outside);
    }

    [Fact]
    public void RemoveWatches_SkipsUnwatchedCellsInSelection()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var watched = new CellAddress(sheet.Id, 1, 2);
        WatchWindowService.AddWatch(workbook, watched);

        var removed = WatchWindowService.RemoveWatches(
            workbook,
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2)));

        removed.Should().Be(1);
        workbook.WatchedCells.Should().BeEmpty();
    }

    [Fact]
    public void AddWatches_AddsEveryCellInSelectionAndSkipsExistingWatches()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var first = new CellAddress(sheet.Id, 1, 1);
        var second = new CellAddress(sheet.Id, 1, 2);
        var third = new CellAddress(sheet.Id, 2, 1);
        var fourth = new CellAddress(sheet.Id, 2, 2);
        WatchWindowService.AddWatch(workbook, second);

        var added = WatchWindowService.AddWatches(workbook, new GridRange(first, fourth));

        added.Should().Be(3);
        workbook.WatchedCells.Should().Equal(second, first, third, fourth);
    }

    [Fact]
    public void GetEntries_ReturnsWatchesInWorkbookSheetAndCellOrder()
    {
        var workbook = new Workbook("test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        var sheet1B2 = new CellAddress(sheet1.Id, 2, 2);
        var sheet1A1 = new CellAddress(sheet1.Id, 1, 1);
        var sheet2A1 = new CellAddress(sheet2.Id, 1, 1);

        WatchWindowService.AddWatch(workbook, sheet2A1);
        WatchWindowService.AddWatch(workbook, sheet1B2);
        WatchWindowService.AddWatch(workbook, sheet1A1);

        WatchWindowService.GetEntries(workbook).Select(entry => entry.Address)
            .Should().Equal(sheet1A1, sheet1B2, sheet2A1);
    }

    [Fact]
    public void WatchWindowEntry_IsValueTypedToAvoidPerEntryAllocations()
    {
        typeof(WatchWindowEntry).IsValueType.Should().BeTrue();
    }

    [Fact]
    public void GetDeleteTargets_ReturnsDistinctSelectedAddressesInSelectionOrder()
    {
        var sheet = SheetId.New();
        var first = new CellAddress(sheet, 1, 1);
        var second = new CellAddress(sheet, 2, 1);
        var fallback = new CellAddress(sheet, 3, 1);

        WatchWindowService.GetDeleteTargets([first, second, first], fallback)
            .Should().Equal(first, second);
    }

    [Fact]
    public void GetDeleteTargets_UsesFallbackWhenSelectionIsEmpty()
    {
        var sheet = SheetId.New();
        var fallback = new CellAddress(sheet, 3, 1);

        WatchWindowService.GetDeleteTargets([], fallback)
            .Should().ContainSingle().Which.Should().Be(fallback);
    }

    [BenchmarkFact]
    [Trait("Category", "Benchmark")]
    public void Benchmark_AddWatchesDenseSelection_ReportsTimingAndAllocatedBytes()
    {
        var workbook = new Workbook("watch");
        var sheet = workbook.AddSheet("Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 120, 120));

        var warmup = new Workbook("warmup");
        var warmupSheet = warmup.AddSheet("Sheet1");
        WatchWindowService.AddWatches(warmup, new GridRange(
            new CellAddress(warmupSheet.Id, 1, 1),
            new CellAddress(warmupSheet.Id, 1, 1)));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        var added = WatchWindowService.AddWatches(workbook, range);
        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Console.WriteLine(
            "PERF WATCHWINDOW_ADD_DENSE_SELECTION " +
            $"rows={range.RowCount} cols={range.ColCount} total_ms={stopwatch.Elapsed.TotalMilliseconds:F2} " +
            $"allocated_bytes={allocatedBytes:N0} added={added}");

        added.Should().Be((int)range.CellCount);
    }

    [BenchmarkFact]
    [Trait("Category", "Benchmark")]
    public void Benchmark_RemoveWatchesSparseSelection_ReportsTimingAndAllocatedBytes()
    {
        var workbook = new Workbook("watch");
        var sheet = workbook.AddSheet("Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 240, 240));

        for (uint row = 1; row <= 240; row += 2)
        {
            for (uint col = 1; col <= 8; col++)
                workbook.WatchedCells.Add(new CellAddress(sheet.Id, row, col));
        }

        var watched = workbook.WatchedCells.Count;

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        var removed = WatchWindowService.RemoveWatches(workbook, range);
        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Console.WriteLine(
            "PERF WATCHWINDOW_REMOVE_SPARSE_SELECTION " +
            $"rows={range.RowCount} cols={range.ColCount} watched={watched} total_ms={stopwatch.Elapsed.TotalMilliseconds:F2} " +
            $"allocated_bytes={allocatedBytes:N0} removed={removed}");

        removed.Should().Be(watched);
        workbook.WatchedCells.Should().BeEmpty();
    }

    [BenchmarkFact]
    [Trait("Category", "Benchmark")]
    public void Benchmark_GetEntriesManyWatches_ReportsTimingAndAllocatedBytes()
    {
        const int sheetCount = 8;
        const int rowsPerSheet = 250;
        const int columnsPerSheet = 6;
        const int iterations = 5;
        var workbook = new Workbook("watch");

        for (var sheetIndex = 0; sheetIndex < sheetCount; sheetIndex++)
        {
            var sheet = workbook.AddSheet($"Sheet{sheetIndex + 1}");
            for (uint row = 1; row <= rowsPerSheet; row++)
            {
                for (uint col = 1; col <= columnsPerSheet; col++)
                {
                    var address = new CellAddress(sheet.Id, row, col);
                    sheet.SetCell(address, new NumberValue(row * 1000 + col));
                    workbook.WatchedCells.Add(address);
                }
            }
        }

        workbook.WatchedCells.Reverse();
        WatchWindowService.GetEntries(workbook).Should().HaveCount(workbook.WatchedCells.Count);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var timings = new List<double>(iterations);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var total = Stopwatch.StartNew();
        IReadOnlyList<WatchWindowEntry> entries = [];
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            var step = Stopwatch.StartNew();
            entries = WatchWindowService.GetEntries(workbook);
            step.Stop();
            timings.Add(step.Elapsed.TotalMilliseconds);
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var ordered = timings.OrderBy(value => value).ToArray();
        var p95 = ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)];

        Console.WriteLine(
            "PERF WATCHWINDOW_GET_ENTRIES_MANY " +
            $"sheets={sheetCount} watched={workbook.WatchedCells.Count} steps={iterations} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={timings.Average():F2} p95_ms={p95:F2} max_ms={ordered[^1]:F2} " +
            $"allocated_bytes={allocatedBytes:N0} entries={entries.Count}");

        entries.Should().HaveCount(workbook.WatchedCells.Count);
    }
}
