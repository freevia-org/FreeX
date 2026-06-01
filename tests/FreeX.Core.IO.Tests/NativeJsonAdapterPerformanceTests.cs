using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class NativeJsonAdapterPerformanceTests
{
    [Fact]
    public void Save_SkipsDefaultCellStylePayloadsWhilePreservingCustomStyles()
    {
        var workbook = new Workbook("Native JSON Styles");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(2));
        sheet.GetCell(1, 2)!.StyleId = workbook.RegisterStyle(new CellStyle { Bold = true });

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);

        using var document = JsonDocument.Parse(stream.ToArray());
        var cells = document.RootElement
            .GetProperty("Sheets")[0]
            .GetProperty("Cells")
            .EnumerateArray()
            .ToDictionary(cell => cell.GetProperty("Address").GetString()!);

        cells["A1"].GetProperty("Style").ValueKind.Should().Be(JsonValueKind.Null);
        cells["B1"].GetProperty("Style").GetProperty("Bold").GetBoolean().Should().BeTrue();

        stream.Position = 0;
        var loaded = adapter.Load(stream);
        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.GetCell(1, 1)!.StyleId.Should().Be(StyleId.Default);
        var loadedStyleId = loadedSheet.GetCell(1, 2)!.StyleId;
        loadedStyleId.Should().NotBe(StyleId.Default);
        loaded.GetStyle(loadedStyleId).Bold.Should().BeTrue();
    }

    [Fact]
    public void Benchmark_SaveDenseWorkbook_ReportsTimingAndAllocatedBytes()
    {
        const int iterations = 3;
        var workbook = CreateDenseWorkbook();
        var adapter = new NativeJsonAdapter();

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
            "PERF NATIVE_JSON_SAVE_DENSE " +
            $"sheets={DenseSheetCount} rows={DenseRowsPerSheet} cols={DenseColumnsPerSheet} " +
            $"steps={iterations} bytes={packageSizes.Max():N0} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={timings.Average():F2} " +
            $"p95_ms={p95:F2} max_ms={ordered[^1]:F2} allocated_bytes={allocatedBytes:N0}");

        timings.Average().Should().BeGreaterThan(0);
        allocatedBytes.Should().BeGreaterThan(0);
    }

    private const int DenseSheetCount = 4;
    private const int DenseRowsPerSheet = 160;
    private const int DenseColumnsPerSheet = 80;

    private static Workbook CreateDenseWorkbook()
    {
        var workbook = new Workbook("Native JSON Dense");
        for (var sheetIndex = 1; sheetIndex <= DenseSheetCount; sheetIndex++)
        {
            var sheet = workbook.AddSheet($"Sheet {sheetIndex}");
            for (uint row = 1; row <= DenseRowsPerSheet; row++)
            {
                for (uint col = 1; col <= DenseColumnsPerSheet; col++)
                {
                    var address = new CellAddress(sheet.Id, row, col);
                    if ((row + col + sheetIndex) % 11 == 0)
                    {
                        sheet.SetFormula(address, $"SUM(A{Math.Max(1, row - 1)}:A{row})");
                    }
                    else if ((row + col) % 7 == 0)
                    {
                        sheet.SetCell(address, new TextValue($"S{sheetIndex}-R{row}-C{col}"));
                    }
                    else
                    {
                        sheet.SetCell(address, new NumberValue(row * col + sheetIndex));
                    }
                }
            }

            for (uint row = 1; row <= 120; row++)
                sheet.SetStyleOnly(row, (uint)(DenseColumnsPerSheet + 4), StyleId.Default);
        }

        return workbook;
    }
}
