using System.Diagnostics;
using FluentAssertions;

namespace FreeX.Core.IO.Tests;

public sealed partial class SpreadsheetXmlFileAdapterTests
{
    [Fact]
    public void Benchmark_SaveDenseWorkbook_ReportsTimingAndAllocatedBytes()
    {
        const int iterations = 3;
        const int sheetCount = 2;
        const int rowCount = 120;
        const int columnCount = 80;
        var workbook = CreateDenseWorkbook(sheetCount, rowCount, columnCount);
        var adapter = new SpreadsheetXmlFileAdapter();

        using (var warmup = new MemoryStream())
            adapter.Save(workbook, warmup);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var timings = new List<double>(iterations);
        var packageSizes = new List<long>(iterations);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var total = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            using var stream = new MemoryStream();
            var step = Stopwatch.StartNew();
            adapter.Save(workbook, stream);
            step.Stop();
            timings.Add(step.Elapsed.TotalMilliseconds);
            packageSizes.Add(stream.Length);
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var ordered = timings.OrderBy(value => value).ToArray();
        var p95 = ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)];

        Console.WriteLine(
            "PERF SPREADSHEET_XML_SAVE_DENSE " +
            $"sheets={sheetCount} rows={rowCount} cols={columnCount} " +
            $"steps={iterations} bytes={packageSizes.Max():N0} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={timings.Average():F2} " +
            $"p95_ms={p95:F2} max_ms={ordered[^1]:F2} allocated_bytes={allocatedBytes:N0}");

        timings.Average().Should().BeGreaterThan(0);
        allocatedBytes.Should().BeGreaterThan(0);
        packageSizes.Should().OnlyContain(size => size > 0);
    }

    [Fact]
    public void Benchmark_SaveRichDenseWorkbook_ReportsTimingAndAllocatedBytes()
    {
        const int iterations = 3;
        const int sheetCount = 2;
        const int rowCount = 120;
        const int columnCount = 80;
        var workbook = CreateRichDenseWorkbook(sheetCount, rowCount, columnCount);
        var adapter = new SpreadsheetXmlFileAdapter();

        using (var warmup = new MemoryStream())
            adapter.Save(workbook, warmup);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var timings = new List<double>(iterations);
        var packageSizes = new List<long>(iterations);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var total = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            using var stream = new MemoryStream();
            var step = Stopwatch.StartNew();
            adapter.Save(workbook, stream);
            step.Stop();
            timings.Add(step.Elapsed.TotalMilliseconds);
            packageSizes.Add(stream.Length);
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var ordered = timings.OrderBy(value => value).ToArray();
        var p95 = ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)];

        Console.WriteLine(
            "PERF SPREADSHEET_XML_SAVE_RICH_DENSE " +
            $"sheets={sheetCount} rows={rowCount} cols={columnCount} " +
            $"steps={iterations} bytes={packageSizes.Max():N0} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={timings.Average():F2} " +
            $"p95_ms={p95:F2} max_ms={ordered[^1]:F2} allocated_bytes={allocatedBytes:N0}");

        timings.Average().Should().BeGreaterThan(0);
        allocatedBytes.Should().BeGreaterThan(0);
        packageSizes.Should().OnlyContain(size => size > 0);
    }

}
