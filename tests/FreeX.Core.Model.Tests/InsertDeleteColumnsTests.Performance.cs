using System.Diagnostics;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public partial class InsertDeleteColumnsTests
{
    [BenchmarkFact]
    public void Benchmark_InsertColumnsWithDenseMovedCells_ReportsTiming()
    {
        const int iterations = 3;
        var (workbook, sheet, ctx) = SetupDenseShiftWorkbook();

        var warmup = new InsertColumnsCommand(sheet.Id, beforeCol: DenseShiftBeforeColumn);
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
            var command = new InsertColumnsCommand(sheet.Id, beforeCol: DenseShiftBeforeColumn);
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
            "PERF INSERT_COLUMNS_DENSE_SHIFT " +
            $"rows={DenseShiftRows} cols={DenseShiftColumns} before_col={DenseShiftBeforeColumn} " +
            $"moved_cells={DenseShiftRows * (DenseShiftColumns - DenseShiftBeforeColumn + 1)} steps={iterations} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={timings.Average():F2} " +
            $"p95_ms={p95:F2} max_ms={ordered[^1]:F2} allocated_bytes={allocatedBytes:N0}");

        timings.Average().Should().BeGreaterThan(0);
    }

    [BenchmarkFact]
    public void Benchmark_DeleteColumnsWithDenseMovedCells_ReportsTiming()
    {
        const int iterations = 3;
        var (workbook, sheet, ctx) = SetupDenseShiftWorkbook();

        var warmup = new DeleteColumnsCommand(sheet.Id, startCol: DenseShiftBeforeColumn);
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
            var command = new DeleteColumnsCommand(sheet.Id, startCol: DenseShiftBeforeColumn);
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
            "PERF DELETE_COLUMNS_DENSE_SHIFT " +
            $"rows={DenseShiftRows} cols={DenseShiftColumns} start_col={DenseShiftBeforeColumn} " +
            $"moved_cells={DenseShiftRows * (DenseShiftColumns - DenseShiftBeforeColumn)} steps={iterations} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={timings.Average():F2} " +
            $"p95_ms={p95:F2} max_ms={ordered[^1]:F2} allocated_bytes={allocatedBytes:N0}");

        timings.Average().Should().BeGreaterThan(0);
    }

    [BenchmarkFact]
    public void Benchmark_InsertColumnsWithDenseColumnMetadata_ReportsTiming()
    {
        const int iterations = 3;
        var (workbook, sheet, ctx) = SetupDenseColumnMetadataWorkbook();

        var warmup = new InsertColumnsCommand(sheet.Id, beforeCol: DenseMetadataStartColumn);
        warmup.Apply(ctx).Success.Should().BeTrue();
        warmup.Revert(ctx);
        sheet.ColumnWidths.Should().HaveCount(DenseMetadataColumns);
        sheet.Comments.Should().HaveCount(DenseMetadataColumns);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var timings = new List<double>(iterations);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var total = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var command = new InsertColumnsCommand(sheet.Id, beforeCol: DenseMetadataStartColumn);
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
        sheet.ColumnWidths.Should().HaveCount(DenseMetadataColumns);
        sheet.Comments.Should().ContainKey(new CellAddress(sheet.Id, 1, DenseMetadataColumns));
        sheet.ThreadedComments.Should().ContainKey(new CellAddress(sheet.Id, 2, DenseMetadataColumns));
        sheet.Hyperlinks.Should().ContainKey(new CellAddress(sheet.Id, 3, DenseMetadataColumns));
        sheet.HyperlinkMetadata.Should().ContainKey(new CellAddress(sheet.Id, 3, DenseMetadataColumns));
        Console.WriteLine(
            "PERF INSERT_COLUMNS_METADATA_SHIFT " +
            $"cols={DenseMetadataColumns} before_col={DenseMetadataStartColumn} steps={iterations} " +
            $"metadata_entries={DenseMetadataColumns * 6} total_ms={total.Elapsed.TotalMilliseconds:F2} " +
            $"mean_ms={timings.Average():F2} p95_ms={p95:F2} max_ms={ordered[^1]:F2} " +
            $"allocated_bytes={allocatedBytes:N0}");

        timings.Average().Should().BeGreaterThan(0);
    }

    [BenchmarkFact]
    public void Benchmark_DeleteColumnsWithDenseColumnMetadata_ReportsTiming()
    {
        const int iterations = 3;
        var (workbook, sheet, ctx) = SetupDenseColumnMetadataWorkbook();

        var warmup = new DeleteColumnsCommand(sheet.Id, startCol: DenseMetadataStartColumn);
        warmup.Apply(ctx).Success.Should().BeTrue();
        warmup.Revert(ctx);
        sheet.ColumnWidths.Should().HaveCount(DenseMetadataColumns);
        sheet.Comments.Should().HaveCount(DenseMetadataColumns);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var timings = new List<double>(iterations);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var total = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var command = new DeleteColumnsCommand(sheet.Id, startCol: DenseMetadataStartColumn);
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
        sheet.ColumnWidths.Should().HaveCount(DenseMetadataColumns);
        sheet.Comments.Should().ContainKey(new CellAddress(sheet.Id, 1, DenseMetadataColumns));
        sheet.ThreadedComments.Should().ContainKey(new CellAddress(sheet.Id, 2, DenseMetadataColumns));
        sheet.Hyperlinks.Should().ContainKey(new CellAddress(sheet.Id, 3, DenseMetadataColumns));
        sheet.HyperlinkMetadata.Should().ContainKey(new CellAddress(sheet.Id, 3, DenseMetadataColumns));
        Console.WriteLine(
            "PERF DELETE_COLUMNS_METADATA_SHIFT " +
            $"cols={DenseMetadataColumns} start_col={DenseMetadataStartColumn} steps={iterations} " +
            $"metadata_entries={DenseMetadataColumns * 6} total_ms={total.Elapsed.TotalMilliseconds:F2} " +
            $"mean_ms={timings.Average():F2} p95_ms={p95:F2} max_ms={ordered[^1]:F2} " +
            $"allocated_bytes={allocatedBytes:N0}");

        timings.Average().Should().BeGreaterThan(0);
    }
}
