using System.Diagnostics;
using System.Xml.Linq;
using ClosedXML.Excel;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxFileAdapterPerformanceTests
{
    [Fact]
    [Trait("Category", "ExternalWorkbook")]
    public void Benchmark_LoadExternalWorkbook_ReportsTiming()
    {
        var paths = ResolveExternalWorkbookPaths();
        if (paths.Length == 0)
        {
            Console.WriteLine("PERF XLSX_LOAD_EXTERNAL skipped=true reason=FREEX_IO_BENCHMARK_PATHS_NOT_SET");
            return;
        }

        var adapter = new XlsxFileAdapter();
        var successfulLoads = 0;
        foreach (var path in paths)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var stopwatch = Stopwatch.StartNew();
            try
            {
                Workbook workbook;
                using (var stream = File.OpenRead(path))
                    workbook = adapter.Load(stream);
                stopwatch.Stop();
                var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

                workbook.SheetCount.Should().BeGreaterThan(0);
                successfulLoads++;
                Console.WriteLine(
                    "PERF XLSX_LOAD_EXTERNAL " +
                    $"file=\"{Path.GetFileName(path)}\" bytes={new FileInfo(path).Length:N0} " +
                    $"sheets={workbook.SheetCount} elapsed_ms={stopwatch.Elapsed.TotalMilliseconds:F2} " +
                    $"allocated_bytes={allocatedBytes:N0}");
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                var stackTop = ex.StackTrace?
                    .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault()?
                    .Trim()
                    .Replace("\"", "'", StringComparison.Ordinal) ?? "";
                Console.WriteLine(
                    "PERF XLSX_LOAD_EXTERNAL_FAILED " +
                    $"file=\"{Path.GetFileName(path)}\" bytes={new FileInfo(path).Length:N0} " +
                    $"elapsed_ms={stopwatch.Elapsed.TotalMilliseconds:F2} " +
                    $"error=\"{ex.GetType().Name}: {ex.Message}\" stack_top=\"{stackTop}\"");
            }
        }

        successfulLoads.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Benchmark_LoadDenseWorkbook_ReportsTiming()
    {
        const int iterations = 3;
        var package = CreateDenseXlsxPackage();
        var adapter = new XlsxFileAdapter();

        using (var warmup = new MemoryStream(package, writable: false))
            adapter.Load(warmup).SheetCount.Should().Be(DenseSheetCount);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var timings = new List<double>(iterations);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var total = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            using var stream = new MemoryStream(package, writable: false);
            var step = Stopwatch.StartNew();
            var workbook = adapter.Load(stream);
            step.Stop();
            workbook.SheetCount.Should().Be(DenseSheetCount);
            timings.Add(step.Elapsed.TotalMilliseconds);
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var ordered = timings.OrderBy(value => value).ToArray();
        var p95 = ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)];

        Console.WriteLine(
            "PERF XLSX_LOAD_DENSE " +
            $"sheets={DenseSheetCount} rows={DenseRowsPerSheet} cols={DenseColumnsPerSheet} " +
            $"steps={iterations} package_bytes={package.Length:N0} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={timings.Average():F2} " +
            $"p95_ms={p95:F2} max_ms={ordered[^1]:F2} allocated_bytes={allocatedBytes:N0}");

        timings.Average().Should().BeGreaterThan(0);
    }

    [Fact]
    public void Benchmark_SaveDenseWorkbook_ReportsTiming()
    {
        const int iterations = 3;
        var workbook = CreateDenseModelWorkbook();
        var adapter = new XlsxFileAdapter();

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
            "PERF XLSX_SAVE_DENSE " +
            $"sheets={DenseSheetCount} rows={DenseRowsPerSheet} cols={DenseColumnsPerSheet} " +
            $"steps={iterations} package_bytes={packageSizes.Max():N0} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={timings.Average():F2} " +
            $"p95_ms={p95:F2} max_ms={ordered[^1]:F2} allocated_bytes={allocatedBytes:N0}");

        timings.Average().Should().BeGreaterThan(0);
    }

    [Fact]
    public void Benchmark_SaveLoadedDenseWorkbook_ReportsTiming()
    {
        const int iterations = 3;
        var package = CreateDenseXlsxPackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var loadStream = new MemoryStream(package, writable: false))
            workbook = adapter.Load(loadStream);

        var sheet = workbook.Sheets[0];
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(42));

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
            "PERF XLSX_SAVE_LOADED_DENSE " +
            $"sheets={DenseSheetCount} rows={DenseRowsPerSheet} cols={DenseColumnsPerSheet} " +
            $"steps={iterations} package_bytes={packageSizes.Max():N0} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={timings.Average():F2} " +
            $"p95_ms={p95:F2} max_ms={ordered[^1]:F2} allocated_bytes={allocatedBytes:N0}");

        timings.Average().Should().BeGreaterThan(0);
    }

    [Fact]
    public void Benchmark_SaveWorksheetNativeMetadataWorkbook_ReportsTiming()
    {
        const int iterations = 3;
        var workbook = CreateWorksheetNativeMetadataWorkbook();
        var adapter = new XlsxFileAdapter();

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
            "PERF XLSX_SAVE_WORKSHEET_NATIVE_METADATA " +
            $"sheets={WorksheetNativeMetadataSheetCount} rows={WorksheetNativeMetadataRowsPerSheet} " +
            $"steps={iterations} package_bytes={packageSizes.Max():N0} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={timings.Average():F2} " +
            $"p95_ms={p95:F2} max_ms={ordered[^1]:F2} allocated_bytes={allocatedBytes:N0}");

        timings.Average().Should().BeGreaterThan(0);
    }

    [Fact]
    public void Benchmark_StructuredTableWriterTrailingNumber_AvoidsReverseIteratorAllocation()
    {
        var source = File.ReadAllText(FindRepoFile("src", "FreeX.Core.IO", "XlsxStructuredTableWriter.cs"));
        var methodStart = source.IndexOf("private static int ExtractTrailingNumber", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private static void TrySetNativeAttributeIfMissing", methodStart, StringComparison.Ordinal);
        var method = source[methodStart..methodEnd];

        method.Should().NotContain(
            ".Reverse()",
            "structured table id fallback parsing runs during XLSX save and should avoid LINQ iterator scaffolding");
        method.Should().NotContain(
            ".ToArray()",
            "trailing-number parsing should avoid a temporary char array allocation");
    }

    [Fact]
    public void SavePostProcessing_DetectsPivotCustomNumberFormatsWithoutNestedLinq()
    {
        var source = File.ReadAllText(FindRepoFile("src", "FreeX.Core.IO", "XlsxFileAdapter.SavePostProcessing.cs"));

        source.Should().Contain("HasPivotCustomNumberFormats(workbook)");
        source.Should().Contain("private static bool HasPivotCustomNumberFormats(Workbook workbook)");
        source.Should().NotContain(
            "workbook.Sheets.SelectMany(sheet => sheet.PivotTables)",
            "XLSX save post-processing should avoid nested LINQ iterator allocation while deciding whether pivot custom number formats need catalog output");
    }

    [Fact]
    public void NumberFormatCatalogWriter_BuildsPivotCustomFormatCatalogWithoutNestedLinq()
    {
        var source = File.ReadAllText(FindRepoFile("src", "FreeX.Core.IO", "XlsxNumberFormatCatalogWriter.cs"));

        source.Should().Contain("foreach (var sheet in workbook.Sheets)");
        source.Should().Contain("foreach (var pivot in sheet.PivotTables)");
        source.Should().Contain("foreach (var field in pivot.DataFields)");
        source.Should().NotContain(
            ".SelectMany(",
            "pivot custom number-format catalog building should walk sheets/pivots/data fields directly");
        source.Should().NotContain(
            ".Where(pair => pair.Key >= 164",
            "catalog seeding should avoid a temporary LINQ filtered dictionary projection");
    }

    [Fact]
    public void LoadCore_ReadsWorkbookMetadataInSinglePackagePass()
    {
        var adapterSource = File.ReadAllText(FindRepoFile("src", "FreeX.Core.IO", "XlsxFileAdapter.cs"));
        var metadataSource = File.ReadAllText(FindRepoFile("src", "FreeX.Core.IO", "XlsxWorkbookMetadataReader.cs"));

        adapterSource.Should().Contain("var workbookMetadata = XlsxWorkbookMetadataReader.LoadWorkbookMetadata(packageStream);");
        foreach (var legacyCall in new[]
        {
            "LoadUses1904DateSystem(packageStream)",
            "LoadWorkbookProperties(packageStream)",
            "LoadWorkbookViewProperties(packageStream)",
            "LoadFileSharing(packageStream)",
            "LoadFileRecoveryProperties(packageStream)",
            "LoadFileVersion(packageStream)",
            "LoadFunctionGroups(packageStream)",
            "LoadSmartTags(packageStream)",
            "LoadProtection(packageStream)",
            "LoadProtectionMetadata(packageStream)",
            "LoadCalculationProperties(packageStream)",
            "LoadCustomViews(packageStream)"
        })
        {
            adapterSource.Should().NotContain(legacyCall);
        }

        metadataSource.Should().Contain("public static XlsxWorkbookMetadataSnapshot LoadWorkbookMetadata(Stream xlsxStream)");
        metadataSource.Should().Contain("var workbookEntry = archive.GetEntry(\"xl/workbook.xml\");");
        metadataSource.Should().Contain("return LoadWorkbookMetadata(workbookXml);");
    }

    [Fact]
    public void LoadCore_ReusesSingleStylesheetParseForLoadMetadata()
    {
        var adapterSource = File.ReadAllText(FindRepoFile("src", "FreeX.Core.IO", "XlsxFileAdapter.cs"));

        adapterSource.Should().Contain("var stylesXml = XlsxStylesheetReader.Load(packageStream);");
        adapterSource.Should().Contain("XlsxWorkbookMetadataReader.LoadNumberFormatCatalog(stylesXml)");
        adapterSource.Should().Contain("XlsxIndexedColorPaletteMapper.Load(stylesXml)");
        adapterSource.Should().Contain("XlsxPivotTableStyleMetadataReader.Load(stylesXml)");
        adapterSource.Should().Contain("LoadSheetXmlLayout(packageStream, stylesXml)");
        adapterSource.Should().NotContain("LoadNumberFormatCatalog(packageStream)");
        adapterSource.Should().NotContain("XlsxIndexedColorPaletteMapper.Load(packageStream)");
        adapterSource.Should().NotContain("XlsxPivotTableStyleMetadataReader.Load(packageStream)");
        adapterSource.Should().NotContain("LoadSheetXmlLayout(packageStream);");
    }

    [Fact]
    public void SavePostProcessing_BatchesWorkbookMetadataXmlWrites()
    {
        var adapterSource = File.ReadAllText(FindRepoFile("src", "FreeX.Core.IO", "XlsxFileAdapter.SavePostProcessing.cs"));
        var writerSource = File.ReadAllText(FindRepoFile("src", "FreeX.Core.IO", "XlsxWorkbookMetadataWriter.cs"));

        adapterSource.Should().Contain("XlsxWorkbookMetadataWriter.SavePostProcessingMetadata(packageStream, workbook);");
        adapterSource.Should().Contain("XlsxWorkbookMetadataWriter.SaveSourcePackageReplayMetadata(packageStream, workbook);");
        adapterSource.Should().NotContain("XlsxWorkbookMetadataWriter.SaveWorkbookProperties(packageStream, workbook);");
        adapterSource.Should().NotContain("XlsxWorkbookMetadataWriter.SaveCalculationProperties(packageStream, workbook);");
        adapterSource.Should().NotContain("XlsxWorkbookAdditionalViewMapper.Save(packageStream, workbook);");

        writerSource.Should().Contain("public static void SavePostProcessingMetadata(Stream xlsxStream, Workbook workbook)");
        writerSource.Should().Contain("public static void SaveSourcePackageReplayMetadata(Stream xlsxStream, Workbook workbook)");
        writerSource.Should().Contain("private static void SaveWorkbookXml(Stream xlsxStream, Workbook workbook");
    }

    [Fact]
    public void SavePostProcessing_BatchesWorksheetNativeMetadataXmlWrites()
    {
        var adapterSource = File.ReadAllText(FindRepoFile("src", "FreeX.Core.IO", "XlsxFileAdapter.SavePostProcessing.cs"));
        var batchSource = File.ReadAllText(FindRepoFile("src", "FreeX.Core.IO", "XlsxWorksheetNativeMetadataBatchWriter.cs"));
        var sessionSource = File.ReadAllText(FindRepoFile("src", "FreeX.Core.IO", "XlsxWorksheetXmlEditSession.cs"));

        adapterSource.Should().Contain("XlsxWorksheetNativeMetadataBatchWriter.Save(packageStream, workbook, GetWorksheetPathMap());");
        adapterSource.Should().Contain("HasSourcePackageIndependentWorksheetNativeMetadata");
        foreach (var legacyCall in new[]
        {
            "XlsxWorksheetProtectionMetadataWriter.Save(packageStream, workbook, GetWorksheetPathMap());",
            "XlsxWorksheetPrintOptionsMetadataWriter.Save(packageStream, workbook, GetWorksheetPathMap());",
            "XlsxWorksheetDimensionMetadataWriter.Save(packageStream, workbook, GetWorksheetPathMap());",
            "XlsxWorksheetSheetPropertiesMetadataWriter.Save(packageStream, workbook, GetWorksheetPathMap());",
            "XlsxWorksheetPrimaryViewMetadataWriter.Save(packageStream, workbook, GetWorksheetPathMap());",
            "XlsxWorksheetPageMarginsMetadataWriter.Save(packageStream, workbook, GetWorksheetPathMap());",
            "XlsxWorksheetPageBreaksMetadataWriter.Save(packageStream, workbook, GetWorksheetPathMap());",
            "XlsxWorksheetHeaderFooterMetadataWriter.Save(packageStream, workbook, GetWorksheetPathMap());"
        })
        {
            adapterSource.Should().NotContain(legacyCall);
        }

        batchSource.Should().Contain("using var session = new XlsxWorksheetXmlEditSession(xlsxStream, worksheetPathMap);");
        batchSource.Should().Contain("XlsxWorksheetProtectionMetadataWriter.Save(session, workbook);");
        batchSource.Should().Contain("XlsxWorksheetHeaderFooterMetadataWriter.Save(session, workbook);");
        sessionSource.Should().Contain("private readonly Dictionary<string, XDocument> _documents");
        sessionSource.Should().Contain("XlsxPackageXmlEditor.ReplaceXml(_archive, path, _documents[path]);");
    }

    private const int DenseSheetCount = 8;
    private const int DenseRowsPerSheet = 80;
    private const int DenseColumnsPerSheet = 24;
    private const int WorksheetNativeMetadataSheetCount = 8;
    private const int WorksheetNativeMetadataRowsPerSheet = 40;

    private static byte[] CreateDenseXlsxPackage()
    {
        using var workbook = new XLWorkbook();
        for (var sheetIndex = 1; sheetIndex <= DenseSheetCount; sheetIndex++)
        {
            var sheet = workbook.Worksheets.Add($"Sheet {sheetIndex}");
            for (var row = 1; row <= DenseRowsPerSheet; row++)
            {
                for (var col = 1; col <= DenseColumnsPerSheet; col++)
                {
                    var cell = sheet.Cell(row, col);
                    cell.Value = row * col + sheetIndex;
                    if ((row + col) % 17 == 0)
                    {
                        cell.Style.Font.Bold = true;
                        cell.Style.Fill.BackgroundColor = XLColor.FromArgb(255, 242, 204);
                    }
                }
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static Workbook CreateDenseModelWorkbook()
    {
        var workbook = new Workbook("Dense IO");
        for (var sheetIndex = 1; sheetIndex <= DenseSheetCount; sheetIndex++)
        {
            var sheet = workbook.AddSheet($"Sheet {sheetIndex}");
            for (uint row = 1; row <= DenseRowsPerSheet; row++)
            {
                for (uint col = 1; col <= DenseColumnsPerSheet; col++)
                {
                    sheet.SetCell(
                        new CellAddress(sheet.Id, row, col),
                        new NumberValue(row * col + sheetIndex));
                }
            }
        }

        return workbook;
    }

    private static Workbook CreateWorksheetNativeMetadataWorkbook()
    {
        var workbook = new Workbook("Worksheet native metadata IO");
        for (var sheetIndex = 1; sheetIndex <= WorksheetNativeMetadataSheetCount; sheetIndex++)
        {
            var sheet = workbook.AddSheet($"Metadata {sheetIndex}");
            for (uint row = 1; row <= WorksheetNativeMetadataRowsPerSheet; row++)
            {
                sheet.SetCell(
                    new CellAddress(sheet.Id, row, 1),
                    new TextValue($"R{row}"));
            }

            sheet.IsProtected = true;
            sheet.ProtectionMetadata = MakeBag(
                "sheetProtection",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["algorithmName"] = "SHA-512",
                    ["hashValue"] = $"hash{sheetIndex}",
                    ["saltValue"] = $"salt{sheetIndex}",
                    ["spinCount"] = "100000",
                    ["objects"] = "1",
                    ["scenarios"] = "1"
                },
                [$"<fx:sheetProtectionNative xmlns:fx=\"urn:freex:test\" id=\"{sheetIndex}\" />"]);
            sheet.PrintOptionsMetadata = MakeBag(
                "printOptions",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["gridLinesSet"] = "1",
                    ["customAttr"] = $"print-{sheetIndex}"
                },
                [$"<fx:printOptionsNative xmlns:fx=\"urn:freex:test\" id=\"{sheetIndex}\" />"]);
            sheet.DimensionMetadata = MakeBag(
                "dimension",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["nativeDimensionAttr"] = $"dimension-{sheetIndex}"
                });
            sheet.SheetPropertiesMetadata = MakeBag(
                "sheetPr",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["filterMode"] = "1",
                    ["customSheetPrAttr"] = $"sheetPr-{sheetIndex}"
                },
                [$"<fx:sheetPrNative xmlns:fx=\"urn:freex:test\" id=\"{sheetIndex}\" />"]);
            sheet.PrimaryViewMetadata = MakeBag(
                "sheetView",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["showZeros"] = "0",
                    ["rightToLeft"] = "1",
                    ["customViewAttr"] = $"view-{sheetIndex}"
                },
                [$"<pivotSelection xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" pane=\"topLeft\" />"]);
            sheet.PageMargins = new WorksheetPageMargins(0.7, 0.75, 0.8, 0.85);
            sheet.PageMarginsMetadata = MakeBag(
                "pageMargins",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["customAttr"] = $"margins-{sheetIndex}"
                },
                [$"<fx:pageMarginsNative xmlns:fx=\"urn:freex:test\" id=\"{sheetIndex}\" />"]);
            sheet.RowPageBreaks.Add(20);
            sheet.RowPageBreaksMetadata = new WorksheetPageBreaksMetadataModel
            {
                NativeAttributes = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["manualBreakCount"] = "1"
                },
                BreakNativeAttributes = new Dictionary<uint, Dictionary<string, string>>
                {
                    [20] = new(StringComparer.Ordinal)
                    {
                        ["pt"] = "1",
                        ["customAttr"] = $"row-break-{sheetIndex}"
                    }
                }
            };
            sheet.ColumnPageBreaks.Add(5);
            sheet.ColumnPageBreaksMetadata = new WorksheetPageBreaksMetadataModel
            {
                NativeAttributes = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["manualBreakCount"] = "1"
                },
                BreakNativeAttributes = new Dictionary<uint, Dictionary<string, string>>
                {
                    [5] = new(StringComparer.Ordinal)
                    {
                        ["pt"] = "1",
                        ["customAttr"] = $"column-break-{sheetIndex}"
                    }
                }
            };
            sheet.PageHeader = new WorksheetHeaderFooter("L", "C", "R");
            sheet.PageFooter = new WorksheetHeaderFooter("FL", "FC", "FR");
            sheet.HeaderFooterMetadata = MakeBag(
                "headerFooter",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["nativeHeaderFooterAttr"] = $"header-footer-{sheetIndex}"
                },
                [$"<fx:headerFooterNative xmlns:fx=\"urn:freex:test\" id=\"{sheetIndex}\" />"]);
        }

        return workbook;
    }

    private static NativeXmlPreserveBag MakeBag(
        string key,
        Dictionary<string, string>? attrs = null,
        IReadOnlyList<string>? children = null)
    {
        var wrapper = new XElement("e");
        foreach (var (name, value) in attrs ?? [])
            wrapper.SetAttributeValue(XName.Get(name), value);
        foreach (var childXml in children ?? [])
            wrapper.Add(XElement.Parse(childXml, System.Xml.Linq.LoadOptions.PreserveWhitespace));

        var bag = new NativeXmlPreserveBag();
        bag.Set(key, wrapper.ToString(System.Xml.Linq.SaveOptions.DisableFormatting));
        return bag;
    }

    private static string[] ResolveExternalWorkbookPaths()
    {
        var configured = Environment.GetEnvironmentVariable("FREEX_IO_BENCHMARK_PATHS");
        if (string.IsNullOrWhiteSpace(configured))
            return [];

        var limit = 3;
        if (int.TryParse(Environment.GetEnvironmentVariable("FREEX_IO_BENCHMARK_LIMIT"), out var configuredLimit))
            limit = Math.Clamp(configuredLimit, 1, 20);

        return configured
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .SelectMany(EnumerateWorkbookPaths)
            .Where(path => !Path.GetFileName(path).StartsWith("~$", StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => new FileInfo(path))
            .Where(file => file.Exists)
            .OrderByDescending(file => file.Length)
            .Take(limit)
            .Select(file => file.FullName)
            .ToArray();
    }

    private static IEnumerable<string> EnumerateWorkbookPaths(string path)
    {
        if (Directory.Exists(path))
        {
            return Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories)
                .Where(IsSupportedBenchmarkWorkbook);
        }

        return File.Exists(path) && IsSupportedBenchmarkWorkbook(path)
            ? [path]
            : [];
    }

    private static bool IsSupportedBenchmarkWorkbook(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".xlsm", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".xltx", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".xltm", StringComparison.OrdinalIgnoreCase);
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
