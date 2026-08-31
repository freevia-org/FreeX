using System.Diagnostics;
using System.Globalization;
using System.Text;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit.Abstractions;

namespace FreeX.Core.IO.Tests;

public sealed partial class CsvFileAdapterTests
{
    [Fact]
    public void Save_WarmedSparseRowsHasBoundedAllocation()
    {
        const int rowCount = 5_000;
        const int colCount = 2_000;
        const int cellsPerRow = 3;
        const long allocationLimit = 4_000_000;
        var workbook = CreateSparseWideWorkbook(rowCount, colCount, cellsPerRow);
        var adapter = new CsvFileAdapter();

        adapter.Save(workbook, Stream.Null);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        adapter.Save(workbook, Stream.Null);
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        output.WriteLine(
            $"CSV warmed sparse save allocation: rows={rowCount}, cols={colCount}, cellsPerRow={cellsPerRow}, allocatedBytes={allocatedBytes}");
        allocatedBytes.Should().BeLessThan(
            allocationLimit,
            "saving a sparse snapshot should not duplicate every cell into per-row buckets");
    }

    [BenchmarkFact]
    public void Save_DenseSyntheticSheet_ReportsThroughputAndAllocatedBytes()
    {
        const int rowCount = 300;
        const int colCount = 120;
        var workbook = CreateDenseWorkbook(rowCount, colCount);
        var adapter = new CsvFileAdapter();

        using (var warmup = new MemoryStream(rowCount * colCount * 12))
        {
            adapter.Save(workbook, warmup);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        using var stream = new MemoryStream(rowCount * colCount * 12);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        adapter.Save(workbook, stream);
        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        output.WriteLine(
            $"CSV dense save benchmark: rows={rowCount}, cols={colCount}, bytes={stream.Length}, elapsedMs={stopwatch.Elapsed.TotalMilliseconds:F2}, allocatedBytes={allocatedBytes}");
        stream.Length.Should().BeGreaterThan(0);
    }

    [BenchmarkFact]
    public void Save_SparseWideSyntheticSheet_ReportsThroughputAndAllocatedBytes()
    {
        const int rowCount = 5_000;
        const int colCount = 2_000;
        const int cellsPerRow = 3;
        var workbook = CreateSparseWideWorkbook(rowCount, colCount, cellsPerRow);
        var adapter = new CsvFileAdapter();
        var expectedCapacity = rowCount * (colCount + 32);

        using (var warmup = new MemoryStream(expectedCapacity))
        {
            adapter.Save(workbook, warmup);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        using var stream = new MemoryStream(expectedCapacity);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        adapter.Save(workbook, stream);
        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        output.WriteLine(
            $"CSV sparse wide save benchmark: rows={rowCount}, cols={colCount}, cellsPerRow={cellsPerRow}, bytes={stream.Length}, elapsedMs={stopwatch.Elapsed.TotalMilliseconds:F2}, allocatedBytes={allocatedBytes}");
        stream.Length.Should().BeGreaterThan(0);
    }

    [BenchmarkFact]
    public void Load_LargeAccessibleMemoryStream_ReportsThroughputAndAllocatedBytes()
    {
        const int rowCount = 20_000;
        const int colCount = 10;
        var bytes = CreateCsvBytes(rowCount, colCount);
        var adapter = new CsvFileAdapter();

        using (var warmup = new MemoryStream(bytes, index: 0, count: bytes.Length, writable: false, publiclyVisible: true))
        {
            adapter.Load(warmup);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        using var stream = new MemoryStream(bytes, index: 0, count: bytes.Length, writable: false, publiclyVisible: true);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        var workbook = adapter.Load(stream);
        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        output.WriteLine(
            $"CSV accessible MemoryStream load benchmark: rows={rowCount}, cols={colCount}, bytes={bytes.Length}, elapsedMs={stopwatch.Elapsed.TotalMilliseconds:F2}, allocatedBytes={allocatedBytes}");
        var sheet = workbook.Sheets.Single();
        sheet.GetValue(new CellAddress(sheet.Id, (uint)rowCount, (uint)colCount))
            .Should().Be(new NumberValue(rowCount * colCount));
        stream.Position.Should().Be(stream.Length);
    }

}
