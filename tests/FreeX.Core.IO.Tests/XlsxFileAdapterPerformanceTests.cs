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
    private static string CoinToolCorpusWorkbookPath =>
        TestWorkspaceFiles.FindRepoFile("test-corpus", "local-private", "COIN_Tool_v1_FULL_exampledata.xlsm");

    private static void PrepareLoadedWorkbookForEdit(Workbook workbook)
    {
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);
    }

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
    public void Benchmark_LoadCoinToolCorpusWorkbook_ReportsTimingWhenAvailable()
    {
        var path = CoinToolCorpusWorkbookPath;
        if (!File.Exists(path))
        {
            Console.WriteLine(
                "PERF XLSM_LOAD_COIN_TOOL_CORPUS skipped=true " +
                $"reason=CORPUS_WORKBOOK_NOT_FOUND path=\"{path}\"");
            return;
        }

        var adapter = new XlsxFileAdapter();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        using var stream = File.OpenRead(path);
        var workbook = adapter.Load(stream);
        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        workbook.SheetCount.Should().BeGreaterThan(0);
        Console.WriteLine(
            "PERF XLSM_LOAD_COIN_TOOL_CORPUS " +
            $"file=\"{Path.GetFileName(path)}\" " +
            $"bytes={new FileInfo(path).Length:N0} sheets={workbook.SheetCount} " +
            $"cells={workbook.Sheets.Sum(sheet => sheet.CellCount):N0} " +
            $"elapsed_ms={stopwatch.Elapsed.TotalMilliseconds:F2} allocated_bytes={allocatedBytes:N0}");
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
                var sanitizedPackage = XlsxClosedXmlLoadPackageSanitizer.Create(
                    sourcePackage,
                    styleOnlyWorksheetPathsToStrip: null);
                try
                {
                    using var workbook = new XLWorkbook(sanitizedPackage);
                    workbook.Worksheets.Count.Should().BeGreaterThan(0);
                }
                finally
                {
                    if (sanitizedPackage is not null &&
                        !ReferenceEquals(sanitizedPackage, sourcePackage))
                    {
                        sanitizedPackage.Dispose();
                    }
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
            PrepareLoadedWorkbookForEdit(workbook);

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
                $"elapsed_ms={stopwatch.Elapsed.TotalMilliseconds:F2} allocated_bytes={allocatedBytes:N0} " +
                FormatSaveDiagnostics(adapter));
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
        var source = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxSparklineMapper.cs");
        var readMethod = source[
            source.IndexOf("public static IReadOnlyList<XlsxSparklineLayout> Read", StringComparison.Ordinal)..
            source.IndexOf("public static void Save", StringComparison.Ordinal)];

        readMethod.Should().Contain("FindChildByLocalName(worksheetXml.Root, \"extLst\")");
        readMethod.Should().Contain("FindChildByLocalName(sparkline, \"f\")");
        readMethod.Should().Contain("return [];");
        readMethod.Should().Contain("extensionList.Descendants()");
        readMethod.Should().NotContain("worksheetXml.Descendants()");
    }

    [Fact]
    public void LoadSourcePackageCapture_ReusesOnlyOwnedPackageBuffers()
    {
        var adapterSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxFileAdapter.cs");
        var snapshotSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxFileAdapter.SourcePackageSnapshot.cs");

        adapterSource.Should().Contain("loadPackage.CanReuseBufferForSnapshot");
        adapterSource.Should().Contain("CanReuseBufferForSnapshot: false");
        adapterSource.Should().Contain("CanReuseBufferForSnapshot: true");
        adapterSource.Should().Contain("sourceHasWorkbookCustomViews: xlsxCustomViews.Count > 0");
        snapshotSource.Should().Contain("allowBufferReuse &&");
        snapshotSource.Should().Contain("buffer.Array,");
        snapshotSource.Should().Contain("worksheetsWithPreservableSourceMetadata");
    }

    [Fact]
    public void LoadSourcePackageCapture_FingerprintsDenseSaveBenchmarkForCopyFastPath()
    {
        var snapshotSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxFileAdapter.SourcePackageSnapshot.cs");

        snapshotSource.Should().Contain("private const int FingerprintCellLimit = 100_000;");
        snapshotSource.Should().Contain("private const int FingerprintCompressedStyleOnlyCellLimit = 1_250_000;");
        snapshotSource.Should().Contain("styleOnlyCellCount += sheet.StyleOnlyCellCount;");
        snapshotSource.Should().Contain("!sheet.TryGetCompressedStyleOnlyRuns(out _)");
        (DenseSheetCount * DenseRowsPerSheet * DenseColumnsPerSheet).Should().BeLessThan(100_000);
    }

    [Fact]
    public void LoadSourcePackageCapture_CountsStyleOnlyEntriesTowardFingerprintLimit()
    {
        var snapshotSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxFileAdapter.SourcePackageSnapshot.cs");

        snapshotSource.Should().Contain("sheet.HasStyleOnlyCells");
        snapshotSource.Should().Contain("sheet.GetStyleOnlyEntries()");
    }

    [Fact]
    public void LoadPath_BoundsStyleOnlyStripperToLargeStyleOnlyLayouts()
    {
        var adapterSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxFileAdapter.cs");

        adapterSource.Should().Contain("private const int ClosedXmlStyleOnlyStripCellThreshold = 16_384;");
        adapterSource.Should().Contain("if (sheetXmlLayoutHadWarnings || sheetXmlLayout.Count == 0)");
        adapterSource.Should().Contain("explicitStyleOnlyCellCount += layout.ExplicitStyleOnlyCells.Count;");
        adapterSource.Should().Contain("explicitStyleOnlyCellCount <= ClosedXmlStyleOnlyStripCellThreshold");
        adapterSource.Should().Contain("layout.HasDuplicateStyleOnlyCellStyleIndexes");
    }

    [Fact]
    public void LoadPath_PreSizesSheetCellStorageFromSheetDataLayout()
    {
        var adapterSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxFileAdapter.cs");
        var cellLayoutSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxWorksheetCellLayoutReader.cs");
        var sheetXmlLayoutSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxFileAdapter.SheetXmlLayout.cs");

        cellLayoutSource.Should().Contain("int PopulatedCellCount");
        sheetXmlLayoutSource.Should().Contain("cellLayout.PopulatedCellCount");
        adapterSource.Should().Contain("sheet.EnsureCellCapacity(layoutWithCells.PopulatedCellCount);");
    }

    [Fact]
    public void SaveSourcePackageCapture_ReusesSaveFingerprintForSnapshot()
    {
        var adapterSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxFileAdapter.cs");
        var saveSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxFileAdapter.Save.cs");
        var postProcessingSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxFileAdapter.SavePostProcessing.cs");
        var snapshotSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxFileAdapter.SourcePackageSnapshot.cs");

        adapterSource.Should().Contain("sourceNeedsPackageGraphNormalization: XlsxDocumentPropertiesPreserver.NeedsPackageGraphNormalization(packageStream)");
        saveSource.Should().Contain("string? currentModelFingerprint = null;");
        saveSource.Should().Contain("sourcePackage!.Matches(workbook, out currentModelFingerprint)");
        saveSource.Should().Contain("sourcePackage.TrySavePatchedCellValues(");
        saveSource.Should().Contain("ref currentModelFingerprint,");
        saveSource.Should().Contain("out patchDiagnostics");
        postProcessingSource.Should().Contain("currentModelFingerprint,");
        postProcessingSource.Should().Contain("sourcePackage?.WorksheetsWithPreservableSourceMetadata");
        postProcessingSource.Should().Contain("SourceNeedsPackageGraphNormalization = false");
        snapshotSource.Should().Contain("public bool Matches(Workbook workbook, out string? currentModelFingerprint)");
        snapshotSource.Should().Contain("ref string? currentModelFingerprint");
        snapshotSource.Should().Contain("currentModelFingerprint,");
        snapshotSource.Should().Contain("GetModelFingerprint(workbook, currentModelFingerprint)");
        snapshotSource.Should().Contain("using var cryptoStream = new CryptoStream(Stream.Null, hash, CryptoStreamMode.Write, leaveOpen: true);");
        snapshotSource.Should().Contain("using var stream = new BufferedStream(cryptoStream, bufferSize: 64 * 1024);");
        snapshotSource.Should().Contain("cryptoStream.FlushFinalBlock();");
        snapshotSource.Should().Contain("TryPrepareLoadedPackageSnapshotForEdit(Workbook workbook, out string? blockReason)");
        snapshotSource.Should().Contain("TryEnsureCellPatchBaseline(");
        snapshotSource.Should().Contain("sourcePackage.TryEnsureCellPatchEligibility(workbook, out var preparedPackage, out blockReason)");
        snapshotSource.Should().Contain("preparedPackage.TryEnsureCellPatchBaseline(workbook, out preparedPackage, out blockReason)");
        snapshotSource.Should().Contain("public bool TryEnsureCellPatchEligibility(");
        snapshotSource.Should().Contain("IsCellPatchEligibilityLazy = false");
        snapshotSource.Should().Contain("SourceHasCustomViews");
        snapshotSource.Should().Contain("if (sourceHasWorkbookCustomViews || workbook.CustomViews.Count > 0)");
        snapshotSource.Should().Contain("layout.CustomViews.Count > 0");
        snapshotSource.Should().Contain("SourceNeedsPackageGraphNormalization");
        snapshotSource.Should().Contain("if (SourceNeedsPackageGraphNormalization != false)");
        snapshotSource.Should().Contain("TryApplySimpleExistingCellChangesStreaming(");
        snapshotSource.Should().Contain("CanStreamSimpleExistingCellChanges(");
        snapshotSource.Should().Contain("XlsxCellValuePatchKind.LiteralValue or");
        snapshotSource.Should().Contain("XlsxCellValuePatchKind.FormulaCachedValue or");
        snapshotSource.Should().Contain("XlsxCellPatchBaselineFacts.Capture(workbook, sheetXmlLayout)");
        snapshotSource.Should().Contain("baselineFacts: CellPatchBaselineFacts");
        snapshotSource.Should().Contain("private const int CellPatchBaselineLimit = 2_000_000;");
        snapshotSource.Should().Contain("private const int CellPatchChangeLimit = 4_096;");
        snapshotSource.Should().Contain("retainedBaselineFacts.TryGetSheetFacts");
        snapshotSource.Should().Contain("retainedChartSourceRanges.Matches(workbook)");
        snapshotSource.Should().Contain("CellPatchBaselineFacts = null");
        snapshotSource.Should().Contain("XlsxPatchCellEntry[] Cells");
        snapshotSource.Should().Contain("baseline.TryGetCell(row, col, out var original)");
        snapshotSource.Should().Contain("baseline.WithAppliedCellChanges(sheetChanges ?? [])");
        snapshotSource.Should().Contain("Array.Sort(cells, XlsxPatchCellEntry.Compare)");
        snapshotSource.Should().NotContain("new Dictionary<(uint Row, uint Col), XlsxPatchCell>");
        snapshotSource.Should().Contain("sheet.TryGetCompressedStyleOnlyRuns(out var runs)");
        snapshotSource.Should().Contain("ReadCompressedSourceStyleOnlyCells(");
        snapshotSource.Should().Contain("XlsxSourceStyleOnlyCellCollection.FromRuns");
        snapshotSource.Should().Contain("XlsxSourceStyleOnlyRunEntry");
        snapshotSource.Should().Contain("StyleOnlyRunIsBeforeCell");
        snapshotSource.Should().Contain("IsCellPatchBaselineLazy: true");
        snapshotSource.Should().Contain("patch_blocked_deferred_baseline_not_materialized");
        snapshotSource.Should().Contain("patchedPackage.TryGetBuffer");
        snapshotSource.Should().Contain("mergeRegionChanges,");
        snapshotSource.Should().Contain("hyperlinkChanges,");
        snapshotSource.Should().Contain("var patchedSourceModelFingerprint = currentModelFingerprint;");
        snapshotSource.Should().Contain("out var currentPatchValidationModelFingerprint,");
        snapshotSource.Should().Contain("currentPatchValidationModelFingerprint ?? CreatePatchValidationModelFingerprint(workbook);");
        snapshotSource.Should().Contain("currentPatchValidationModelFingerprint = CreatePatchValidationModelFingerprint(workbook);");
        snapshotSource.Should().Contain("ChangesOnlyExistingCells(");
        snapshotSource.Should().Contain("HasWorksheetAutoFilterChanges(workbook)");
        snapshotSource.Should().Contain("CreateWorksheetAutoFilterFingerprint(workbook)");
        snapshotSource.Should().Contain("patchedSourceModelFingerprint,");
        snapshotSource.Should().Contain("patchedPatchValidationFingerprint,");
        snapshotSource.Should().Contain("CellPatchBaselineBlockReason,");
        snapshotSource.Should().Contain("SourceHasCustomViews: workbook.CustomViews.Count > 0,");
    }

    [Fact]
    public void SaveSourcePackagePreservation_ReusesLoadTimePlainWorksheetPreflight()
    {
        var adapterSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxFileAdapter.cs");
        var layoutSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxFileAdapter.SheetXmlLayout.cs");
        var sourcePackageSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxFileAdapter.SourcePackage.cs");
        var preserverSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxWorksheetMetadataPreserver.cs");

        adapterSource.Should().Contain("GetWorksheetsWithPreservableSourceMetadata(");
        layoutSource.Should().Contain("HasPreservableSourceWorksheetMetadata(worksheetXml, worksheetNs)");
        sourcePackageSource.Should().Contain("sourcePackage.WorksheetsWithPreservableSourceMetadata");
        preserverSource.Should().Contain("worksheetsWithPreservableSourceMetadata is not null");
        preserverSource.Should().Contain("!worksheetsWithPreservableSourceMetadata.Contains(sheetName)");
    }

    [Fact]
    public void SaveSourcePackageCompatibilityNormalization_SkipsUnneededWorksheetScans()
    {
        var postProcessingSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxFileAdapter.SavePostProcessing.cs");
        var normalizerSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxExcelCompatibilityNormalizer.cs");

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
        var source = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxWorkbookSchemaNormalizer.cs");

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
        PrepareLoadedWorkbookForEdit(workbook);

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
