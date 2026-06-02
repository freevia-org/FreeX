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

        cells["A1"].TryGetProperty("Style", out _).Should().BeFalse();
        cells["A1"].TryGetProperty("StyleId", out _).Should().BeFalse();
        var styleId = cells["B1"].GetProperty("StyleId").GetInt32();
        cells["B1"].TryGetProperty("Style", out _).Should().BeFalse();
        document.RootElement.GetProperty("CellStyles")[styleId].GetProperty("Bold").GetBoolean().Should().BeTrue();

        stream.Position = 0;
        var loaded = adapter.Load(stream);
        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.GetCell(1, 1)!.StyleId.Should().Be(StyleId.Default);
        var loadedStyleId = loadedSheet.GetCell(1, 2)!.StyleId;
        loadedStyleId.Should().NotBe(StyleId.Default);
        loaded.GetStyle(loadedStyleId).Bold.Should().BeTrue();
    }

    [Fact]
    public void Save_StreamsCellAddressesWithoutChangingA1Payload()
    {
        var workbook = new Workbook("Native JSON Address Streaming");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, CellAddress.MaxRow, CellAddress.MaxCol), new TextValue("edge"));
        sheet.SetStyleOnly(12, CellAddress.MaxCol, StyleId.Default);

        using var stream = new MemoryStream();
        new NativeJsonAdapter().Save(workbook, stream);

        using var document = JsonDocument.Parse(stream.ToArray());
        var sheetJson = document.RootElement.GetProperty("Sheets")[0];
        var cellAddresses = sheetJson
            .GetProperty("Cells")
            .EnumerateArray()
            .Select(cell => cell.GetProperty("Address").GetString())
            .ToHashSet();
        var styleOnlyAddress = sheetJson
            .GetProperty("StyleOnlyCells")[0]
            .GetProperty("Address")
            .GetString();

        cellAddresses.Should().Contain(["A1", "XFD1048576"]);
        styleOnlyAddress.Should().Be("XFD12");
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

    [Fact]
    public void Benchmark_LoadDenseWorkbook_ReportsTimingAndAllocatedBytes()
    {
        const int iterations = 3;
        var adapter = new NativeJsonAdapter();
        byte[] payload;
        using (var source = new MemoryStream())
        {
            adapter.Save(CreateDenseWorkbook(), source);
            payload = source.ToArray();
        }

        using (var warmup = new MemoryStream(payload, writable: false))
            adapter.Load(warmup).SheetCount.Should().Be(DenseSheetCount);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var timings = new List<double>(iterations);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var total = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            using var stream = new MemoryStream(payload, writable: false);
            var step = Stopwatch.StartNew();
            var loaded = adapter.Load(stream);
            step.Stop();
            loaded.SheetCount.Should().Be(DenseSheetCount);
            loaded.GetSheetAt(0).CellCount.Should().Be(DenseRowsPerSheet * DenseColumnsPerSheet);
            timings.Add(step.Elapsed.TotalMilliseconds);
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var ordered = timings.OrderBy(value => value).ToArray();
        var p95 = ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)];

        Console.WriteLine(
            "PERF NATIVE_JSON_LOAD_DENSE " +
            $"sheets={DenseSheetCount} rows={DenseRowsPerSheet} cols={DenseColumnsPerSheet} " +
            $"steps={iterations} bytes={payload.Length:N0} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={timings.Average():F2} " +
            $"p95_ms={p95:F2} max_ms={ordered[^1]:F2} allocated_bytes={allocatedBytes:N0}");

        timings.Average().Should().BeGreaterThan(0);
        allocatedBytes.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Benchmark_LoadRepeatedCustomStyles_ReportsTimingAndAllocatedBytes()
    {
        const int iterations = 3;
        var adapter = new NativeJsonAdapter();
        var workbook = CreateRepeatedCustomStyleWorkbook();
        byte[] payload;
        using (var source = new MemoryStream())
        {
            adapter.Save(workbook, source);
            payload = source.ToArray();
        }

        using (var warmup = new MemoryStream(payload, writable: false))
            adapter.Load(warmup);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var timings = new List<double>(iterations);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var total = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            using var stream = new MemoryStream(payload, writable: false);
            var step = Stopwatch.StartNew();
            var loaded = adapter.Load(stream);
            step.Stop();
            loaded.SheetCount.Should().Be(RepeatedStyleSheetCount);
            timings.Add(step.Elapsed.TotalMilliseconds);
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var ordered = timings.OrderBy(value => value).ToArray();
        var p95 = ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)];

        Console.WriteLine(
            "PERF NATIVE_JSON_LOAD_REPEATED_STYLES " +
            $"sheets={RepeatedStyleSheetCount} rows={RepeatedStyleRowsPerSheet} cols={RepeatedStyleColumnsPerSheet} " +
            $"steps={iterations} bytes={payload.Length:N0} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={timings.Average():F2} " +
            $"p95_ms={p95:F2} max_ms={ordered[^1]:F2} allocated_bytes={allocatedBytes:N0}");

        timings.Average().Should().BeGreaterThan(0);
        allocatedBytes.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Benchmark_SaveWorkbookReferences_ReportsTimingAndAllocatedBytes()
    {
        const int iterations = 3;
        var workbook = CreateWorkbookReferencesWorkbook();
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
            "PERF NATIVE_JSON_SAVE_WORKBOOK_REFERENCES " +
            $"sheets={ReferenceSheetCount} watched={ReferenceWatchesPerSheet * ReferenceSheetCount:N0} " +
            $"scenarios={ReferenceScenarioCount:N0} changes_per_scenario={ReferenceScenarioChangesPerScenario:N0} " +
            $"steps={iterations} bytes={packageSizes.Max():N0} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={timings.Average():F2} " +
            $"p95_ms={p95:F2} max_ms={ordered[^1]:F2} allocated_bytes={allocatedBytes:N0}");

        timings.Average().Should().BeGreaterThan(0);
        allocatedBytes.Should().BeGreaterThan(0);
    }

    [Fact]
    public void SaveWorkbookReferences_UsesIndexedSheetLookup()
    {
        var source = File.ReadAllText(FindRepoFile(
            "src",
            "FreeX.Core.IO",
            "NativeJsonAdapter.Save.cs"));

        source.Should().Contain("workbook.GetSheet(address.Sheet)");
        source.Should().Contain("workbook.GetSheet(change.Address.Sheet)");
        source.Should().NotContain("workbook.Sheets.FirstOrDefault(s => s.Id.Equals(address.Sheet))");
        source.Should().NotContain("workbook.Sheets.FirstOrDefault(s => s.Id.Equals(change.Address.Sheet))");
    }

    [Fact]
    public void SaveCellWriters_StreamAddressesInsteadOfAllocatingA1Strings()
    {
        var saveSource = File.ReadAllText(FindRepoFile(
            "src",
            "FreeX.Core.IO",
            "NativeJsonAdapter.Save.cs"));
        var dtoSource = File.ReadAllText(FindRepoFile(
            "src",
            "FreeX.Core.IO",
            "NativeJsonAdapter.Dto.cs"));

        saveSource.Should().Contain("CellDtoJsonConverter.WriteCell(");
        saveSource.Should().Contain("row,");
        saveSource.Should().Contain("col);");
        saveSource.Should().Contain("StyleOnlyCellDtoJsonConverter.WriteCell(writer, dto, options, row, col);");
        saveSource.Should().NotContain("dto.Address = address.ToA1();");
        saveSource.Should().NotContain("dto.Address = new CellAddress(sheet.Id, row, col).ToA1();");
        dtoSource.Should().Contain("WriteStringValue(address[..length])");
    }

    [Fact]
    public void SaveCellWriters_StreamScalarValuesWithoutAllocatingFormattedNumberStrings()
    {
        var saveSource = File.ReadAllText(FindRepoFile(
            "src",
            "FreeX.Core.IO",
            "NativeJsonAdapter.Save.cs"));
        var dtoSource = File.ReadAllText(FindRepoFile(
            "src",
            "FreeX.Core.IO",
            "NativeJsonAdapter.Dto.cs"));

        saveSource.Should().Contain("cell.Value,");
        saveSource.Should().NotContain("SerializeWithType(cell.Value)");
        dtoSource.Should().Contain("WriteSmallIntegerStringValue");
        dtoSource.Should().Contain("Utf8Formatter.TryFormat");
        dtoSource.Should().Contain("WriteRawValue");
    }

    [Fact]
    public void LoadCellReader_MapsValueTypesWithoutAllocatingPerCellStrings()
    {
        var adapterSource = File.ReadAllText(FindRepoFile(
            "src",
            "FreeX.Core.IO",
            "NativeJsonAdapter.cs"));
        var dtoSource = File.ReadAllText(FindRepoFile(
            "src",
            "FreeX.Core.IO",
            "NativeJsonAdapter.Dto.cs"));

        dtoSource.Should().Contain("ParsedValueType");
        dtoSource.Should().Contain("ReadValueTypeToken");
        dtoSource.Should().Contain("reader.ValueTextEquals(NumberValueType)");
        dtoSource.Should().NotContain("dto.ValueType = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();");
        adapterSource.Should().Contain("NativeJsonScalarValueMapper.Deserialize(cDto.Value, cDto.ParsedValueType)");
    }

    private const int DenseSheetCount = 4;
    private const int DenseRowsPerSheet = 160;
    private const int DenseColumnsPerSheet = 80;
    private const int RepeatedStyleSheetCount = 3;
    private const int RepeatedStyleRowsPerSheet = 140;
    private const int RepeatedStyleColumnsPerSheet = 70;
    private const int ReferenceSheetCount = 32;
    private const int ReferenceWatchesPerSheet = 80;
    private const int ReferenceScenarioCount = 240;
    private const int ReferenceScenarioChangesPerScenario = 12;

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

    private static Workbook CreateRepeatedCustomStyleWorkbook()
    {
        var workbook = new Workbook("Native JSON Repeated Styles");
        var boldCurrencyStyleId = workbook.RegisterStyle(new CellStyle
        {
            Bold = true,
            NumberFormat = "$#,##0.00",
            FillColor = CellColor.FromArgb(234, 242, 255)
        });
        var wrappedPercentStyleId = workbook.RegisterStyle(new CellStyle
        {
            Italic = true,
            WrapText = true,
            NumberFormat = "0.00%",
            HorizontalAlignment = HorizontalAlignment.Center,
            BorderBottom = new CellBorder(BorderStyle.Thin, CellColor.FromArgb(93, 93, 93))
        });

        for (var sheetIndex = 1; sheetIndex <= RepeatedStyleSheetCount; sheetIndex++)
        {
            var sheet = workbook.AddSheet($"Styled {sheetIndex}");
            for (uint row = 1; row <= RepeatedStyleRowsPerSheet; row++)
            {
                for (uint col = 1; col <= RepeatedStyleColumnsPerSheet; col++)
                {
                    var address = new CellAddress(sheet.Id, row, col);
                    sheet.SetCell(address, new NumberValue((row * col + sheetIndex) / 100.0));
                    sheet.GetCell(row, col)!.StyleId = (row + col + sheetIndex) % 2 == 0
                        ? boldCurrencyStyleId
                        : wrappedPercentStyleId;
                }

                sheet.SetStyleOnly(row, (uint)(RepeatedStyleColumnsPerSheet + 2), boldCurrencyStyleId);
            }
        }

        return workbook;
    }

    private static Workbook CreateWorkbookReferencesWorkbook()
    {
        var workbook = new Workbook("Native JSON Workbook References");
        var sheets = new List<Sheet>(ReferenceSheetCount);
        for (var sheetIndex = 1; sheetIndex <= ReferenceSheetCount; sheetIndex++)
            sheets.Add(workbook.AddSheet($"Reference {sheetIndex}"));

        foreach (var sheet in sheets)
        {
            for (uint row = 1; row <= ReferenceWatchesPerSheet; row++)
            {
                var address = new CellAddress(sheet.Id, row, 1);
                sheet.SetCell(address, new NumberValue(row));
                workbook.WatchedCells.Add(address);
            }
        }

        for (var scenarioIndex = 0; scenarioIndex < ReferenceScenarioCount; scenarioIndex++)
        {
            var changes = new List<ScenarioCellValue>(ReferenceScenarioChangesPerScenario);
            for (var changeIndex = 0; changeIndex < ReferenceScenarioChangesPerScenario; changeIndex++)
            {
                var sheet = sheets[(scenarioIndex + changeIndex) % sheets.Count];
                var row = (uint)(1 + (scenarioIndex + changeIndex) % ReferenceWatchesPerSheet);
                var address = new CellAddress(sheet.Id, row, (uint)(2 + changeIndex % 4));
                changes.Add(new ScenarioCellValue(address, new NumberValue(scenarioIndex * 100 + changeIndex)));
            }

            workbook.Scenarios.Add(new WorkbookScenario(
                $"Scenario {scenarioIndex + 1}",
                changes,
                Comment: "Reference-heavy save benchmark"));
        }

        return workbook;
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
