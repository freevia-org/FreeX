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
    private static string FormatSaveDiagnostics(XlsxFileAdapter adapter)
    {
        var diagnostics = adapter.LastSaveDiagnostics;
        return $"save_path={diagnostics.PathLabel} save_reason={diagnostics.Reason} " +
               $"patch_changes={diagnostics.TotalPatchChangeCount} cell_changes={diagnostics.CellChangeCount} " +
               $"dimension_changes={diagnostics.DimensionChangeCount} merge_changes={diagnostics.MergeRegionChangeCount} " +
               $"hyperlink_changes={diagnostics.HyperlinkChangeCount} comment_changes={diagnostics.CommentChangeCount} " +
               $"worksheet_view_changes={diagnostics.WorksheetViewChangeCount}";
    }

    [BenchmarkFact]
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

    [BenchmarkFact]
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
    public void LoadDrawingPictures_TransfersOwnedImageBuffersWithoutSecondCopy()
    {
        var source = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxFileAdapter.LoadSheetXmlLayoutApplication.cs");
        var pictureLoop = source[
            source.IndexOf("foreach (var picturePart in layout.PictureParts)", StringComparison.Ordinal)..
            source.IndexOf("foreach (var textBoxPart in layout.TextBoxParts)", StringComparison.Ordinal)];

        pictureLoop.Should().Contain("ImageBytes = picturePart.ImageBytes,");
        pictureLoop.Should().NotContain("ImageBytes = picturePart.ImageBytes.ToArray()");

        var drawingPartsSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxWorksheetDrawingParts.cs");
        var readPictureParts = drawingPartsSource[
            drawingPartsSource.IndexOf("internal static IReadOnlyList<XlsxPicturePackagePart> ReadPictureParts", StringComparison.Ordinal)..
            drawingPartsSource.IndexOf("private static Dictionary<string, string> ReadRelationshipTargetsById", StringComparison.Ordinal)];

        readPictureParts.Should().Contain("ReadEntryBytes(imageEntry)");
        readPictureParts.Should().Contain("GC.AllocateUninitializedArray<byte>");
        readPictureParts.Should().Contain("ReadNonVisualProperties(pictureElement)");
        readPictureParts.Should().NotContain("ReadNonVisualName(pictureElement)");
        readPictureParts.Should().NotContain("ReadNonVisualTitle(pictureElement)");
        readPictureParts.Should().NotContain("ReadNonVisualDescription(pictureElement)");
    }

    [BenchmarkFact]
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

    [BenchmarkFact]
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

    [BenchmarkFact]
    public void Benchmark_SaveLoadedDenseWorkbook_ReportsTiming()
    {
        const int iterations = 3;
        var package = CreateDenseXlsxPackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var loadStream = new MemoryStream(package, writable: false))
        {
            workbook = adapter.Load(loadStream);
            PrepareLoadedWorkbookForEdit(workbook);
        }

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

    [BenchmarkFact]
    public void Benchmark_SaveLoadedGeneratedStyleHeavyWorkbook_ReportsTiming()
    {
        const int iterations = 5;
        var package = CreateGeneratedStyleHeavyXlsxPackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var loadStream = new MemoryStream(package, writable: false))
        {
            workbook = adapter.Load(loadStream);
            PrepareLoadedWorkbookForEdit(workbook);
        }

        AssertGeneratedStyleHeavyWorkbook(workbook);
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
            "PERF XLSX_SAVE_LOADED_GENERATED_STYLE_HEAVY " +
            $"sheets={GeneratedStyleHeavySheetCount} rows={GeneratedStyleHeavyRowsPerSheet} " +
            $"value_cols={GeneratedStyleHeavyValueColumnsPerSheet} style_only_cols={GeneratedStyleHeavyStyleOnlyColumnsPerSheet} " +
            $"style_only_cells={GeneratedStyleHeavySheetCount * GeneratedStyleHeavyRowsPerSheet * GeneratedStyleHeavyStyleOnlyColumnsPerSheet:N0} " +
            $"steps={iterations} source_bytes={package.Length:N0} package_bytes={packageSizes.Max():N0} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={timings.Average():F2} " +
            $"p95_ms={p95:F2} max_ms={ordered[^1]:F2} allocated_bytes={allocatedBytes:N0} " +
            FormatSaveDiagnostics(adapter));

        timings.Average().Should().BeGreaterThan(0);
        packageSizes.Should().OnlyContain(size => size > 0);
    }

    [BenchmarkFact]
    public void Benchmark_SaveLoadedLargeCellBaselineWorkbook_ReportsTiming()
    {
        var package = CreateLargePatchBaselineXlsxPackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var loadStream = new MemoryStream(package, writable: false))
            workbook = adapter.Load(loadStream);

        workbook.SheetCount.Should().Be(1);
        workbook.Sheets[0].CellCount.Should().Be(LargePatchBaselineRows);

        var prepareAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var prepare = Stopwatch.StartNew();
        var prepared = XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var prepareBlockReason);
        prepare.Stop();
        var prepareAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - prepareAllocatedBefore;

        var sheet = workbook.Sheets[0];
        sheet.SetCell(new CellAddress(sheet.Id, (uint)LargePatchBaselineRows, 1), new NumberValue(42));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        using var stream = new MemoryStream();
        var saveAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var save = Stopwatch.StartNew();
        adapter.Save(workbook, stream);
        save.Stop();
        var saveAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - saveAllocatedBefore;

        Console.WriteLine(
            "PERF XLSX_SAVE_LOADED_LARGE_CELL_BASELINE " +
            $"rows={LargePatchBaselineRows:N0} source_bytes={package.Length:N0} package_bytes={stream.Length:N0} " +
            $"prepared={prepared.ToString().ToLowerInvariant()} prepare_reason=\"{prepareBlockReason ?? ""}\" " +
            $"prepare_ms={prepare.Elapsed.TotalMilliseconds:F2} prepare_allocated_bytes={prepareAllocatedBytes:N0} " +
            $"save_ms={save.Elapsed.TotalMilliseconds:F2} save_allocated_bytes={saveAllocatedBytes:N0} " +
            FormatSaveDiagnostics(adapter));

        prepared.Should().BeTrue(prepareBlockReason);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        adapter.LastSaveDiagnostics.CellChangeCount.Should().Be(1);
        stream.Length.Should().BeGreaterThan(0);
    }

    [BenchmarkFact]
    public void Benchmark_SaveLoadedGeneratedStyleHeavyWorksheetViewWorkbook_ReportsTiming()
    {
        const int iterations = 5;
        var package = CreateGeneratedStyleHeavyXlsxPackage();
        var adapter = new XlsxFileAdapter();
        var workbooks = new List<Workbook>(iterations + 1);
        for (var i = 0; i <= iterations; i++)
        {
            using var loadStream = new MemoryStream(package, writable: false);
            var workbook = adapter.Load(loadStream);
            PrepareLoadedWorkbookForEdit(workbook);
            AssertGeneratedStyleHeavyWorkbook(workbook);

            var sheet = workbook.Sheets[0];
            sheet.ShowGridlines = false;
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(200 + i));
            workbooks.Add(workbook);
        }

        using (var warmup = new MemoryStream())
            adapter.Save(workbooks[0], warmup);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");

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
            adapter.Save(workbooks[i + 1], stream);
            step.Stop();
            timings.Add(step.Elapsed.TotalMilliseconds);
            packageSizes.Add(stream.Length);
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var ordered = timings.OrderBy(value => value).ToArray();
        var p95 = ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)];

        Console.WriteLine(
            "PERF XLSX_SAVE_LOADED_GENERATED_STYLE_HEAVY_WORKSHEET_VIEW " +
            $"sheets={GeneratedStyleHeavySheetCount} rows={GeneratedStyleHeavyRowsPerSheet} " +
            $"value_cols={GeneratedStyleHeavyValueColumnsPerSheet} style_only_cols={GeneratedStyleHeavyStyleOnlyColumnsPerSheet} " +
            $"style_only_cells={GeneratedStyleHeavySheetCount * GeneratedStyleHeavyRowsPerSheet * GeneratedStyleHeavyStyleOnlyColumnsPerSheet:N0} " +
            $"steps={iterations} source_bytes={package.Length:N0} package_bytes={packageSizes.Max():N0} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={timings.Average():F2} " +
            $"p95_ms={p95:F2} max_ms={ordered[^1]:F2} allocated_bytes={allocatedBytes:N0} " +
            FormatSaveDiagnostics(adapter));

        timings.Average().Should().BeGreaterThan(0);
        packageSizes.Should().OnlyContain(size => size > 0);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        adapter.LastSaveDiagnostics.CellChangeCount.Should().Be(1);
        adapter.LastSaveDiagnostics.WorksheetViewChangeCount.Should().Be(1);
    }

    [BenchmarkFact]
    public void Benchmark_SaveLoadedGeneratedStyleHeavyHeaderFooterVmlWorkbook_ReportsTiming()
    {
        const int iterations = 5;
        var package = AddGeneratedStyleHeavyHeaderFooterLegacyDrawingPackage(CreateGeneratedStyleHeavyXlsxPackage());
        var adapter = new XlsxFileAdapter();
        var workbooks = new List<Workbook>(iterations + 1);
        for (var i = 0; i <= iterations; i++)
        {
            using var loadStream = new MemoryStream(package, writable: false);
            var workbook = adapter.Load(loadStream);
            PrepareLoadedWorkbookForEdit(workbook);
            AssertGeneratedStyleHeavyWorkbook(workbook);

            var sheet = workbook.Sheets[0];
            sheet.PageHeaderPictures.Left.Should().NotBeNull();
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(300 + i));
            workbooks.Add(workbook);
        }

        using (var warmup = new MemoryStream())
            adapter.Save(workbooks[0], warmup);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");

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
            adapter.Save(workbooks[i + 1], stream);
            step.Stop();
            timings.Add(step.Elapsed.TotalMilliseconds);
            packageSizes.Add(stream.Length);
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var ordered = timings.OrderBy(value => value).ToArray();
        var p95 = ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)];

        Console.WriteLine(
            "PERF XLSX_SAVE_LOADED_GENERATED_STYLE_HEAVY_HEADER_FOOTER_VML " +
            $"sheets={GeneratedStyleHeavySheetCount} rows={GeneratedStyleHeavyRowsPerSheet} " +
            $"value_cols={GeneratedStyleHeavyValueColumnsPerSheet} style_only_cols={GeneratedStyleHeavyStyleOnlyColumnsPerSheet} " +
            $"style_only_cells={GeneratedStyleHeavySheetCount * GeneratedStyleHeavyRowsPerSheet * GeneratedStyleHeavyStyleOnlyColumnsPerSheet:N0} " +
            $"steps={iterations} source_bytes={package.Length:N0} package_bytes={packageSizes.Max():N0} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={timings.Average():F2} " +
            $"p95_ms={p95:F2} max_ms={ordered[^1]:F2} allocated_bytes={allocatedBytes:N0} " +
            FormatSaveDiagnostics(adapter));

        timings.Average().Should().BeGreaterThan(0);
        packageSizes.Should().OnlyContain(size => size > 0);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        adapter.LastSaveDiagnostics.CellChangeCount.Should().Be(1);
    }

    [BenchmarkFact]
    public void Benchmark_SaveLoadedGeneratedStyleHeavyDrawingShapesWorkbook_ReportsTiming()
    {
        const int iterations = 5;
        var package = AddGeneratedStyleHeavyDrawingShapePackage(CreateGeneratedStyleHeavyXlsxPackage());
        var adapter = new XlsxFileAdapter();
        var workbooks = new List<Workbook>(iterations + 1);
        for (var i = 0; i <= iterations; i++)
        {
            using var loadStream = new MemoryStream(package, writable: false);
            var workbook = adapter.Load(loadStream);
            PrepareLoadedWorkbookForEdit(workbook);
            AssertGeneratedStyleHeavyWorkbook(workbook);

            var sheet = workbook.Sheets[0];
            sheet.TextBoxes.Should().HaveCount(GeneratedStyleHeavyDrawingShapePairs);
            sheet.DrawingShapes.Should().HaveCount(GeneratedStyleHeavyDrawingShapePairs);
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(400 + i));
            workbooks.Add(workbook);
        }

        using (var warmup = new MemoryStream())
            adapter.Save(workbooks[0], warmup);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");

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
            adapter.Save(workbooks[i + 1], stream);
            step.Stop();
            timings.Add(step.Elapsed.TotalMilliseconds);
            packageSizes.Add(stream.Length);
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var ordered = timings.OrderBy(value => value).ToArray();
        var p95 = ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)];

        Console.WriteLine(
            "PERF XLSX_SAVE_LOADED_GENERATED_STYLE_HEAVY_DRAWING_SHAPES " +
            $"sheets={GeneratedStyleHeavySheetCount} rows={GeneratedStyleHeavyRowsPerSheet} " +
            $"value_cols={GeneratedStyleHeavyValueColumnsPerSheet} style_only_cols={GeneratedStyleHeavyStyleOnlyColumnsPerSheet} " +
            $"style_only_cells={GeneratedStyleHeavySheetCount * GeneratedStyleHeavyRowsPerSheet * GeneratedStyleHeavyStyleOnlyColumnsPerSheet:N0} " +
            $"text_boxes={GeneratedStyleHeavyDrawingShapePairs} shapes={GeneratedStyleHeavyDrawingShapePairs} " +
            $"steps={iterations} source_bytes={package.Length:N0} package_bytes={packageSizes.Max():N0} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={timings.Average():F2} " +
            $"p95_ms={p95:F2} max_ms={ordered[^1]:F2} allocated_bytes={allocatedBytes:N0} " +
            FormatSaveDiagnostics(adapter));

        timings.Average().Should().BeGreaterThan(0);
        packageSizes.Should().OnlyContain(size => size > 0);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        adapter.LastSaveDiagnostics.CellChangeCount.Should().Be(1);
    }

    [BenchmarkFact]
    public void Benchmark_SaveLoadedGeneratedStyleHeavyChartExWorkbook_ReportsTiming()
    {
        const int iterations = 5;
        var package = AddGeneratedStyleHeavyChartExPackage(CreateGeneratedStyleHeavyXlsxPackage());
        var adapter = new XlsxFileAdapter();
        var workbooks = new List<Workbook>(iterations + 1);
        for (var i = 0; i <= iterations; i++)
        {
            using var loadStream = new MemoryStream(package, writable: false);
            var workbook = adapter.Load(loadStream);
            PrepareLoadedWorkbookForEdit(workbook);
            AssertGeneratedStyleHeavyWorkbook(workbook);

            var sheet = workbook.Sheets[0];
            sheet.Charts.Should().ContainSingle().Which.Type.Should().Be(ChartType.Histogram);
            sheet.SetCell(
                new CellAddress(sheet.Id, 1, GeneratedStyleHeavyValueColumnsPerSheet),
                new NumberValue(500 + i));
            workbooks.Add(workbook);
        }

        using (var warmup = new MemoryStream())
            adapter.Save(workbooks[0], warmup);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");

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
            adapter.Save(workbooks[i + 1], stream);
            step.Stop();
            timings.Add(step.Elapsed.TotalMilliseconds);
            packageSizes.Add(stream.Length);
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var ordered = timings.OrderBy(value => value).ToArray();
        var p95 = ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)];

        Console.WriteLine(
            "PERF XLSX_SAVE_LOADED_GENERATED_STYLE_HEAVY_CHARTEX " +
            $"sheets={GeneratedStyleHeavySheetCount} rows={GeneratedStyleHeavyRowsPerSheet} " +
            $"value_cols={GeneratedStyleHeavyValueColumnsPerSheet} style_only_cols={GeneratedStyleHeavyStyleOnlyColumnsPerSheet} " +
            $"style_only_cells={GeneratedStyleHeavySheetCount * GeneratedStyleHeavyRowsPerSheet * GeneratedStyleHeavyStyleOnlyColumnsPerSheet:N0} " +
            $"chart_ex_source_rows={GeneratedStyleHeavyChartExSourceRows} steps={iterations} " +
            $"source_bytes={package.Length:N0} package_bytes={packageSizes.Max():N0} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={timings.Average():F2} " +
            $"p95_ms={p95:F2} max_ms={ordered[^1]:F2} allocated_bytes={allocatedBytes:N0} " +
            FormatSaveDiagnostics(adapter));

        timings.Average().Should().BeGreaterThan(0);
        packageSizes.Should().OnlyContain(size => size > 0);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        adapter.LastSaveDiagnostics.CellChangeCount.Should().Be(1);
    }

    [BenchmarkFact]
    public void Benchmark_SaveLoadedGeneratedStyleHeavyPivotChartWorkbook_ReportsTiming()
    {
        const int iterations = 5;
        var package = AddGeneratedStyleHeavyPivotChartPackage(CreateGeneratedStyleHeavyXlsxPackage());
        var adapter = new XlsxFileAdapter();
        var workbooks = new List<Workbook>(iterations + 1);
        for (var i = 0; i <= iterations; i++)
        {
            using var loadStream = new MemoryStream(package, writable: false);
            var workbook = adapter.Load(loadStream);
            PrepareLoadedWorkbookForEdit(workbook);
            AssertGeneratedStyleHeavyWorkbook(workbook);

            var sheet = workbook.Sheets[0];
            sheet.PivotTables.Should().ContainSingle();
            sheet.Charts.Should().ContainSingle().Which.IsPivotChart.Should().BeTrue();
            sheet.SetCell(
                new CellAddress(sheet.Id, 1, GeneratedStyleHeavyValueColumnsPerSheet),
                new NumberValue(600 + i));
            workbooks.Add(workbook);
        }

        using (var warmup = new MemoryStream())
            adapter.Save(workbooks[0], warmup);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");

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
            adapter.Save(workbooks[i + 1], stream);
            step.Stop();
            timings.Add(step.Elapsed.TotalMilliseconds);
            packageSizes.Add(stream.Length);
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var ordered = timings.OrderBy(value => value).ToArray();
        var p95 = ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)];

        Console.WriteLine(
            "PERF XLSX_SAVE_LOADED_GENERATED_STYLE_HEAVY_PIVOT_CHART " +
            $"sheets={GeneratedStyleHeavySheetCount} rows={GeneratedStyleHeavyRowsPerSheet} " +
            $"value_cols={GeneratedStyleHeavyValueColumnsPerSheet} style_only_cols={GeneratedStyleHeavyStyleOnlyColumnsPerSheet} " +
            $"style_only_cells={GeneratedStyleHeavySheetCount * GeneratedStyleHeavyRowsPerSheet * GeneratedStyleHeavyStyleOnlyColumnsPerSheet:N0} " +
            $"pivot_chart_source_rows={GeneratedStyleHeavyPivotChartSourceRows} steps={iterations} " +
            $"source_bytes={package.Length:N0} package_bytes={packageSizes.Max():N0} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={timings.Average():F2} " +
            $"p95_ms={p95:F2} max_ms={ordered[^1]:F2} allocated_bytes={allocatedBytes:N0} " +
            FormatSaveDiagnostics(adapter));

        timings.Average().Should().BeGreaterThan(0);
        packageSizes.Should().OnlyContain(size => size > 0);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        adapter.LastSaveDiagnostics.CellChangeCount.Should().Be(1);
    }

    [BenchmarkFact]
    public void Benchmark_SaveLoadedGeneratedStyleHeavyExistingStyleWorkbook_ReportsTiming()
    {
        const int iterations = 5;
        var package = CreateGeneratedStyleHeavyXlsxPackage();
        var adapter = new XlsxFileAdapter();
        var workbooks = new List<Workbook>(iterations + 1);
        for (var i = 0; i <= iterations; i++)
        {
            using var loadStream = new MemoryStream(package, writable: false);
            var workbook = adapter.Load(loadStream);
            PrepareLoadedWorkbookForEdit(workbook);
            AssertGeneratedStyleHeavyWorkbook(workbook);
            ApplyGeneratedStyleHeavyExistingStyleMutation(workbook);
            workbooks.Add(workbook);
        }

        using (var warmup = new MemoryStream())
            adapter.Save(workbooks[0], warmup);

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
            adapter.Save(workbooks[i + 1], stream);
            step.Stop();
            timings.Add(step.Elapsed.TotalMilliseconds);
            packageSizes.Add(stream.Length);
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var ordered = timings.OrderBy(value => value).ToArray();
        var p95 = ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)];

        Console.WriteLine(
            "PERF XLSX_SAVE_LOADED_GENERATED_STYLE_HEAVY_EXISTING_STYLE " +
            $"sheets={GeneratedStyleHeavySheetCount} rows={GeneratedStyleHeavyRowsPerSheet} " +
            $"value_cols={GeneratedStyleHeavyValueColumnsPerSheet} style_only_cols={GeneratedStyleHeavyStyleOnlyColumnsPerSheet} " +
            $"style_only_cells={GeneratedStyleHeavySheetCount * GeneratedStyleHeavyRowsPerSheet * GeneratedStyleHeavyStyleOnlyColumnsPerSheet:N0} " +
            $"steps={iterations} source_bytes={package.Length:N0} package_bytes={packageSizes.Max():N0} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={timings.Average():F2} " +
            $"p95_ms={p95:F2} max_ms={ordered[^1]:F2} allocated_bytes={allocatedBytes:N0} " +
            FormatSaveDiagnostics(adapter));

        timings.Average().Should().BeGreaterThan(0);
        packageSizes.Should().OnlyContain(size => size > 0);
    }

    [BenchmarkFact]
    public void Benchmark_SaveLoadedGeneratedStyleHeavyDimensionsWorkbook_ReportsTiming()
    {
        const int iterations = 5;
        var package = CreateGeneratedStyleHeavyXlsxPackage();
        var adapter = new XlsxFileAdapter();
        var workbooks = new List<Workbook>(iterations + 1);
        for (var i = 0; i <= iterations; i++)
        {
            using var loadStream = new MemoryStream(package, writable: false);
            var workbook = adapter.Load(loadStream);
            PrepareLoadedWorkbookForEdit(workbook);
            AssertGeneratedStyleHeavyWorkbook(workbook);
            ApplyGeneratedStyleHeavyDimensionMutation(workbook, i);
            workbooks.Add(workbook);
        }

        using (var warmup = new MemoryStream())
            adapter.Save(workbooks[0], warmup);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");

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
            adapter.Save(workbooks[i + 1], stream);
            step.Stop();
            timings.Add(step.Elapsed.TotalMilliseconds);
            packageSizes.Add(stream.Length);
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var ordered = timings.OrderBy(value => value).ToArray();
        var p95 = ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)];

        Console.WriteLine(
            "PERF XLSX_SAVE_LOADED_GENERATED_STYLE_HEAVY_DIMENSIONS " +
            $"sheets={GeneratedStyleHeavySheetCount} rows={GeneratedStyleHeavyRowsPerSheet} " +
            $"value_cols={GeneratedStyleHeavyValueColumnsPerSheet} style_only_cols={GeneratedStyleHeavyStyleOnlyColumnsPerSheet} " +
            $"style_only_cells={GeneratedStyleHeavySheetCount * GeneratedStyleHeavyRowsPerSheet * GeneratedStyleHeavyStyleOnlyColumnsPerSheet:N0} " +
            $"steps={iterations} source_bytes={package.Length:N0} package_bytes={packageSizes.Max():N0} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={timings.Average():F2} " +
            $"p95_ms={p95:F2} max_ms={ordered[^1]:F2} allocated_bytes={allocatedBytes:N0}");

        timings.Average().Should().BeGreaterThan(0);
        packageSizes.Should().OnlyContain(size => size > 0);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
    }

    [BenchmarkFact]
    public void Benchmark_SaveLoadedGeneratedStyleHeavyMergedRegionsWorkbook_ReportsTiming()
    {
        const int iterations = 5;
        var package = CreateGeneratedStyleHeavyXlsxPackage();
        var adapter = new XlsxFileAdapter();
        var workbooks = new List<Workbook>(iterations + 1);
        for (var i = 0; i <= iterations; i++)
        {
            using var loadStream = new MemoryStream(package, writable: false);
            var workbook = adapter.Load(loadStream);
            PrepareLoadedWorkbookForEdit(workbook);
            AssertGeneratedStyleHeavyWorkbook(workbook);
            ApplyGeneratedStyleHeavyMergeRegionMutation(workbook, i);
            workbooks.Add(workbook);
        }

        using (var warmup = new MemoryStream())
            adapter.Save(workbooks[0], warmup);

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
            adapter.Save(workbooks[i + 1], stream);
            step.Stop();
            timings.Add(step.Elapsed.TotalMilliseconds);
            packageSizes.Add(stream.Length);
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var ordered = timings.OrderBy(value => value).ToArray();
        var p95 = ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)];

        Console.WriteLine(
            "PERF XLSX_SAVE_LOADED_GENERATED_STYLE_HEAVY_MERGED_REGIONS " +
            $"sheets={GeneratedStyleHeavySheetCount} rows={GeneratedStyleHeavyRowsPerSheet} " +
            $"value_cols={GeneratedStyleHeavyValueColumnsPerSheet} style_only_cols={GeneratedStyleHeavyStyleOnlyColumnsPerSheet} " +
            $"style_only_cells={GeneratedStyleHeavySheetCount * GeneratedStyleHeavyRowsPerSheet * GeneratedStyleHeavyStyleOnlyColumnsPerSheet:N0} " +
            $"steps={iterations} source_bytes={package.Length:N0} package_bytes={packageSizes.Max():N0} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={timings.Average():F2} " +
            $"p95_ms={p95:F2} max_ms={ordered[^1]:F2} allocated_bytes={allocatedBytes:N0}");

        timings.Average().Should().BeGreaterThan(0);
        packageSizes.Should().OnlyContain(size => size > 0);
    }

    [BenchmarkFact]
    public void Benchmark_SaveLoadedGeneratedStyleHeavyInternalHyperlinkWorkbook_ReportsTiming()
    {
        const int iterations = 5;
        var package = CreateGeneratedStyleHeavyXlsxPackage(internalHyperlinkMarker: true);
        var adapter = new XlsxFileAdapter();
        var workbooks = new List<Workbook>(iterations + 1);
        for (var i = 0; i <= iterations; i++)
        {
            using var loadStream = new MemoryStream(package, writable: false);
            var workbook = adapter.Load(loadStream);
            PrepareLoadedWorkbookForEdit(workbook);
            AssertGeneratedStyleHeavyWorkbook(workbook);
            ApplyGeneratedStyleHeavyInternalHyperlinkMutation(workbook, i);
            workbooks.Add(workbook);
        }

        using (var warmup = new MemoryStream())
            adapter.Save(workbooks[0], warmup);

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
            adapter.Save(workbooks[i + 1], stream);
            step.Stop();
            timings.Add(step.Elapsed.TotalMilliseconds);
            packageSizes.Add(stream.Length);
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var ordered = timings.OrderBy(value => value).ToArray();
        var p95 = ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)];

        Console.WriteLine(
            "PERF XLSX_SAVE_LOADED_GENERATED_STYLE_HEAVY_INTERNAL_HYPERLINK " +
            $"sheets={GeneratedStyleHeavySheetCount} rows={GeneratedStyleHeavyRowsPerSheet} " +
            $"value_cols={GeneratedStyleHeavyValueColumnsPerSheet} style_only_cols={GeneratedStyleHeavyStyleOnlyColumnsPerSheet} " +
            $"style_only_cells={GeneratedStyleHeavySheetCount * GeneratedStyleHeavyRowsPerSheet * GeneratedStyleHeavyStyleOnlyColumnsPerSheet:N0} " +
            $"steps={iterations} source_bytes={package.Length:N0} package_bytes={packageSizes.Max():N0} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={timings.Average():F2} " +
            $"p95_ms={p95:F2} max_ms={ordered[^1]:F2} allocated_bytes={allocatedBytes:N0}");

        timings.Average().Should().BeGreaterThan(0);
        packageSizes.Should().OnlyContain(size => size > 0);
    }

    [BenchmarkFact]
    public void Benchmark_SaveLoadedGeneratedStyleHeavyLegacyCommentWorkbook_ReportsTiming()
    {
        const int iterations = 5;
        var package = CreateGeneratedStyleHeavyXlsxPackage(legacyCommentMarker: true);
        var adapter = new XlsxFileAdapter();
        var workbooks = new List<Workbook>(iterations + 1);
        for (var i = 0; i <= iterations; i++)
        {
            using var loadStream = new MemoryStream(package, writable: false);
            var workbook = adapter.Load(loadStream);
            PrepareLoadedWorkbookForEdit(workbook);
            AssertGeneratedStyleHeavyWorkbook(workbook);
            workbook.Sheets[0].Comments[new CellAddress(workbook.Sheets[0].Id, 1, 1)]
                .Should()
                .Be("Comment 0");
            ApplyGeneratedStyleHeavyLegacyCommentMutation(workbook, i);
            workbooks.Add(workbook);
        }

        using (var warmup = new MemoryStream())
            adapter.Save(workbooks[0], warmup);

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
            adapter.Save(workbooks[i + 1], stream);
            step.Stop();
            timings.Add(step.Elapsed.TotalMilliseconds);
            packageSizes.Add(stream.Length);
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var ordered = timings.OrderBy(value => value).ToArray();
        var p95 = ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)];

        Console.WriteLine(
            "PERF XLSX_SAVE_LOADED_GENERATED_STYLE_HEAVY_LEGACY_COMMENT " +
            $"sheets={GeneratedStyleHeavySheetCount} rows={GeneratedStyleHeavyRowsPerSheet} " +
            $"value_cols={GeneratedStyleHeavyValueColumnsPerSheet} style_only_cols={GeneratedStyleHeavyStyleOnlyColumnsPerSheet} " +
            $"style_only_cells={GeneratedStyleHeavySheetCount * GeneratedStyleHeavyRowsPerSheet * GeneratedStyleHeavyStyleOnlyColumnsPerSheet:N0} " +
            $"steps={iterations} source_bytes={package.Length:N0} package_bytes={packageSizes.Max():N0} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={timings.Average():F2} " +
            $"p95_ms={p95:F2} max_ms={ordered[^1]:F2} allocated_bytes={allocatedBytes:N0}");

        timings.Average().Should().BeGreaterThan(0);
        packageSizes.Should().OnlyContain(size => size > 0);
    }

    [BenchmarkFact]
    public void Benchmark_SaveLoadedGeneratedStyleHeavyStructuredTableWorkbook_ReportsTiming()
        => RunGeneratedStyleHeavyStructuredTableSaveBenchmark(
            filteredStructuredTable: false,
            "XLSX_SAVE_LOADED_GENERATED_STYLE_HEAVY_STRUCTURED_TABLE");

    [BenchmarkFact]
    public void Benchmark_SaveLoadedGeneratedStyleHeavyFilteredStructuredTableWorkbook_ReportsTiming()
        => RunGeneratedStyleHeavyStructuredTableSaveBenchmark(
            filteredStructuredTable: true,
            "XLSX_SAVE_LOADED_GENERATED_STYLE_HEAVY_FILTERED_STRUCTURED_TABLE");

    private static void RunGeneratedStyleHeavyStructuredTableSaveBenchmark(
        bool filteredStructuredTable,
        string label)
    {
        const int iterations = 5;
        var package = CreateGeneratedStyleHeavyXlsxPackage(
            structuredTableMarker: !filteredStructuredTable,
            filteredStructuredTableMarker: filteredStructuredTable);
        var adapter = new XlsxFileAdapter();
        var workbooks = new List<Workbook>(iterations + 1);
        for (var i = 0; i <= iterations; i++)
        {
            using var loadStream = new MemoryStream(package, writable: false);
            var workbook = adapter.Load(loadStream);
            PrepareLoadedWorkbookForEdit(workbook);
            AssertGeneratedStyleHeavyWorkbook(workbook);
            workbook.Sheets[0].StructuredTables.Should().ContainSingle();
            ApplyGeneratedStyleHeavyStructuredTableMutation(workbook, i);
            workbooks.Add(workbook);
        }

        using (var warmup = new MemoryStream())
            adapter.Save(workbooks[0], warmup);

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
            adapter.Save(workbooks[i + 1], stream);
            step.Stop();
            timings.Add(step.Elapsed.TotalMilliseconds);
            packageSizes.Add(stream.Length);
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var ordered = timings.OrderBy(value => value).ToArray();
        var p95 = ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)];

        Console.WriteLine(
            $"PERF {label} " +
            $"sheets={GeneratedStyleHeavySheetCount} rows={GeneratedStyleHeavyRowsPerSheet} " +
            $"value_cols={GeneratedStyleHeavyValueColumnsPerSheet} style_only_cols={GeneratedStyleHeavyStyleOnlyColumnsPerSheet} " +
            $"style_only_cells={GeneratedStyleHeavySheetCount * GeneratedStyleHeavyRowsPerSheet * GeneratedStyleHeavyStyleOnlyColumnsPerSheet:N0} " +
            $"steps={iterations} source_bytes={package.Length:N0} package_bytes={packageSizes.Max():N0} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={timings.Average():F2} " +
            $"p95_ms={p95:F2} max_ms={ordered[^1]:F2} allocated_bytes={allocatedBytes:N0} " +
            FormatSaveDiagnostics(adapter));

        timings.Average().Should().BeGreaterThan(0);
        packageSizes.Should().OnlyContain(size => size > 0);
    }

    [BenchmarkFact]
    public void Benchmark_SaveLoadedGeneratedStyleHeavySparklineWorkbook_ReportsTiming()
    {
        const int iterations = 5;
        var package = CreateGeneratedStyleHeavyXlsxPackage(sparklineMarker: true);
        var adapter = new XlsxFileAdapter();
        var workbooks = new List<Workbook>(iterations + 1);
        for (var i = 0; i <= iterations; i++)
        {
            using var loadStream = new MemoryStream(package, writable: false);
            var workbook = adapter.Load(loadStream);
            PrepareLoadedWorkbookForEdit(workbook);
            AssertGeneratedStyleHeavyWorkbook(workbook);
            workbook.Sheets[0].Sparklines.Should().ContainSingle();
            ApplyGeneratedStyleHeavySparklineMutation(workbook, i);
            workbooks.Add(workbook);
        }

        using (var warmup = new MemoryStream())
            adapter.Save(workbooks[0], warmup);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");

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
            adapter.Save(workbooks[i + 1], stream);
            step.Stop();
            timings.Add(step.Elapsed.TotalMilliseconds);
            packageSizes.Add(stream.Length);
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var ordered = timings.OrderBy(value => value).ToArray();
        var p95 = ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)];

        Console.WriteLine(
            "PERF XLSX_SAVE_LOADED_GENERATED_STYLE_HEAVY_SPARKLINE " +
            $"sheets={GeneratedStyleHeavySheetCount} rows={GeneratedStyleHeavyRowsPerSheet} " +
            $"value_cols={GeneratedStyleHeavyValueColumnsPerSheet} style_only_cols={GeneratedStyleHeavyStyleOnlyColumnsPerSheet} " +
            $"style_only_cells={GeneratedStyleHeavySheetCount * GeneratedStyleHeavyRowsPerSheet * GeneratedStyleHeavyStyleOnlyColumnsPerSheet:N0} " +
            $"steps={iterations} source_bytes={package.Length:N0} package_bytes={packageSizes.Max():N0} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={timings.Average():F2} " +
            $"p95_ms={p95:F2} max_ms={ordered[^1]:F2} allocated_bytes={allocatedBytes:N0} " +
            FormatSaveDiagnostics(adapter));

        timings.Average().Should().BeGreaterThan(0);
        packageSizes.Should().OnlyContain(size => size > 0);
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
    }

    [BenchmarkFact]
    public void Benchmark_SaveLoadedGeneratedStyleHeavyFormulaCacheWorkbook_ReportsTiming()
    {
        const int iterations = 5;
        var package = CreateGeneratedStyleHeavyXlsxPackage(formulaMarker: true);
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var loadStream = new MemoryStream(package, writable: false))
        {
            workbook = adapter.Load(loadStream);
            PrepareLoadedWorkbookForEdit(workbook);
        }

        AssertGeneratedStyleHeavyWorkbook(workbook);
        var markerCell = workbook.Sheets[0].GetCell(1, 1);
        markerCell.Should().NotBeNull();
        markerCell!.FormulaText.Should().Be("1+1");
        markerCell.Value = new NumberValue(42);
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
            markerCell.Value = new NumberValue(100 + i);
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
            "PERF XLSX_SAVE_LOADED_GENERATED_STYLE_HEAVY_FORMULA_CACHE " +
            $"sheets={GeneratedStyleHeavySheetCount} rows={GeneratedStyleHeavyRowsPerSheet} " +
            $"value_cols={GeneratedStyleHeavyValueColumnsPerSheet} style_only_cols={GeneratedStyleHeavyStyleOnlyColumnsPerSheet} " +
            $"style_only_cells={GeneratedStyleHeavySheetCount * GeneratedStyleHeavyRowsPerSheet * GeneratedStyleHeavyStyleOnlyColumnsPerSheet:N0} " +
            $"steps={iterations} source_bytes={package.Length:N0} package_bytes={packageSizes.Max():N0} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={timings.Average():F2} " +
            $"p95_ms={p95:F2} max_ms={ordered[^1]:F2} allocated_bytes={allocatedBytes:N0}");

        timings.Average().Should().BeGreaterThan(0);
        packageSizes.Should().OnlyContain(size => size > 0);
    }

    [BenchmarkFact]
    public void Benchmark_SaveLoadedGeneratedStyleHeavyFormulaTextWorkbook_ReportsTiming()
    {
        const int iterations = 5;
        var package = CreateGeneratedStyleHeavyXlsxPackage(formulaMarker: true);
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var loadStream = new MemoryStream(package, writable: false))
        {
            workbook = adapter.Load(loadStream);
            PrepareLoadedWorkbookForEdit(workbook);
        }

        AssertGeneratedStyleHeavyWorkbook(workbook);
        var markerCell = workbook.Sheets[0].GetCell(1, 1);
        markerCell.Should().NotBeNull();
        markerCell!.FormulaText.Should().Be("1+1");
        markerCell.FormulaText = "1+2";
        markerCell.Value = new NumberValue(3);
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
            markerCell.FormulaText = string.Create(CultureInfo.InvariantCulture, $"1+{3 + i}");
            markerCell.Value = new NumberValue(4 + i);
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
            "PERF XLSX_SAVE_LOADED_GENERATED_STYLE_HEAVY_FORMULA_TEXT " +
            $"sheets={GeneratedStyleHeavySheetCount} rows={GeneratedStyleHeavyRowsPerSheet} " +
            $"value_cols={GeneratedStyleHeavyValueColumnsPerSheet} style_only_cols={GeneratedStyleHeavyStyleOnlyColumnsPerSheet} " +
            $"style_only_cells={GeneratedStyleHeavySheetCount * GeneratedStyleHeavyRowsPerSheet * GeneratedStyleHeavyStyleOnlyColumnsPerSheet:N0} " +
            $"steps={iterations} source_bytes={package.Length:N0} package_bytes={packageSizes.Max():N0} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={timings.Average():F2} " +
            $"p95_ms={p95:F2} max_ms={ordered[^1]:F2} allocated_bytes={allocatedBytes:N0}");

        timings.Average().Should().BeGreaterThan(0);
        packageSizes.Should().OnlyContain(size => size > 0);
    }

    [BenchmarkFact]
    public void Benchmark_SaveLoadedGeneratedStyleHeavyNewCellWorkbook_ReportsTiming()
    {
        const int iterations = 5;
        var package = CreateGeneratedStyleHeavyXlsxPackage();
        var adapter = new XlsxFileAdapter();
        var workbooks = new List<Workbook>(iterations + 1);
        for (var i = 0; i <= iterations; i++)
        {
            using var loadStream = new MemoryStream(package, writable: false);
            var workbook = adapter.Load(loadStream);
            PrepareLoadedWorkbookForEdit(workbook);
            AssertGeneratedStyleHeavyWorkbook(workbook);
            var sheet = workbook.Sheets[0];
            sheet.SetCell(
                new CellAddress(sheet.Id, (uint)(i + 1), GeneratedStyleHeavyValueColumnsPerSheet + 1),
                new TextValue(string.Create(CultureInfo.InvariantCulture, $"new-{i}")));
            workbooks.Add(workbook);
        }

        using (var warmup = new MemoryStream())
            adapter.Save(workbooks[0], warmup);

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
            adapter.Save(workbooks[i + 1], stream);
            step.Stop();
            timings.Add(step.Elapsed.TotalMilliseconds);
            packageSizes.Add(stream.Length);
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var ordered = timings.OrderBy(value => value).ToArray();
        var p95 = ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)];

        Console.WriteLine(
            "PERF XLSX_SAVE_LOADED_GENERATED_STYLE_HEAVY_NEW_CELL " +
            $"sheets={GeneratedStyleHeavySheetCount} rows={GeneratedStyleHeavyRowsPerSheet} " +
            $"value_cols={GeneratedStyleHeavyValueColumnsPerSheet} style_only_cols={GeneratedStyleHeavyStyleOnlyColumnsPerSheet} " +
            $"style_only_cells={GeneratedStyleHeavySheetCount * GeneratedStyleHeavyRowsPerSheet * GeneratedStyleHeavyStyleOnlyColumnsPerSheet:N0} " +
            $"steps={iterations} source_bytes={package.Length:N0} package_bytes={packageSizes.Max():N0} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={timings.Average():F2} " +
            $"p95_ms={p95:F2} max_ms={ordered[^1]:F2} allocated_bytes={allocatedBytes:N0}");

        timings.Average().Should().BeGreaterThan(0);
        packageSizes.Should().OnlyContain(size => size > 0);
    }

    [BenchmarkFact]
    public void Benchmark_SaveLoadedGeneratedStyleHeavyClearedCellWorkbook_ReportsTiming()
    {
        const int iterations = 5;
        var package = CreateGeneratedStyleHeavyXlsxPackage();
        var adapter = new XlsxFileAdapter();
        var workbooks = new List<Workbook>(iterations + 1);
        for (var i = 0; i <= iterations; i++)
        {
            using var loadStream = new MemoryStream(package, writable: false);
            var workbook = adapter.Load(loadStream);
            PrepareLoadedWorkbookForEdit(workbook);
            AssertGeneratedStyleHeavyWorkbook(workbook);
            workbook.Sheets[0].ClearCell((uint)(i + 1), 1);
            workbooks.Add(workbook);
        }

        using (var warmup = new MemoryStream())
            adapter.Save(workbooks[0], warmup);

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
            adapter.Save(workbooks[i + 1], stream);
            step.Stop();
            timings.Add(step.Elapsed.TotalMilliseconds);
            packageSizes.Add(stream.Length);
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var ordered = timings.OrderBy(value => value).ToArray();
        var p95 = ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)];

        Console.WriteLine(
            "PERF XLSX_SAVE_LOADED_GENERATED_STYLE_HEAVY_CLEARED_CELL " +
            $"sheets={GeneratedStyleHeavySheetCount} rows={GeneratedStyleHeavyRowsPerSheet} " +
            $"value_cols={GeneratedStyleHeavyValueColumnsPerSheet} style_only_cols={GeneratedStyleHeavyStyleOnlyColumnsPerSheet} " +
            $"style_only_cells={GeneratedStyleHeavySheetCount * GeneratedStyleHeavyRowsPerSheet * GeneratedStyleHeavyStyleOnlyColumnsPerSheet:N0} " +
            $"steps={iterations} source_bytes={package.Length:N0} package_bytes={packageSizes.Max():N0} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={timings.Average():F2} " +
            $"p95_ms={p95:F2} max_ms={ordered[^1]:F2} allocated_bytes={allocatedBytes:N0}");

        timings.Average().Should().BeGreaterThan(0);
        packageSizes.Should().OnlyContain(size => size > 0);
    }

    [BenchmarkFact]
    public void Benchmark_SaveLoadedGeneratedStyleHeavyFullFallbackWorkbook_ReportsTiming()
    {
        const int iterations = 3;
        var package = CreateGeneratedStyleHeavyXlsxPackage();
        var adapter = new XlsxFileAdapter();
        var workbooks = new List<Workbook>(iterations + 1);
        for (var i = 0; i <= iterations; i++)
        {
            using var loadStream = new MemoryStream(package, writable: false);
            var workbook = adapter.Load(loadStream);
            PrepareLoadedWorkbookForEdit(workbook);
            AssertGeneratedStyleHeavyWorkbook(workbook);
            ApplyGeneratedStyleHeavyFallbackMutation(workbook, i);
            workbooks.Add(workbook);
        }

        using (var warmup = new MemoryStream())
            adapter.Save(workbooks[0], warmup);

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
            adapter.Save(workbooks[i + 1], stream);
            step.Stop();
            timings.Add(step.Elapsed.TotalMilliseconds);
            packageSizes.Add(stream.Length);
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var ordered = timings.OrderBy(value => value).ToArray();
        var p95 = ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)];

        Console.WriteLine(
            "PERF XLSX_SAVE_LOADED_GENERATED_STYLE_HEAVY_FULL_FALLBACK " +
            $"sheets={GeneratedStyleHeavySheetCount} rows={GeneratedStyleHeavyRowsPerSheet} " +
            $"value_cols={GeneratedStyleHeavyValueColumnsPerSheet} style_only_cols={GeneratedStyleHeavyStyleOnlyColumnsPerSheet} " +
            $"style_only_cells={GeneratedStyleHeavySheetCount * GeneratedStyleHeavyRowsPerSheet * GeneratedStyleHeavyStyleOnlyColumnsPerSheet:N0} " +
            $"steps={iterations} source_bytes={package.Length:N0} package_bytes={packageSizes.Max():N0} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={timings.Average():F2} " +
            $"p95_ms={p95:F2} max_ms={ordered[^1]:F2} allocated_bytes={allocatedBytes:N0} " +
            FormatSaveDiagnostics(adapter));

        timings.Average().Should().BeGreaterThan(0);
        packageSizes.Should().OnlyContain(size => size > 0);
    }

    [BenchmarkFact]
    public void Benchmark_SaveLoadedDenseMutatedWorkbook_ReportsTiming()
    {
        const int iterations = 5;
        var package = CreateDenseXlsxPackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var loadStream = new MemoryStream(package, writable: false))
        {
            workbook = adapter.Load(loadStream);
            PrepareLoadedWorkbookForEdit(workbook);
        }

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

    [BenchmarkFact]
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
            PrepareLoadedWorkbookForEdit(workbook);
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

    [BenchmarkFact]
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

    [BenchmarkFact]
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

    [BenchmarkFact]
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

    [BenchmarkFact]
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

    [BenchmarkFact]
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

    [BenchmarkFact]
    public void Benchmark_SaveLoadedWorksheetReplayMetadataWorkbook_ReportsTiming()
    {
        const int iterations = 3;
        var package = CreateWorksheetReplayMetadataSourcePackage();
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var loadStream = new MemoryStream(package, writable: false))
        {
            workbook = adapter.Load(loadStream);
            PrepareLoadedWorkbookForEdit(workbook);
        }
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
}
