using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
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
        adapterSource.Should().Contain("explicitStyleOnlyCellCount > ClosedXmlStyleOnlyStripCellThreshold");
    }

    [Fact]
    public void SaveSourcePackageCapture_ReusesSaveFingerprintForSnapshot()
    {
        var saveSource = File.ReadAllText(FindRepoFile("src", "FreeX.Core.IO", "XlsxFileAdapter.Save.cs"));
        var postProcessingSource = File.ReadAllText(FindRepoFile("src", "FreeX.Core.IO", "XlsxFileAdapter.SavePostProcessing.cs"));
        var snapshotSource = File.ReadAllText(FindRepoFile("src", "FreeX.Core.IO", "XlsxFileAdapter.SourcePackageSnapshot.cs"));

        saveSource.Should().Contain("string? currentModelFingerprint = null;");
        saveSource.Should().Contain("sourcePackage.Matches(workbook, out currentModelFingerprint)");
        postProcessingSource.Should().Contain("currentModelFingerprint,");
        postProcessingSource.Should().Contain("sourcePackage?.WorksheetsWithPreservableSourceMetadata");
        snapshotSource.Should().Contain("public bool Matches(Workbook workbook, out string? currentModelFingerprint)");
        snapshotSource.Should().Contain("GetModelFingerprint(workbook, currentModelFingerprint)");
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

    [Fact]
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
    public void Benchmark_LoadDrawingPicturesWorkbook_ReportsTiming()
    {
        const int iterations = 3;
        const int pictureCount = 180;
        var workbook = CreateDrawingPicturesWorkbook(pictureCount);
        var adapter = new XlsxFileAdapter();
        byte[] package;
        using (var source = new MemoryStream())
        {
            adapter.Save(workbook, source);
            package = source.ToArray();
        }

        using (var warmup = new MemoryStream(package))
        {
            var loaded = adapter.Load(warmup);
            loaded.GetSheetAt(0).Pictures.Should().HaveCount(pictureCount);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var timings = new List<double>(iterations);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var total = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            using var stream = new MemoryStream(package);
            var step = Stopwatch.StartNew();
            var loaded = adapter.Load(stream);
            step.Stop();
            loaded.GetSheetAt(0).Pictures.Should().HaveCount(pictureCount);
            timings.Add(step.Elapsed.TotalMilliseconds);
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var ordered = timings.OrderBy(value => value).ToArray();
        var p95 = ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)];

        Console.WriteLine(
            "PERF XLSX_LOAD_DRAWING_PICTURES " +
            $"pictures={pictureCount} package_bytes={package.Length:N0} steps={iterations} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={timings.Average():F2} " +
            $"p95_ms={p95:F2} max_ms={ordered[^1]:F2} allocated_bytes={allocatedBytes:N0}");

        timings.Average().Should().BeGreaterThan(0);
    }

    [Fact]
    public void Benchmark_SaveIgnoredErrorsWorkbook_ReportsTiming()
    {
        const int iterations = 3;
        var workbook = CreateIgnoredErrorsSaveWorkbook();
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
            "PERF XLSX_SAVE_IGNORED_ERRORS " +
            $"rows={IgnoredErrorSaveRows} cols={IgnoredErrorSaveColumns} " +
            $"ignored_cells={IgnoredErrorSaveRows * IgnoredErrorSaveColumns} " +
            $"steps={iterations} package_bytes={packageSizes.Max():N0} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={timings.Average():F2} " +
            $"p95_ms={p95:F2} max_ms={ordered[^1]:F2} allocated_bytes={allocatedBytes:N0}");

        timings.Average().Should().BeGreaterThan(0);
        packageSizes.Should().OnlyContain(size => size > 0);
    }

    [Fact]
    public void Benchmark_SaveStyleOnlyWorkbook_ReportsTiming()
    {
        const int iterations = 3;
        var workbook = CreateStyleOnlyModelWorkbook();
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
            "PERF XLSX_SAVE_STYLE_ONLY " +
            $"sheets={StyleOnlySaveSheetCount} rows={StyleOnlySaveRowsPerSheet} " +
            $"style_only_cols={StyleOnlySaveColumnsPerSheet} run_width={StyleOnlySaveRunWidth} " +
            $"style_only_cells={StyleOnlySaveSheetCount * StyleOnlySaveRowsPerSheet * StyleOnlySaveColumnsPerSheet} " +
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
    public void Benchmark_SaveLoadedDenseMutatedWorkbook_ReportsTiming()
    {
        const int iterations = 5;
        var package = CreateDenseXlsxPackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var loadStream = new MemoryStream(package, writable: false))
            workbook = adapter.Load(loadStream);

        var sheet = workbook.Sheets[0];
        var markerAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(markerAddress, new NumberValue(42));
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
            sheet.SetCell(markerAddress, new NumberValue(100 + i));
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
            "PERF XLSX_SAVE_LOADED_DENSE_MUTATED " +
            $"sheets={DenseSheetCount} rows={DenseRowsPerSheet} cols={DenseColumnsPerSheet} " +
            $"steps={iterations} package_bytes={packageSizes.Max():N0} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={timings.Average():F2} " +
            $"p95_ms={p95:F2} max_ms={ordered[^1]:F2} allocated_bytes={allocatedBytes:N0}");

        timings.Average().Should().BeGreaterThan(0);
    }

    [Fact]
    public void Benchmark_SaveLoadedDensePostProcessing_ReportsTiming()
    {
        const int iterations = 3;
        var sourcePackage = CreateDenseXlsxPackage();
        var generatedPackage = CreateDenseXlsxPackage();
        var adapter = new XlsxFileAdapter();
        var workbooks = new List<Workbook>(iterations + 1);
        for (var i = 0; i <= iterations; i++)
        {
            using var loadStream = new MemoryStream(sourcePackage, writable: false);
            var workbook = adapter.Load(loadStream);
            workbook.Sheets[0].SetCell(new CellAddress(workbook.Sheets[0].Id, 1, 1), new NumberValue(42 + i));
            workbooks.Add(workbook);
        }

        using (var warmup = CreateWritablePackageStream(generatedPackage))
            InvokeSavePostProcessing(workbooks[0], warmup);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var timings = new List<double>(iterations);
        var packageSizes = new List<long>(iterations);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var total = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            using var stream = CreateWritablePackageStream(generatedPackage);
            var step = Stopwatch.StartNew();
            InvokeSavePostProcessing(workbooks[i + 1], stream);
            step.Stop();
            timings.Add(step.Elapsed.TotalMilliseconds);
            packageSizes.Add(stream.Length);
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var ordered = timings.OrderBy(value => value).ToArray();
        var p95 = ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)];

        Console.WriteLine(
            "PERF XLSX_SAVE_LOADED_DENSE_POSTPROCESSING " +
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
    public void Benchmark_SaveWorksheetAutoFilterNativeMetadataWorkbook_ReportsTiming()
    {
        const int iterations = 3;
        var workbook = CreateWorksheetAutoFilterNativeMetadataWorkbook();
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
            "PERF XLSX_SAVE_WORKSHEET_AUTOFILTER_NATIVE_METADATA " +
            $"sheets={WorksheetNativeMetadataSheetCount} rows={WorksheetNativeMetadataRowsPerSheet} " +
            $"steps={iterations} package_bytes={packageSizes.Max():N0} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={timings.Average():F2} " +
            $"p95_ms={p95:F2} max_ms={ordered[^1]:F2} allocated_bytes={allocatedBytes:N0}");

        timings.Average().Should().BeGreaterThan(0);
    }

    [Fact]
    public void Benchmark_SaveDataValidationNativeMetadataWorkbook_ReportsTiming()
    {
        const int iterations = 3;
        var workbook = CreateDataValidationNativeMetadataWorkbook();
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
            "PERF XLSX_SAVE_DATA_VALIDATION_NATIVE_METADATA " +
            $"sheets={WorksheetNativeMetadataSheetCount} validations_per_sheet={WorksheetNativeMetadataRowsPerSheet} " +
            $"steps={iterations} package_bytes={packageSizes.Max():N0} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={timings.Average():F2} " +
            $"p95_ms={p95:F2} max_ms={ordered[^1]:F2} allocated_bytes={allocatedBytes:N0}");

        timings.Average().Should().BeGreaterThan(0);
    }

    [Fact]
    public void Benchmark_SaveAdvancedConditionalFormattingWorkbook_ReportsTiming()
    {
        const int iterations = 3;
        var workbook = CreateAdvancedConditionalFormattingWorkbook();
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
            "PERF XLSX_SAVE_ADVANCED_CONDITIONAL_FORMATTING " +
            $"sheets={WorksheetNativeMetadataSheetCount} rules_per_sheet={AdvancedConditionalFormatRulesPerSheet} " +
            $"steps={iterations} package_bytes={packageSizes.Max():N0} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={timings.Average():F2} " +
            $"p95_ms={p95:F2} max_ms={ordered[^1]:F2} allocated_bytes={allocatedBytes:N0}");

        timings.Average().Should().BeGreaterThan(0);
    }

    [Fact]
    public void Benchmark_SaveWorksheetSingleXmlCellsPostProcessingWorkbook_ReportsTiming()
    {
        const int iterations = 3;
        var workbook = CreateWorksheetSingleXmlCellsPostProcessingWorkbook();
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
            "PERF XLSX_SAVE_WORKSHEET_SINGLE_XML_CELLS_POSTPROCESSING " +
            $"sheets={WorksheetNativeMetadataSheetCount} rows={WorksheetNativeMetadataRowsPerSheet} " +
            $"single_xml_cells_per_sheet={WorksheetSingleXmlCellsPerSheet} steps={iterations} " +
            $"package_bytes={packageSizes.Max():N0} total_ms={total.Elapsed.TotalMilliseconds:F2} " +
            $"mean_ms={timings.Average():F2} p95_ms={p95:F2} max_ms={ordered[^1]:F2} " +
            $"allocated_bytes={allocatedBytes:N0}");

        timings.Average().Should().BeGreaterThan(0);
    }

    [Fact]
    public void Benchmark_SaveLoadedWorksheetReplayMetadataWorkbook_ReportsTiming()
    {
        const int iterations = 3;
        var package = CreateWorksheetReplayMetadataSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var loadStream = new MemoryStream(package, writable: false))
            workbook = adapter.Load(loadStream);
        ApplyWorksheetReplayMetadata(workbook);

        var markerAddress = new CellAddress(workbook.Sheets[0].Id, 1, 2);
        workbook.Sheets[0].SetCell(markerAddress, new NumberValue(1000));
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
            workbook.Sheets[0].SetCell(markerAddress, new NumberValue(1001 + i));
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
            "PERF XLSX_SAVE_LOADED_WORKSHEET_REPLAY_METADATA " +
            $"sheets={WorksheetReplayMetadataSheetCount} rows={WorksheetReplayMetadataRowsPerSheet} " +
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

        source.Should().Contain("featurePlan.HasPivotCustomNumberFormats");
        source.Should().Contain("private static bool HasPivotCustomNumberFormats(Sheet sheet)");
        source.Should().NotContain(
            "workbook.Sheets.SelectMany(sheet => sheet.PivotTables)",
            "XLSX save post-processing should avoid nested LINQ iterator allocation while deciding whether pivot custom number formats need catalog output");
    }

    [Fact]
    public void SavePostProcessing_BatchesWorkbookFeatureDetection()
    {
        var source = File.ReadAllText(FindRepoFile("src", "FreeX.Core.IO", "XlsxFileAdapter.SavePostProcessing.cs"));

        source.Should().Contain("var featurePlan = XlsxPostProcessingFeaturePlan.Create(workbook);");
        source.Should().Contain("private struct XlsxPostProcessingFeaturePlan");
        source.Should().Contain("foreach (var sheet in workbook.Sheets)");
        source.Should().Contain("sheet.GetOccupiedCellMap()");
        source.Should().NotContain(
            "workbook.Sheets.Any(",
            "XLSX save post-processing should batch sheet feature checks instead of rescanning every sheet for each optional writer");
        source.Should().NotContain(
            "sheet.EnumerateCells().Any",
            "ignored-error detection should avoid nested LINQ and cell-address iterator allocation");
    }

    [Fact]
    public void StylesheetMetadataPreserver_PreflightsPlainStylesheetBeforeLoadingXml()
    {
        var source = File.ReadAllText(FindRepoFile("src", "FreeX.Core.IO", "XlsxStylesheetMetadataPreserver.cs"));

        source.Should().Contain("HasPreservableStylesheetMetadata(sourceStylesEntry)");
        source.Should().Contain("case \"colors\":");
        source.Should().Contain("case \"extLst\":");
        source.Should().Contain("case \"dxfs\":");
        source.Should().Contain("case \"tableStyles\":");
        source.Should().Contain("TableStyleMedium2");
        source.Should().Contain("PivotStyleLight16");
        source.Should().Contain("return true;");
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

        adapterSource.Should().Contain("workbookMetadata = packageParts.HasWorkbook");
        adapterSource.Should().Contain("XlsxWorkbookMetadataReader.LoadWorkbookMetadata(packageArchive)");
        adapterSource.Should().NotContain("XlsxWorkbookMetadataReader.LoadWorkbookMetadata(packageStream)");
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
        metadataSource.Should().Contain("internal static XlsxWorkbookMetadataSnapshot LoadWorkbookMetadata(ZipArchive archive)");
        metadataSource.Should().Contain("var workbookEntry = archive.GetEntry(\"xl/workbook.xml\");");
        metadataSource.Should().Contain("return LoadWorkbookMetadata(workbookXml);");
    }

    [Fact]
    public void LoadCore_ReusesSingleStylesheetParseForLoadMetadata()
    {
        var adapterSource = File.ReadAllText(FindRepoFile("src", "FreeX.Core.IO", "XlsxFileAdapter.cs"));

        adapterSource.Should().Contain("stylesXml = packageParts.HasStyles");
        adapterSource.Should().Contain("XlsxStylesheetReader.Load(packageArchive)");
        adapterSource.Should().Contain("XlsxWorkbookMetadataReader.LoadNumberFormatCatalog(stylesXml)");
        adapterSource.Should().Contain("XlsxIndexedColorPaletteMapper.Load(stylesXml)");
        adapterSource.Should().Contain("XlsxPivotTableStyleMetadataReader.Load(stylesXml)");
        adapterSource.Should().Contain("XlsxStructuredTableStyleMetadataReader.Load(stylesXml)");
        adapterSource.Should().Contain("LoadSheetXmlLayout(packageStream, stylesXml, workbookTheme, indexedColors, warnings)");
        adapterSource.Should().NotContain("XlsxStylesheetReader.Load(packageStream)");
        adapterSource.Should().NotContain("LoadNumberFormatCatalog(packageStream)");
        adapterSource.Should().NotContain("XlsxIndexedColorPaletteMapper.Load(packageStream)");
        adapterSource.Should().NotContain("XlsxPivotTableStyleMetadataReader.Load(packageStream)");
        adapterSource.Should().NotContain("XlsxStructuredTableStyleMetadataReader.Load(packageStream)");
        adapterSource.Should().NotContain("LoadSheetXmlLayout(packageStream);");
    }

    [Fact]
    public void LoadCore_UsesPackagePartSummaryToSkipOptionalMetadataReaders()
    {
        var adapterSource = File.ReadAllText(FindRepoFile("src", "FreeX.Core.IO", "XlsxFileAdapter.cs"));

        adapterSource.Should().Contain("packageParts = XlsxLoadPackageParts.Inspect(packageArchive);");
        adapterSource.Should().Contain("if (packageParts.HasPivotPackageParts)");
        adapterSource.Should().Contain("if (packageParts.HasSlicerTimelinePackageParts)");
        adapterSource.Should().Contain("if (packageParts.HasExternalLinks)");
        adapterSource.Should().Contain("if (packageParts.HasStructuredTables)");
        adapterSource.Should().Contain("XlsxPivotTableReader.Load(packageArchive, numberFormatCatalog)");
        adapterSource.Should().Contain("XlsxSlicerTimelineMetadataReader.Load(packageArchive)");
        adapterSource.Should().Contain("XlsxExternalLinkMetadataReader.Load(packageArchive)");
        adapterSource.Should().Contain("XlsxStructuredTableMetadataReader.Load(packageArchive)");
        adapterSource.Should().NotContain("XlsxPivotTableReader.Load(packageStream, numberFormatCatalog)");
        adapterSource.Should().NotContain("XlsxSlicerTimelineMetadataReader.Load(packageStream)");
        adapterSource.Should().NotContain("XlsxExternalLinkMetadataReader.Load(packageStream)");
        adapterSource.Should().NotContain("XlsxStructuredTableMetadataReader.Load(packageStream)");
    }

    [Fact]
    public void Save_UsesSaveScopedStyleCacheForStyleLookup()
    {
        var saveSource = File.ReadAllText(FindRepoFile("src", "FreeX.Core.IO", "XlsxFileAdapter.Save.cs"));

        saveSource.Should().Contain("var styleCache = new Dictionary<StyleId, CellStyle>(workbook.StyleCount);");
        saveSource.Should().Contain("GetCachedStyle(workbook, styleCache, cell.StyleId)");
        saveSource.Should().Contain("GetCachedStyle(workbook, styleCache, seed.StyleId)");
        saveSource.Should().Contain("style = workbook.GetStyle(styleId);");
        saveSource.Should().NotContain("workbook.GetStyle(cell.StyleId)");
        saveSource.Should().NotContain("workbook.GetStyle(seed.StyleId)");
    }

    [Fact]
    public void Save_ExpandsStyleOnlyCellsInPostProcessingAfterClosedXmlStyleSeeding()
    {
        var saveSource = File.ReadAllText(FindRepoFile("src", "FreeX.Core.IO", "XlsxFileAdapter.Save.cs"));
        var postProcessingSource = File.ReadAllText(FindRepoFile("src", "FreeX.Core.IO", "XlsxFileAdapter.SavePostProcessing.cs"));
        var writerSource = File.ReadAllText(FindRepoFile("src", "FreeX.Core.IO", "XlsxStyleOnlyCellWriter.cs"));

        saveSource.Should().Contain("ApplyStyleOnlySeedCells");
        saveSource.Should().Contain("XlsxStyleOnlyCellWriter.GetSeedCells(sheet)");
        saveSource.Should().NotContain("GetStyleOnlyRuns");
        postProcessingSource.Should().Contain("featurePlan.HasStyleOnlyCells");
        postProcessingSource.Should().Contain("XlsxStyleOnlyCellWriter.Save(packageStream, workbook, GetWorksheetPathMap());");
        writerSource.Should().Contain("ReadSeedStyleIndexes");
        writerSource.Should().Contain("ApplyStyleOnlyCells");
        writerSource.Should().Contain("UpdateDimension");
    }

    [Fact]
    public void SavePostProcessing_BatchesWorkbookMetadataXmlWrites()
    {
        var adapterSource = File.ReadAllText(FindRepoFile("src", "FreeX.Core.IO", "XlsxFileAdapter.SavePostProcessing.cs"));
        var writerSource = File.ReadAllText(FindRepoFile("src", "FreeX.Core.IO", "XlsxWorkbookMetadataWriter.cs"));

        adapterSource.Should().Contain("XlsxWorkbookMetadataWriter.SavePostProcessingMetadata(packageStream, workbook);");
        adapterSource.Should().Contain("if (featurePlan.HasWorkbookPostProcessingMetadata)");
        adapterSource.Should().Contain("XlsxWorkbookMetadataWriter.SaveSourcePackageReplayMetadata(packageStream, workbook);");
        adapterSource.Should().NotContain("XlsxWorkbookMetadataWriter.SaveWorkbookProperties(packageStream, workbook);");
        adapterSource.Should().NotContain("XlsxWorkbookMetadataWriter.SaveCalculationProperties(packageStream, workbook);");
        adapterSource.Should().NotContain("XlsxWorkbookAdditionalViewMapper.Save(packageStream, workbook);");

        writerSource.Should().Contain("public static bool HasPostProcessingMetadata(Workbook workbook)");
        writerSource.Should().Contain("private static bool HasCalculationProperties(Workbook workbook)");
        writerSource.Should().Contain("public static void SavePostProcessingMetadata(Stream xlsxStream, Workbook workbook)");
        writerSource.Should().Contain("public static void SaveSourcePackageReplayMetadata(Stream xlsxStream, Workbook workbook)");
        writerSource.Should().Contain("private static void SaveWorkbookXml(Stream xlsxStream, Workbook workbook");
    }

    [Fact]
    public void SavePostProcessing_BatchesWorksheetNativeMetadataXmlWrites()
    {
        var adapterSource = File.ReadAllText(FindRepoFile("src", "FreeX.Core.IO", "XlsxFileAdapter.SavePostProcessing.cs"));
        var saveSource = File.ReadAllText(FindRepoFile("src", "FreeX.Core.IO", "XlsxFileAdapter.Save.cs"));
        var batchSource = File.ReadAllText(FindRepoFile("src", "FreeX.Core.IO", "XlsxWorksheetNativeMetadataBatchWriter.cs"));
        var sourceIndependentBatchSource = File.ReadAllText(FindRepoFile(
            "src",
            "FreeX.Core.IO",
            "XlsxWorksheetSourceIndependentMetadataBatchWriter.cs"));
        var dataValidationNativeSource = File.ReadAllText(FindRepoFile(
            "src",
            "FreeX.Core.IO",
            "XlsxDataValidationNativeMetadataMapper.cs"));
        var sessionSource = File.ReadAllText(FindRepoFile("src", "FreeX.Core.IO", "XlsxWorksheetXmlEditSession.cs"));

        adapterSource.Should().Contain("XlsxWorksheetSourceIndependentMetadataBatchWriter.Save(packageStream, workbook, GetWorksheetPathMap());");
        adapterSource.Should().Contain("XlsxWorksheetSourceIndependentMetadataBatchWriter.HasMetadata");
        adapterSource.Should().NotContain("XlsxWorksheetNativeMetadataBatchWriter.Save(packageStream, workbook, GetWorksheetPathMap());");
        adapterSource.Should().NotContain("XlsxWorksheetAutoFilterMapper.Save(packageStream, workbook, GetWorksheetPathMap());");
        adapterSource.Should().NotContain("XlsxDataValidationNativeMetadataMapper.Save(packageStream, workbook);");
        adapterSource.Should().NotContain("HasSourcePackageIndependentWorksheetNativeMetadata");
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
        batchSource.Should().Contain("internal static void Save(XlsxWorksheetXmlEditSession session, Workbook workbook)");
        batchSource.Should().Contain("XlsxWorksheetProtectionMetadataWriter.Save(session, workbook);");
        batchSource.Should().Contain("XlsxWorksheetHeaderFooterMetadataWriter.Save(session, workbook);");
        sourceIndependentBatchSource.Should().Contain("XlsxWorksheetAutoFilterMapper.Save(session, workbook);");
        sourceIndependentBatchSource.Should().Contain("XlsxDataValidationNativeMetadataMapper.Save(session, workbook);");
        sourceIndependentBatchSource.Should().Contain("XlsxWorksheetNativeMetadataBatchWriter.Save(session, workbook);");
        saveSource.Should().Contain("if (!XlsxDataValidationNativeMetadataMapper.HasNativeMetadata(sheet))");
        dataValidationNativeSource.Should().Contain("TryCreateDataValidationsElement(sheet, containerSource, out var replacement)");
        dataValidationNativeSource.Should().Contain("AddDataValidationsInOrder(edit.Root, replacement);");
        dataValidationNativeSource.Should().Contain("XlsxDataValidationClosedXmlMapper.NormalizeListFormulaForSave");
        sessionSource.Should().Contain("private readonly Dictionary<string, XDocument> _documents");
        sessionSource.Should().Contain("XlsxPackageXmlEditor.ReplaceXml(_archive, path, _documents[path]);");
    }

    [Fact]
    public void SavePostProcessing_BatchesSourcePackageReplayWorksheetMetadataXmlWrites()
    {
        var adapterSource = File.ReadAllText(FindRepoFile("src", "FreeX.Core.IO", "XlsxFileAdapter.SavePostProcessing.cs"));
        var batchSource = File.ReadAllText(FindRepoFile(
            "src",
            "FreeX.Core.IO",
            "XlsxWorksheetPostProcessingMetadataBatchWriter.cs"));

        adapterSource.Should().Contain(
            "XlsxWorksheetPostProcessingMetadataBatchWriter.Save(packageStream, workbook, GetWorksheetPathMap());");
        adapterSource.Should().Contain(
            "XlsxWorksheetPostProcessingMetadataBatchWriter.SaveWorksheetElementMetadata(");
        adapterSource.Should().Contain("XlsxWorksheetPostProcessingMetadataBatchWriter.HasReplayMetadata");
        adapterSource.Should().Contain("XlsxWorksheetPostProcessingMetadataBatchWriter.HasWorksheetElementMetadata");
        adapterSource.Should().NotContain("XlsxWorksheetSingleXmlCellMapper.Save(packageStream, workbook, GetWorksheetPathMap());");
        batchSource.Should().Contain("using var session = new XlsxWorksheetXmlEditSession(xlsxStream, worksheetPathMap);");
        batchSource.Should().Contain("sheet.SingleXmlCells is not null");
        batchSource.Should().Contain("XlsxWorksheetSmartTagMapper.Save(session, workbook);");
        batchSource.Should().Contain("XlsxWorksheetSortStateMapper.Save(session, workbook);");
        batchSource.Should().Contain("XlsxWorksheetAdditionalViewMapper.Save(session, workbook);");
        batchSource.Should().Contain("XlsxWorksheetDataConsolidationMapper.Save(session, workbook);");
        batchSource.Should().Contain("XlsxWorksheetSingleXmlCellMapper.Save(session, workbook);");
        batchSource.Should().Contain("XlsxWorksheetPageSetupMetadataWriter.Save(session, workbook);");
    }

    private const int DenseSheetCount = 8;
    private const int DenseRowsPerSheet = 80;
    private const int DenseColumnsPerSheet = 24;
    private const int StyleOnlySaveSheetCount = 2;
    private const int StyleOnlySaveRowsPerSheet = 600;
    private const int StyleOnlySaveColumnsPerSheet = 72;
    private const int StyleOnlySaveRunWidth = 8;
    private const int WorksheetNativeMetadataSheetCount = 8;
    private const int WorksheetNativeMetadataRowsPerSheet = 40;
    private const int WorksheetReplayMetadataSheetCount = 8;
    private const int WorksheetReplayMetadataRowsPerSheet = 40;
    private const int AdvancedConditionalFormatRulesPerSheet = 40;
    private const int WorksheetSingleXmlCellsPerSheet = 40;
    private const int IgnoredErrorStyleOnlyRows = 800;
    private const int IgnoredErrorStyleOnlyValueColumns = 30;
    private const int IgnoredErrorStyleOnlyStyleColumns = 10;
    private const int IgnoredErrorStyleOnlyIgnoredRanges = 800;
    private const int IgnoredErrorSaveRows = 300;
    private const int IgnoredErrorSaveColumns = 40;

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

    private static byte[] CreateIgnoredErrorAndStyleOnlyMetadataPackage()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Metadata");
        for (var row = 1; row <= IgnoredErrorStyleOnlyRows; row++)
        {
            for (var col = 1; col <= IgnoredErrorStyleOnlyValueColumns; col++)
                sheet.Cell(row, col).Value = row * col;

            for (var col = IgnoredErrorStyleOnlyValueColumns + 2;
                 col < IgnoredErrorStyleOnlyValueColumns + 2 + IgnoredErrorStyleOnlyStyleColumns;
                 col++)
            {
                var styleOnlyCell = sheet.Cell(row, col);
                styleOnlyCell.Style.Fill.BackgroundColor = XLColor.FromArgb(221, 235, 247);
                styleOnlyCell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
            var worksheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml")!;
            XDocument worksheetXml;
            using (var worksheetStream = worksheetEntry.Open())
                worksheetXml = XDocument.Load(worksheetStream);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            worksheetXml.Root!.Element(ns + "ignoredErrors")?.Remove();

            var ignoredErrors = new XElement(ns + "ignoredErrors");
            for (var rangeIndex = 1; rangeIndex <= IgnoredErrorStyleOnlyIgnoredRanges; rangeIndex++)
            {
                ignoredErrors.Add(new XElement(
                    ns + "ignoredError",
                    new XAttribute("sqref", $"A{rangeIndex}:AD{rangeIndex + 999}"),
                    new XAttribute("numberStoredAsText", "1")));
            }

            worksheetXml.Root.Add(ignoredErrors);
            ReplaceZipEntryXml(archive, worksheetEntry.FullName, worksheetXml);
        }

        return stream.ToArray();
    }

    private static void AssertIgnoredErrorAndStyleOnlyMetadata(Workbook workbook)
    {
        workbook.SheetCount.Should().Be(1);
        var sheet = workbook.Sheets[0];
        sheet.EnumerateCells().Count(pair => pair.Cell.IgnoreFormulaError)
            .Should().Be(IgnoredErrorStyleOnlyRows * IgnoredErrorStyleOnlyValueColumns);
        sheet.GetStyleOnlyEntries().Count()
            .Should().Be(IgnoredErrorStyleOnlyRows * IgnoredErrorStyleOnlyStyleColumns);
    }

    private static void ReplaceZipEntryXml(ZipArchive archive, string entryName, XDocument document)
    {
        archive.GetEntry(entryName)?.Delete();
        var entry = archive.CreateEntry(entryName);
        using var stream = entry.Open();
        document.Save(stream, System.Xml.Linq.SaveOptions.DisableFormatting);
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

    private static Workbook CreateDrawingPicturesWorkbook(int pictureCount)
    {
        var workbook = new Workbook("Drawing Pictures IO");
        var sheet = workbook.AddSheet("Sheet1");
        var imageBytes = MinimalPngBytes();
        for (var index = 0; index < pictureCount; index++)
        {
            var row = (uint)(1 + index / 18);
            var column = (uint)(1 + index % 18);
            sheet.Pictures.Add(new PictureModel
            {
                Name = $"Picture {index + 1}",
                Anchor = new CellAddress(sheet.Id, row, column),
                Kind = PictureKind.Image,
                ImageBytes = imageBytes,
                ContentType = "image/png",
                Width = 72,
                Height = 48,
                AltText = $"Drawing picture {index + 1}"
            });
        }

        return workbook;
    }

    private static byte[] MinimalPngBytes() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82
    ];

    private static Workbook CreateIgnoredErrorsSaveWorkbook()
    {
        var workbook = new Workbook("Ignored Errors Save IO");
        var sheet = workbook.AddSheet("Data");
        for (uint row = 1; row <= IgnoredErrorSaveRows; row++)
        {
            for (uint col = 1; col <= IgnoredErrorSaveColumns; col++)
            {
                sheet.SetCell(
                    new CellAddress(sheet.Id, row, col),
                    new TextValue($"{row:D4}{col:D2}"));
                sheet.GetCell(row, col)!.IgnoreFormulaError = true;
            }
        }

        return workbook;
    }

    private static MemoryStream CreateWritablePackageStream(byte[] package)
    {
        var stream = new MemoryStream(package.Length * 2);
        stream.Write(package, 0, package.Length);
        stream.Position = 0;
        return stream;
    }

    private static void InvokeSavePostProcessing(Workbook workbook, Stream stream)
    {
        var method = typeof(XlsxFileAdapter).GetMethod(
            "ApplyPackagePostProcessing",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        method!.Invoke(null, [workbook, stream, null]);
    }

    private static Workbook CreateStyleOnlyModelWorkbook()
    {
        var workbook = new Workbook("Style-only IO");
        var styleIds = new[]
        {
            workbook.RegisterStyle(new CellStyle
            {
                FillColor = new CellColor(221, 235, 247),
                BorderBottom = new CellBorder(BorderStyle.Thin, new CellColor(91, 155, 213))
            }),
            workbook.RegisterStyle(new CellStyle
            {
                FillColor = new CellColor(226, 239, 218),
                BorderBottom = new CellBorder(BorderStyle.Thin, new CellColor(112, 173, 71))
            }),
            workbook.RegisterStyle(new CellStyle
            {
                FillColor = new CellColor(252, 228, 214),
                BorderBottom = new CellBorder(BorderStyle.Thin, new CellColor(237, 125, 49))
            })
        };

        for (var sheetIndex = 1; sheetIndex <= StyleOnlySaveSheetCount; sheetIndex++)
        {
            var sheet = workbook.AddSheet($"Styled blanks {sheetIndex}");
            for (uint row = 1; row <= StyleOnlySaveRowsPerSheet; row++)
            {
                sheet.SetCell(
                    new CellAddress(sheet.Id, row, 1),
                    new NumberValue(row + (uint)sheetIndex));

                for (uint col = 3; col < 3 + StyleOnlySaveColumnsPerSheet; col++)
                {
                    var runIndex = (col - 3) / StyleOnlySaveRunWidth;
                    var styleIndex = (int)((runIndex + row + (uint)sheetIndex) % (uint)styleIds.Length);
                    sheet.SetStyleOnly(row, col, styleIds[styleIndex]);
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

    private static Workbook CreateWorksheetAutoFilterNativeMetadataWorkbook()
    {
        var workbook = CreateWorksheetNativeMetadataWorkbook();
        foreach (var sheet in workbook.Sheets)
        {
            sheet.AutoFilter = new WorksheetAutoFilterModel(
                $"A1:B{WorksheetNativeMetadataRowsPerSheet}",
                null)
            {
                NativeAttributes = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["customAutoFilterAttr"] = $"auto-filter-{sheet.Name}"
                }
            };
            sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(
                0,
                [$"R{WorksheetNativeMetadataRowsPerSheet / 2}", $"R{WorksheetNativeMetadataRowsPerSheet}"],
                IncludeBlank: false));
            sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(
                1,
                [],
                IncludeBlank: true,
                CustomFilters: [new WorksheetAutoFilterCustomFilterModel("greaterThanOrEqual", "10")],
                CustomFiltersAnd: false,
                NativeCustomFiltersAttributes: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["customFiltersAttr"] = $"custom-filters-{sheet.Name}"
                },
                NativeFilterXmls: [],
                NativeAttributes: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["customFilterColumnAttr"] = $"filter-column-{sheet.Name}"
                }));
        }

        return workbook;
    }

    private static Workbook CreateDataValidationNativeMetadataWorkbook()
    {
        var workbook = new Workbook("Data validation native metadata IO");
        for (var sheetIndex = 1; sheetIndex <= WorksheetNativeMetadataSheetCount; sheetIndex++)
        {
            var sheet = workbook.AddSheet($"DV Metadata {sheetIndex}");
            for (uint row = 1; row <= WorksheetNativeMetadataRowsPerSheet; row++)
            {
                sheet.SetCell(
                    new CellAddress(sheet.Id, row, 1),
                    new NumberValue(row));
                sheet.DataValidations.Add(new DataValidation
                {
                    AppliesTo = new GridRange(
                        new CellAddress(sheet.Id, row, 1),
                        new CellAddress(sheet.Id, row, 1)),
                    Type = DvType.WholeNumber,
                    Operator = DvOperator.Between,
                    Formula1 = "1",
                    Formula2 = "100",
                    NativeAttributes = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["imeMode"] = "noControl",
                        ["customDvAttr"] = $"dv-{sheetIndex}-{row}"
                    },
                    NativeChildXmls =
                    [
                        $"<extLst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><ext uri=\"{{FREEX-DV-{sheetIndex}-{row}}}\" /></extLst>"
                    ],
                    NativeContainerAttributes = row == 1
                        ? new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["disablePrompts"] = "0",
                            ["customDvContainerAttr"] = $"container-{sheetIndex}"
                        }
                        : null
                });
            }
        }

        return workbook;
    }

    private static Workbook CreateAdvancedConditionalFormattingWorkbook()
    {
        var workbook = new Workbook("Advanced conditional formatting IO");
        for (var sheetIndex = 1; sheetIndex <= WorksheetNativeMetadataSheetCount; sheetIndex++)
        {
            var sheet = workbook.AddSheet($"CF Metadata {sheetIndex}");
            for (uint row = 1; row <= WorksheetNativeMetadataRowsPerSheet; row++)
            {
                sheet.SetCell(
                    new CellAddress(sheet.Id, row, 1),
                    new NumberValue(row + sheetIndex));
            }

            for (uint row = 1; row <= AdvancedConditionalFormatRulesPerSheet; row++)
            {
                sheet.ConditionalFormats.Add(new ConditionalFormat
                {
                    AppliesTo = new GridRange(
                        new CellAddress(sheet.Id, row, 1),
                        new CellAddress(sheet.Id, row, 1)),
                    Priority = (int)row,
                    RuleType = CfRuleType.DataBar,
                    DataBarGradient = false,
                    DataBarBorder = true,
                    DataBarAxisPosition = "middle",
                    DataBarAxisColor = new RgbColor(0, 0, 0),
                    DataBarNegativeFillColor = new RgbColor(156, 0, 6),
                    DataBarNegativeBorderColor = new RgbColor(156, 0, 6),
                    NativePayloadChildXmls =
                    [
                        $"<x14:customPayload xmlns:x14=\"http://schemas.microsoft.com/office/spreadsheetml/2009/9/main\" id=\"{sheetIndex}-{row}\" />"
                    ],
                    FormatIfTrue = new CellStyle
                    {
                        FillColor = new CellColor(198, 239, 206),
                        FontColor = new CellColor(0, 97, 0),
                        BorderBottom = new CellBorder(BorderStyle.Thin, new CellColor(0, 97, 0))
                    }
                });
            }
        }

        return workbook;
    }

    private static Workbook CreateWorksheetSingleXmlCellsPostProcessingWorkbook()
    {
        var workbook = new Workbook("Worksheet singleXmlCells IO");
        for (var sheetIndex = 1; sheetIndex <= WorksheetNativeMetadataSheetCount; sheetIndex++)
        {
            var sheet = workbook.AddSheet($"SingleXml {sheetIndex}");
            for (uint row = 1; row <= WorksheetNativeMetadataRowsPerSheet; row++)
            {
                sheet.SetCell(
                    new CellAddress(sheet.Id, row, 1),
                    new TextValue($"R{row}"));
            }

            sheet.SmartTags = new WorksheetSmartTagsModel
            {
                NativeXml = "<smartTags xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                    $"<cellSmartTags r=\"A{sheetIndex}\"><cellSmartTag type=\"{sheetIndex}\" deleted=\"0\">" +
                    $"<cellSmartTagPr key=\"place\" val=\"City{sheetIndex}\" /></cellSmartTag></cellSmartTags></smartTags>"
            };
            sheet.SingleXmlCells = new WorksheetSingleXmlCellsModel
            {
                NativeAttributes =
                {
                    ["nativeSingleXmlCellsAttr"] = $"single-xml-{sheetIndex}"
                }
            };
            for (var cellIndex = 1; cellIndex <= WorksheetSingleXmlCellsPerSheet; cellIndex++)
            {
                sheet.SingleXmlCells.Cells.Add(new WorksheetSingleXmlCellModel
                {
                    Id = cellIndex,
                    Reference = $"A{cellIndex}",
                    XmlCellPropertyId = 1000 + cellIndex,
                    NativeAttributes =
                    {
                        ["nativeSingleXmlCellAttr"] = $"single-cell-{sheetIndex}-{cellIndex}"
                    }
                });
            }
        }

        return workbook;
    }

    private static byte[] CreateWorksheetReplayMetadataSourcePackage()
    {
        using var workbook = new XLWorkbook();
        for (var sheetIndex = 1; sheetIndex <= WorksheetReplayMetadataSheetCount; sheetIndex++)
        {
            var sheet = workbook.Worksheets.Add($"Replay {sheetIndex}");
            for (var row = 1; row <= WorksheetReplayMetadataRowsPerSheet; row++)
                sheet.Cell(row, 1).Value = $"R{row}";
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void ApplyWorksheetReplayMetadata(Workbook workbook)
    {
        for (var i = 0; i < workbook.Sheets.Count; i++)
        {
            var sheet = workbook.Sheets[i];
            var sheetIndex = i + 1;
            sheet.SmartTags = new WorksheetSmartTagsModel
            {
                NativeXml = "<smartTags xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                    $"<cellSmartTags r=\"A{sheetIndex}\"><cellSmartTag type=\"{sheetIndex}\" deleted=\"0\">" +
                    $"<cellSmartTagPr key=\"place\" val=\"City{sheetIndex}\" /></cellSmartTag></cellSmartTags></smartTags>"
            };
            sheet.SortState = new WorksheetSortStateModel
            {
                Reference = $"A1:A{WorksheetReplayMetadataRowsPerSheet}",
                CaseSensitive = true,
                Conditions =
                [
                    new WorksheetSortConditionModel
                    {
                        Reference = $"A1:A{WorksheetReplayMetadataRowsPerSheet}",
                        Descending = sheetIndex % 2 == 0,
                        SortBy = "value"
                    }
                ]
            };
            sheet.AdditionalViews = new WorksheetAdditionalViewsModel
            {
                NativeAttributes = { ["customSheetViewsAttr"] = $"views-{sheetIndex}" },
                Views =
                [
                    new WorksheetAdditionalViewModel
                    {
                        WorkbookViewId = (sheetIndex + 1).ToString(CultureInfo.InvariantCulture),
                        NativeAttributes = { ["customViewAttr"] = $"view-{sheetIndex}" }
                    }
                ]
            };
            sheet.DataConsolidation = new WorksheetDataConsolidationModel
            {
                Function = "sum",
                LeftLabels = true,
                TopLabels = true,
                Link = sheetIndex % 2 == 0,
                NativeAttributes = { ["customDataConsolidationFlag"] = $"data-{sheetIndex}" },
                References =
                [
                    new WorksheetDataConsolidationReferenceModel
                    {
                        Reference = "A1:A2",
                        Sheet = sheet.Name,
                        NativeAttributes = { ["customDataRefFlag"] = $"ref-{sheetIndex}" }
                    }
                ]
            };
            sheet.UsePrinterDefaults = false;
            sheet.PrintCopies = 2 + sheetIndex;
            sheet.PrintQualityVerticalDpi = 300 + sheetIndex;
            sheet.PageSetupMetadata = MakeBag(
                "pageSetup",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["customPageSetupAttr"] = $"page-setup-{sheetIndex}"
                },
                [$"<fx:nativePageSetupChild xmlns:fx=\"urn:freex:test\" id=\"{sheetIndex}\" />"]);
        }
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
