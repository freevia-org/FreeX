using System.Diagnostics;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public partial class InsertDeleteRowsTests
{
    [Fact]
    public void Benchmark_InsertRowsWithDenseMovedCells_ReportsTiming()
    {
        const int iterations = 3;
        var (workbook, sheet, ctx) = SetupDenseShiftWorkbook();

        var warmup = new InsertRowsCommand(sheet.Id, beforeRow: DenseShiftBeforeRow);
        warmup.Apply(ctx).Success.Should().BeTrue();
        warmup.Revert(ctx);
        sheet.CellCount.Should().Be(DenseShiftRows * DenseShiftColumns);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var timings = new List<double>(iterations);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var total = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var command = new InsertRowsCommand(sheet.Id, beforeRow: DenseShiftBeforeRow);
            var step = Stopwatch.StartNew();
            command.Apply(ctx).Success.Should().BeTrue();
            command.Revert(ctx);
            step.Stop();
            timings.Add(step.Elapsed.TotalMilliseconds);
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var ordered = timings.OrderBy(value => value).ToArray();
        var p95 = ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)];

        workbook.SheetCount.Should().Be(1);
        sheet.CellCount.Should().Be(DenseShiftRows * DenseShiftColumns);
        sheet.GetValue(1, 1).Should().Be(new NumberValue(1001));
        sheet.GetValue(DenseShiftRows, DenseShiftColumns).Should().Be(new NumberValue(DenseShiftRows * 1000 + DenseShiftColumns));
        Console.WriteLine(
            "PERF INSERT_ROWS_DENSE_SHIFT " +
            $"rows={DenseShiftRows} cols={DenseShiftColumns} before_row={DenseShiftBeforeRow} " +
            $"moved_cells={(DenseShiftRows - DenseShiftBeforeRow + 1) * DenseShiftColumns} steps={iterations} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={timings.Average():F2} " +
            $"p95_ms={p95:F2} max_ms={ordered[^1]:F2} allocated_bytes={allocatedBytes:N0}");

        timings.Average().Should().BeGreaterThan(0);
    }

    [Fact]
    public void Benchmark_DeleteRowsWithDenseMovedCells_ReportsTiming()
    {
        const int iterations = 3;
        var (workbook, sheet, ctx) = SetupDenseShiftWorkbook();

        var warmup = new DeleteRowsCommand(sheet.Id, startRow: DenseShiftBeforeRow);
        warmup.Apply(ctx).Success.Should().BeTrue();
        warmup.Revert(ctx);
        sheet.CellCount.Should().Be(DenseShiftRows * DenseShiftColumns);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var timings = new List<double>(iterations);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var total = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var command = new DeleteRowsCommand(sheet.Id, startRow: DenseShiftBeforeRow);
            var step = Stopwatch.StartNew();
            command.Apply(ctx).Success.Should().BeTrue();
            command.Revert(ctx);
            step.Stop();
            timings.Add(step.Elapsed.TotalMilliseconds);
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var ordered = timings.OrderBy(value => value).ToArray();
        var p95 = ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)];

        workbook.SheetCount.Should().Be(1);
        sheet.CellCount.Should().Be(DenseShiftRows * DenseShiftColumns);
        sheet.GetValue(1, 1).Should().Be(new NumberValue(1001));
        sheet.GetValue(DenseShiftRows, DenseShiftColumns).Should().Be(new NumberValue(DenseShiftRows * 1000 + DenseShiftColumns));
        Console.WriteLine(
            "PERF DELETE_ROWS_DENSE_SHIFT " +
            $"rows={DenseShiftRows} cols={DenseShiftColumns} start_row={DenseShiftBeforeRow} " +
            $"shifted_cells={(DenseShiftRows - DenseShiftBeforeRow) * DenseShiftColumns} steps={iterations} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={timings.Average():F2} " +
            $"p95_ms={p95:F2} max_ms={ordered[^1]:F2} allocated_bytes={allocatedBytes:N0}");

        timings.Average().Should().BeGreaterThan(0);
    }

    [Fact]
    public void Benchmark_InsertRowsWithDenseTailMovedCells_ReportsTiming()
    {
        const int iterations = 3;
        const uint beforeRow = DenseShiftRows - 9;
        var (workbook, sheet, ctx) = SetupDenseShiftWorkbook();

        var warmup = new InsertRowsCommand(sheet.Id, beforeRow);
        warmup.Apply(ctx).Success.Should().BeTrue();
        warmup.Revert(ctx);
        sheet.CellCount.Should().Be(DenseShiftRows * DenseShiftColumns);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var timings = new List<double>(iterations);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var total = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var command = new InsertRowsCommand(sheet.Id, beforeRow);
            var step = Stopwatch.StartNew();
            command.Apply(ctx).Success.Should().BeTrue();
            command.Revert(ctx);
            step.Stop();
            timings.Add(step.Elapsed.TotalMilliseconds);
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var ordered = timings.OrderBy(value => value).ToArray();
        var p95 = ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)];

        workbook.SheetCount.Should().Be(1);
        sheet.CellCount.Should().Be(DenseShiftRows * DenseShiftColumns);
        sheet.GetValue(1, 1).Should().Be(new NumberValue(1001));
        sheet.GetValue(DenseShiftRows, DenseShiftColumns).Should().Be(new NumberValue(DenseShiftRows * 1000 + DenseShiftColumns));
        Console.WriteLine(
            "PERF INSERT_ROWS_DENSE_TAIL_SHIFT " +
            $"rows={DenseShiftRows} cols={DenseShiftColumns} before_row={beforeRow} " +
            $"moved_cells={(DenseShiftRows - beforeRow + 1) * DenseShiftColumns} steps={iterations} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={timings.Average():F2} " +
            $"p95_ms={p95:F2} max_ms={ordered[^1]:F2} allocated_bytes={allocatedBytes:N0}");

        timings.Average().Should().BeGreaterThan(0);
    }

    [Fact]
    public void Benchmark_InsertRowsWithDenseRowMetadata_ReportsTiming()
    {
        const int iterations = 3;
        var (workbook, sheet, ctx) = SetupDenseRowMetadataWorkbook();

        var warmup = new InsertRowsCommand(sheet.Id, beforeRow: DenseMetadataStartRow);
        warmup.Apply(ctx).Success.Should().BeTrue();
        warmup.Revert(ctx);
        sheet.RowHeights.Should().HaveCount(DenseMetadataRows);
        sheet.Comments.Should().HaveCount(DenseMetadataRows);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var timings = new List<double>(iterations);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var total = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var command = new InsertRowsCommand(sheet.Id, beforeRow: DenseMetadataStartRow);
            var step = Stopwatch.StartNew();
            command.Apply(ctx).Success.Should().BeTrue();
            command.Revert(ctx);
            step.Stop();
            timings.Add(step.Elapsed.TotalMilliseconds);
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var ordered = timings.OrderBy(value => value).ToArray();
        var p95 = ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)];

        workbook.SheetCount.Should().Be(1);
        sheet.RowHeights.Should().HaveCount(DenseMetadataRows);
        sheet.Comments.Should().ContainKey(new CellAddress(sheet.Id, DenseMetadataRows, 1));
        sheet.ThreadedComments.Should().ContainKey(new CellAddress(sheet.Id, DenseMetadataRows, 2));
        sheet.Hyperlinks.Should().ContainKey(new CellAddress(sheet.Id, DenseMetadataRows, 3));
        sheet.HyperlinkMetadata.Should().ContainKey(new CellAddress(sheet.Id, DenseMetadataRows, 3));
        Console.WriteLine(
            "PERF INSERT_ROWS_METADATA_SHIFT " +
            $"rows={DenseMetadataRows} before_row={DenseMetadataStartRow} steps={iterations} " +
            $"metadata_entries={DenseMetadataRows * 6} total_ms={total.Elapsed.TotalMilliseconds:F2} " +
            $"mean_ms={timings.Average():F2} p95_ms={p95:F2} max_ms={ordered[^1]:F2} " +
            $"allocated_bytes={allocatedBytes:N0}");

        timings.Average().Should().BeGreaterThan(0);
    }

    [Fact]
    public void Benchmark_DeleteRowsWithDenseRowMetadata_ReportsTiming()
    {
        const int iterations = 3;
        var (workbook, sheet, ctx) = SetupDenseRowMetadataWorkbook();

        var warmup = new DeleteRowsCommand(sheet.Id, startRow: DenseMetadataStartRow);
        warmup.Apply(ctx).Success.Should().BeTrue();
        warmup.Revert(ctx);
        sheet.RowHeights.Should().HaveCount(DenseMetadataRows);
        sheet.Comments.Should().HaveCount(DenseMetadataRows);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var timings = new List<double>(iterations);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var total = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var command = new DeleteRowsCommand(sheet.Id, startRow: DenseMetadataStartRow);
            var step = Stopwatch.StartNew();
            command.Apply(ctx).Success.Should().BeTrue();
            command.Revert(ctx);
            step.Stop();
            timings.Add(step.Elapsed.TotalMilliseconds);
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var ordered = timings.OrderBy(value => value).ToArray();
        var p95 = ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)];

        workbook.SheetCount.Should().Be(1);
        sheet.RowHeights.Should().HaveCount(DenseMetadataRows);
        sheet.Comments.Should().ContainKey(new CellAddress(sheet.Id, DenseMetadataRows, 1));
        sheet.ThreadedComments.Should().ContainKey(new CellAddress(sheet.Id, DenseMetadataRows, 2));
        sheet.Hyperlinks.Should().ContainKey(new CellAddress(sheet.Id, DenseMetadataRows, 3));
        sheet.HyperlinkMetadata.Should().ContainKey(new CellAddress(sheet.Id, DenseMetadataRows, 3));
        Console.WriteLine(
            "PERF DELETE_ROWS_METADATA_SHIFT " +
            $"rows={DenseMetadataRows} start_row={DenseMetadataStartRow} steps={iterations} " +
            $"metadata_entries={DenseMetadataRows * 6} total_ms={total.Elapsed.TotalMilliseconds:F2} " +
            $"mean_ms={timings.Average():F2} p95_ms={p95:F2} max_ms={ordered[^1]:F2} " +
            $"allocated_bytes={allocatedBytes:N0}");

        timings.Average().Should().BeGreaterThan(0);
    }

}
