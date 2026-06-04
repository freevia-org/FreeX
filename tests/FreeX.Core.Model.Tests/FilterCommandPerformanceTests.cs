using System.Diagnostics;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class FilterCommandPerformanceTests
{
    [Fact]
    public void FilterCommand_ClearRemovesOnlyFilterHiddenRowsInsideRange()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Status"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Drop"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Keep"));
        sheet.FilterHiddenRows.UnionWith([2u, 10u]);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 1));
        var ctx = new SimpleCtx(wb);

        var outcome = new FilterCommand(sheet.Id, range, 0, []).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().BeEquivalentTo([10u]);
    }

    [BenchmarkFact]
    public void Benchmark_ApplyRegularFilterDenseRows_ReportsTimingAndAllocatedBytes()
    {
        const int rows = 60_000;
        const int steps = 8;

        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Status"));

        for (uint row = 2; row <= rows + 1; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue(row % 100 == 0 ? "Keep" : "Drop"));

        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, (uint)rows + 1, 1));
        var ctx = new SimpleCtx(wb);
        var expectedHiddenRows = rows - rows / 100;

        var warmup = new FilterCommand(sheet.Id, range, 0, ["Keep"]).Apply(ctx);
        warmup.Success.Should().BeTrue();
        sheet.FilterHiddenRows.Count.Should().Be(expectedHiddenRows);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        var timings = new double[steps];
        var total = Stopwatch.StartNew();
        var checksum = 0;

        for (var i = 0; i < steps; i++)
        {
            var command = new FilterCommand(sheet.Id, range, 0, ["Keep"]);
            var step = Stopwatch.StartNew();
            var outcome = command.Apply(ctx);
            step.Stop();

            if (!outcome.Success)
                throw new InvalidOperationException(outcome.ErrorMessage);

            sheet.FilterHiddenRows.Count.Should().Be(expectedHiddenRows);
            checksum += sheet.FilterHiddenRows.Count;
            timings[i] = step.Elapsed.TotalMilliseconds;
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        checksum.Should().Be(expectedHiddenRows * steps);
        Console.WriteLine(
            "PERF REGULAR_FILTER_DENSE " +
            $"rows={rows} steps={steps} hidden_rows={expectedHiddenRows} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} " +
            $"mean_ms={timings.Average():F2} " +
            $"p95_ms={timings.OrderBy(x => x).ElementAt((int)Math.Ceiling(steps * 0.95) - 1):F2} " +
            $"max_ms={timings.Max():F2} " +
            $"allocated_bytes={allocatedBytes:N0}");
    }

    [BenchmarkFact]
    public void Benchmark_ApplyAverageFilterDenseRows_ReportsTimingAndAllocatedBytes()
    {
        const int rows = 60_000;
        const int steps = 8;

        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Score"));

        for (uint row = 2; row <= rows + 1; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row % 1_000));

        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, (uint)rows + 1, 1));
        var ctx = new SimpleCtx(wb);
        const int expectedHiddenRows = 30_000;

        var warmup = new AverageFilterCommand(sheet.Id, range, 0, above: true).Apply(ctx);
        warmup.Success.Should().BeTrue();
        sheet.FilterHiddenRows.Count.Should().Be(expectedHiddenRows);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        var timings = new double[steps];
        var total = Stopwatch.StartNew();
        var checksum = 0;

        for (var i = 0; i < steps; i++)
        {
            var command = new AverageFilterCommand(sheet.Id, range, 0, above: true);
            var step = Stopwatch.StartNew();
            var outcome = command.Apply(ctx);
            step.Stop();

            if (!outcome.Success)
                throw new InvalidOperationException(outcome.ErrorMessage);

            sheet.FilterHiddenRows.Count.Should().Be(expectedHiddenRows);
            checksum += sheet.FilterHiddenRows.Count;
            timings[i] = step.Elapsed.TotalMilliseconds;
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        checksum.Should().Be(expectedHiddenRows * steps);
        allocatedBytes.Should().BeLessThan(1_250_000);
        Console.WriteLine(
            "PERF AVERAGE_FILTER_DENSE " +
            $"rows={rows} steps={steps} hidden_rows={expectedHiddenRows} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} " +
            $"mean_ms={timings.Average():F2} " +
            $"p95_ms={timings.OrderBy(x => x).ElementAt((int)Math.Ceiling(steps * 0.95) - 1):F2} " +
            $"max_ms={timings.Max():F2} " +
            $"allocated_bytes={allocatedBytes:N0}");
    }

    [BenchmarkFact]
    public void Benchmark_ApplyTopBottomFilterDenseRows_ReportsTimingAndAllocatedBytes()
    {
        const int rows = 60_000;
        const int steps = 8;

        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Score"));

        for (uint row = 2; row <= rows + 1; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue((row * 37) % 100_000));

        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, (uint)rows + 1, 1));
        var ctx = new SimpleCtx(wb);
        const int keepRows = 600;
        const int expectedHiddenRows = rows - keepRows;

        var warmup = new TopBottomFilterCommand(sheet.Id, range, 0, keepRows, top: true).Apply(ctx);
        warmup.Success.Should().BeTrue();
        sheet.FilterHiddenRows.Count.Should().Be(expectedHiddenRows);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        var timings = new double[steps];
        var total = Stopwatch.StartNew();
        var checksum = 0;

        for (var i = 0; i < steps; i++)
        {
            var command = new TopBottomFilterCommand(sheet.Id, range, 0, keepRows, top: true);
            var step = Stopwatch.StartNew();
            var outcome = command.Apply(ctx);
            step.Stop();

            if (!outcome.Success)
                throw new InvalidOperationException(outcome.ErrorMessage);

            sheet.FilterHiddenRows.Count.Should().Be(expectedHiddenRows);
            checksum += sheet.FilterHiddenRows.Count;
            timings[i] = step.Elapsed.TotalMilliseconds;
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        checksum.Should().Be(expectedHiddenRows * steps);
        allocatedBytes.Should().BeLessThan(1_000_000);
        Console.WriteLine(
            "PERF TOPBOTTOM_FILTER_DENSE " +
            $"rows={rows} steps={steps} hidden_rows={expectedHiddenRows} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} " +
            $"mean_ms={timings.Average():F2} " +
            $"p95_ms={timings.OrderBy(x => x).ElementAt((int)Math.Ceiling(steps * 0.95) - 1):F2} " +
            $"max_ms={timings.Max():F2} " +
            $"allocated_bytes={allocatedBytes:N0}");
    }

    private sealed class SimpleCtx(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;
        public Sheet GetSheet(SheetId sheetId) => Workbook.GetSheet(sheetId)!;
    }
}
