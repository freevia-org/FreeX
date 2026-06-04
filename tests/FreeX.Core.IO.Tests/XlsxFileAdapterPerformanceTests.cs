using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Xml.Linq;
using ClosedXML.Excel;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxFileAdapterPerformanceTests
{
    [BenchmarkFact]
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

    [BenchmarkFact]
    [Trait("Category", "ExternalWorkbook")]
    public void Benchmark_LoadExternalWorkbookStages_ReportsTiming()
    {
        var paths = ResolveExternalWorkbookPaths();
        if (paths.Length == 0)
        {
            Console.WriteLine("PERF XLSX_LOAD_EXTERNAL_STAGES skipped=true reason=FREEX_IO_BENCHMARK_PATHS_NOT_SET");
            return;
        }

        var adapter = new XlsxFileAdapter();
        foreach (var path in paths)
        {
            byte[] package = [];
            MeasureExternalStage(path, "READ_BYTES", () =>
            {
                package = File.ReadAllBytes(path);
                package.Length.Should().BeGreaterThan(0);
            });

            MeasureExternalStage(path, "RAW_CLOSEDXML_LOAD", () =>
            {
                using var packageStream = new MemoryStream(package, writable: false);
                using var workbook = new XLWorkbook(packageStream);
                workbook.Worksheets.Count.Should().BeGreaterThan(0);
            });

            MeasureExternalStage(path, "SANITIZED_CLOSEDXML_LOAD", () =>
            {
                using var sourcePackage = new MemoryStream(package, writable: false);
                var sanitizedPackage = XlsxClosedXmlLoadPackageSanitizer.Create(sourcePackage);
                try
                {
                    using var workbook = new XLWorkbook(sanitizedPackage);
                    workbook.Worksheets.Count.Should().BeGreaterThan(0);
                }
                finally
                {
                    if (!ReferenceEquals(sanitizedPackage, sourcePackage))
                        sanitizedPackage.Dispose();
                }
            });

            MeasureExternalStage(path, "STYLE_STRIPPED_SANITIZED_CLOSEDXML_LOAD", () =>
            {
                using var sourcePackage = new MemoryStream(package, writable: false);
                var strippedPackage = XlsxClosedXmlStyleOnlyCellStripper.Create(sourcePackage);
                MemoryStream? sanitizedPackage = null;
                try
                {
                    sanitizedPackage = XlsxClosedXmlLoadPackageSanitizer.Create(strippedPackage);
                    using var workbook = new XLWorkbook(sanitizedPackage);
                    workbook.Worksheets.Count.Should().BeGreaterThan(0);
                }
                finally
                {
                    if (sanitizedPackage is not null &&
                        !ReferenceEquals(sanitizedPackage, strippedPackage) &&
                        !ReferenceEquals(sanitizedPackage, sourcePackage))
                    {
                        sanitizedPackage.Dispose();
                    }

                    if (!ReferenceEquals(strippedPackage, sourcePackage))
                        strippedPackage.Dispose();
                }
            });

            MeasureExternalStage(path, "FREEX_FULL_LOAD", () =>
            {
                using var packageStream = new MemoryStream(package, writable: false);
                var workbook = adapter.Load(packageStream);
                workbook.SheetCount.Should().BeGreaterThan(0);
                workbook.Sheets.Sum(sheet => sheet.CellCount).Should().BeGreaterThanOrEqualTo(0);
            });
        }
    }

    [BenchmarkFact]
    [Trait("Category", "ExternalWorkbook")]
    public void Benchmark_SaveExternalLoadedWorkbook_ReportsTiming()
    {
        var paths = ResolveExternalWorkbookPaths();
        if (paths.Length == 0)
        {
            Console.WriteLine("PERF XLSX_SAVE_EXTERNAL_LOADED skipped=true reason=FREEX_IO_BENCHMARK_PATHS_NOT_SET");
            return;
        }

        var adapter = new XlsxFileAdapter();
        foreach (var path in paths)
        {
            Workbook workbook;
            using (var stream = File.OpenRead(path))
                workbook = adapter.Load(stream);

            workbook.SheetCount.Should().BeGreaterThan(0);
            var firstSheet = workbook.GetSheetAt(0);
            firstSheet.SetCell(new CellAddress(firstSheet.Id, 1, 1), new TextValue("freex-io-benchmark"));

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            using var output = new MemoryStream();
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var stopwatch = Stopwatch.StartNew();
            adapter.Save(workbook, output);
            stopwatch.Stop();
            var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

            output.Length.Should().BeGreaterThan(0);
            Console.WriteLine(
                "PERF XLSX_SAVE_EXTERNAL_LOADED " +
                $"file=\"{Path.GetFileName(path)}\" source_bytes={new FileInfo(path).Length:N0} " +
                $"package_bytes={output.Length:N0} sheets={workbook.SheetCount} " +
                $"cells={workbook.Sheets.Sum(sheet => sheet.CellCount):N0} " +
                $"style_only_cells={workbook.Sheets.Sum(sheet => sheet.GetStyleOnlyEntries().Count()):N0} " +
                $"elapsed_ms={stopwatch.Elapsed.TotalMilliseconds:F2} allocated_bytes={allocatedBytes:N0}");
        }
    }

    [BenchmarkFact]
    public void Benchmark_LoadGeneratedStyleHeavyWorkbook_ReportsTiming()
    {
        const int iterations = 3;
        var package = CreateGeneratedStyleHeavyXlsxPackage();
        var adapter = new XlsxFileAdapter();

        using (var warmup = new MemoryStream(package, writable: false))
            AssertGeneratedStyleHeavyWorkbook(adapter.Load(warmup));

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

            AssertGeneratedStyleHeavyWorkbook(workbook);
            timings.Add(step.Elapsed.TotalMilliseconds);
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var ordered = timings.OrderBy(value => value).ToArray();
        var p95 = ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)];

        Console.WriteLine(
            "PERF XLSX_LOAD_GENERATED_STYLE_HEAVY " +
            $"sheets={GeneratedStyleHeavySheetCount} rows={GeneratedStyleHeavyRowsPerSheet} " +
            $"value_cols={GeneratedStyleHeavyValueColumnsPerSheet} style_only_cols={GeneratedStyleHeavyStyleOnlyColumnsPerSheet} " +
            $"style_only_cells={GeneratedStyleHeavySheetCount * GeneratedStyleHeavyRowsPerSheet * GeneratedStyleHeavyStyleOnlyColumnsPerSheet:N0} " +
            $"steps={iterations} package_bytes={package.Length:N0} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={timings.Average():F2} " +
            $"p95_ms={p95:F2} max_ms={ordered[^1]:F2} allocated_bytes={allocatedBytes:N0}");

        timings.Average().Should().BeGreaterThan(0);
    }

    [BenchmarkFact]
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
    public void SparklineLoad_UsesExtensionListFastPath()
    {
        var source = File.ReadAllText(FindRepoFile("src", "FreeX.Core.IO", "XlsxSparklineMapper.cs"));
        var readMethod = source[
            source.IndexOf("public static IReadOnlyList<SparklineModel> Read", StringComparison.Ordinal)..
            source.IndexOf("public static void Save", StringComparison.Ordinal)];

        readMethod.Should().Contain("worksheetXml.Root?.Elements()");
        readMethod.Should().Contain("return [];");
        readMethod.Should().Contain("extensionList.Descendants()");
        readMethod.Should().NotContain("worksheetXml.Descendants()");
    }

    [Fact]
    public void LoadSourcePackageCapture_ReusesOnlyOwnedPackageBuffers()
    {
        var adapterSource = File.ReadAllText(FindRepoFile("src", "FreeX.Core.IO", "XlsxFileAdapter.cs"));
        var snapshotSource = File.ReadAllText(FindRepoFile("src", "FreeX.Core.IO", "XlsxFileAdapter.SourcePackageSnapshot.cs"));

        adapterSource.Should().Contain("loadPackage.CanReuseBufferForSnapshot");
        adapterSource.Should().Contain("CanReuseBufferForSnapshot: false");
        adapterSource.Should().Contain("CanReuseBufferForSnapshot: true");
        snapshotSource.Should().Contain("allowBufferReuse &&");
        snapshotSource.Should().Contain("buffer.Array,");
        snapshotSource.Should().Contain("worksheetsWithPreservableSourceMetadata");
    }

    [Fact]
    public void LoadSourcePackageCapture_FingerprintsDenseSaveBenchmarkForCopyFastPath()
    {
        var snapshotSource = File.ReadAllText(FindRepoFile("src", "FreeX.Core.IO", "XlsxFileAdapter.SourcePackageSnapshot.cs"));

        snapshotSource.Should().Contain("private const int FingerprintCellLimit = 25_000;");
        (DenseSheetCount * DenseRowsPerSheet * DenseColumnsPerSheet).Should().BeLessThan(25_000);
    }

    [Fact]
    public void LoadSourcePackageCapture_CountsStyleOnlyEntriesTowardFingerprintLimit()
    {
        var snapshotSource = File.ReadAllText(FindRepoFile("src", "FreeX.Core.IO", "XlsxFileAdapter.SourcePackageSnapshot.cs"));

        snapshotSource.Should().Contain("sheet.HasStyleOnlyCells");
        snapshotSource.Should().Contain("sheet.GetStyleOnlyEntries()");
    }

    [Fact]
    public void LoadPath_BoundsStyleOnlyStripperToLargeStyleOnlyLayouts()
    {
        var adapterSource = File.ReadAllText(FindRepoFile("src", "FreeX.Core.IO", "XlsxFileAdapter.cs"));

        adapterSource.Should().Contain("private const int ClosedXmlStyleOnlyStripCellThreshold = 16_384;");
        adapterSource.Should().Contain("if (sheetXmlLayoutHadWarnings || sheetXmlLayout.Count == 0)");
        adapterSource.Should().Contain("explicitStyleOnlyCellCount += layout.ExplicitStyleOnlyCells.Count;");
        adapterSource.Should().Contain("explicitStyleOnlyCellCount <= ClosedXmlStyleOnlyStripCellThreshold");
        adapterSource.Should().Contain("layout.HasDuplicateStyleOnlyCellStyleIndexes");
    }

    [Fact]
    public void LoadPath_PreSizesSheetCellStorageFromSheetDataLayout()
    {
        var adapterSource = File.ReadAllText(FindRepoFile("src", "FreeX.Core.IO", "XlsxFileAdapter.cs"));
        var cellLayoutSource = File.ReadAllText(FindRepoFile("src", "FreeX.Core.IO", "XlsxWorksheetCellLayoutReader.cs"));
        var sheetXmlLayoutSource = File.ReadAllText(FindRepoFile("src", "FreeX.Core.IO", "XlsxFileAdapter.SheetXmlLayout.cs"));

        cellLayoutSource.Should().Contain("int PopulatedCellCount");
        sheetXmlLayoutSource.Should().Contain("cellLayout.PopulatedCellCount");
        adapterSource.Should().Contain("sheet.EnsureCellCapacity(layoutWithCells.PopulatedCellCount);");
    }

    [Fact]
    public void SaveSourcePackageCapture_ReusesSaveFingerprintForSnapshot()
    {
        var saveSource = File.ReadAllText(FindRepoFile("src", "FreeX.Core.IO", "XlsxFileAdapter.Save.cs"));
        var postProcessingSource = File.ReadAllText(FindRepoFile("src", "FreeX.Core.IO", "XlsxFileAdapter.SavePostProcessing.cs"));
        var snapshotSource = File.ReadAllText(FindRepoFile("src", "FreeX.Core.IO", "XlsxFileAdapter.SourcePackageSnapshot.cs"));

        saveSource.Should().Contain("string? currentModelFingerprint = null;");
        saveSource.Should().Contain("sourcePackage.Matches(workbook, out currentModelFingerprint)");
        saveSource.Should().Contain("sourcePackage.TrySavePatchedCellValues(workbook, stream, ref currentModelFingerprint)");
        postProcessingSource.Should().Contain("currentModelFingerprint,");
        postProcessingSource.Should().Contain("sourcePackage?.WorksheetsWithPreservableSourceMetadata");
        snapshotSource.Should().Contain("public bool Matches(Workbook workbook, out string? currentModelFingerprint)");
        snapshotSource.Should().Contain("ref string? currentModelFingerprint");
        snapshotSource.Should().Contain("currentModelFingerprint,");
        snapshotSource.Should().Contain("GetModelFingerprint(workbook, currentModelFingerprint)");
        snapshotSource.Should().Contain("patchedPackage.TryGetBuffer");
        snapshotSource.Should().Contain("WithAppliedChanges(changes, dimensionChanges, patchedModelFingerprint)");
    }

    [Fact]
    public void SaveSourcePackagePreservation_ReusesLoadTimePlainWorksheetPreflight()
    {
        var adapterSource = File.ReadAllText(FindRepoFile("src", "FreeX.Core.IO", "XlsxFileAdapter.cs"));
        var layoutSource = File.ReadAllText(FindRepoFile("src", "FreeX.Core.IO", "XlsxFileAdapter.SheetXmlLayout.cs"));
        var sourcePackageSource = File.ReadAllText(FindRepoFile("src", "FreeX.Core.IO", "XlsxFileAdapter.SourcePackage.cs"));
        var preserverSource = File.ReadAllText(FindRepoFile("src", "FreeX.Core.IO", "XlsxWorksheetMetadataPreserver.cs"));

        adapterSource.Should().Contain("GetWorksheetsWithPreservableSourceMetadata(");
        layoutSource.Should().Contain("HasPreservableSourceWorksheetMetadata(worksheetXml, worksheetNs)");
        sourcePackageSource.Should().Contain("sourcePackage.WorksheetsWithPreservableSourceMetadata");
        preserverSource.Should().Contain("worksheetsWithPreservableSourceMetadata is not null");
        preserverSource.Should().Contain("!worksheetsWithPreservableSourceMetadata.Contains(sheetName)");
    }

    [Fact]
    public void SaveSourcePackageCompatibilityNormalization_SkipsUnneededWorksheetScans()
    {
        var postProcessingSource = File.ReadAllText(FindRepoFile("src", "FreeX.Core.IO", "XlsxFileAdapter.SavePostProcessing.cs"));
        var normalizerSource = File.ReadAllText(FindRepoFile("src", "FreeX.Core.IO", "XlsxExcelCompatibilityNormalizer.cs"));

        postProcessingSource.Should().Contain("CreateExcelCompatibilityNormalizationPlan(sourcePackage, sourceParts, featurePlan)");
        postProcessingSource.Should().Contain("sourcePackage?.WorksheetsWithPreservableSourceMetadata is null");
        postProcessingSource.Should().Contain("sourceParts.HasDrawings");
        postProcessingSource.Should().Contain("featurePlan.HasCellFormulas");
        normalizerSource.Should().Contain("plan.RequiresWorksheetScan");
        normalizerSource.Should().Contain("plan.ScanWorksheetFormulaText");
        normalizerSource.Should().Contain("plan.ScanWorksheetDrawingTargets");
    }

    [Fact]
    public void WorkbookSchemaNormalizer_PreflightsWorksheetOrderBeforeLoadingXml()
    {
        var source = File.ReadAllText(FindRepoFile("src", "FreeX.Core.IO", "XlsxWorkbookSchemaNormalizer.cs"));

        source.Should().Contain("InspectWorksheetNormalization(worksheetEntry, workbookNs, relNs)");
        source.Should().Contain("!preflight.NeedsChildOrderNormalization && !preflight.HasLegacyDrawingHeaderFooter");
        source.Should().Contain("XmlReader.Create(stream");
        source.Should().Contain("reader.Depth != worksheetDepth + 1");
        source.Should().Contain("preflight.NeedsChildOrderNormalization &&");
        source.Should().Contain("NormalizeWorksheet(worksheetXml, workbookNs)");
    }

    [Fact]
    public void Load_FromCallerOwnedMemoryStream_KeepsSourceSnapshotIndependent()
    {
        var package = CreateDenseXlsxPackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var source = new MemoryStream(package, writable: true))
            workbook = adapter.Load(source);

        package[0] = 0;
        package[1] = 0;
        package[2] = 0;
        package[3] = 0;
        workbook.Sheets[0].SetCell(new CellAddress(workbook.Sheets[0].Id, 1, 1), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        adapter.Load(saved).SheetCount.Should().Be(DenseSheetCount);
    }

    [BenchmarkFact]
    public void Benchmark_LoadIgnoredErrorAndStyleOnlyMetadataWorkbook_ReportsTiming()
    {
        const int iterations = 3;
        var package = CreateIgnoredErrorAndStyleOnlyMetadataPackage();
        var adapter = new XlsxFileAdapter();

        using (var warmup = new MemoryStream(package, writable: false))
            AssertIgnoredErrorAndStyleOnlyMetadata(adapter.Load(warmup));

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
            AssertIgnoredErrorAndStyleOnlyMetadata(workbook);
            timings.Add(step.Elapsed.TotalMilliseconds);
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var ordered = timings.OrderBy(value => value).ToArray();
        var p95 = ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)];

        Console.WriteLine(
            "PERF XLSX_LOAD_IGNORED_ERROR_STYLE_ONLY_METADATA " +
            $"rows={IgnoredErrorStyleOnlyRows} value_cols={IgnoredErrorStyleOnlyValueColumns} " +
            $"style_only_cols={IgnoredErrorStyleOnlyStyleColumns} ignored_ranges={IgnoredErrorStyleOnlyIgnoredRanges} " +
            $"steps={iterations} package_bytes={package.Length:N0} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={timings.Average():F2} " +
            $"p95_ms={p95:F2} max_ms={ordered[^1]:F2} allocated_bytes={allocatedBytes:N0}");

        timings.Average().Should().BeGreaterThan(0);
    }
}
