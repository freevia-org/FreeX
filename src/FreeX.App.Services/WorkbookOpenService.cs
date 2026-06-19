using System.Diagnostics;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public sealed class WorkbookOpenService
{
    private readonly Action<Workbook> _recalculateAllFormulas;
    private readonly Func<Stream, XlsxFeatureReport> _inspectXlsx;
    private readonly bool _hasCustomInspectXlsx;
    private readonly long _maxFileBytes;

    public WorkbookOpenService(
        Action<Workbook>? recalculateAllFormulas = null,
        Func<Stream, XlsxFeatureReport>? inspectXlsx = null,
        long maxFileBytes = WorkbookOpenSizeGuard.DefaultMaxFileBytes)
    {
        _recalculateAllFormulas = recalculateAllFormulas ?? (_ => { });
        _inspectXlsx = inspectXlsx ?? XlsxFeatureInspector.Inspect;
        _hasCustomInspectXlsx = inspectXlsx is not null;
        _maxFileBytes = maxFileBytes;
    }

    public async Task<WorkbookOpenResult> LoadAsync(
        string path,
        IFileAdapter adapter,
        string extension,
        FileFormatDescriptor format,
        IProgress<WorkbookOpenProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(format);
        cancellationToken.ThrowIfCancellationRequested();

        var fileBytes = new FileInfo(path).Length;
        WorkbookOpenSizeGuard.EnsureFileWithinLimit(fileBytes, _maxFileBytes);
        ReportProgress(progress, WorkbookOpenPhase.Reading, TimeSpan.Zero, 8);

        XlsxFeatureReport? featureReport = null;
        var isOpenXmlExcelPackage = IsOpenXmlExcelPackageExtension(extension);
        var inspectFeaturesDuringLoad = isOpenXmlExcelPackage && adapter is XlsxFileAdapter && !_hasCustomInspectXlsx;
        if (isOpenXmlExcelPackage && !inspectFeaturesDuringLoad)
        {
            featureReport = await RunStageAsync(
                progress,
                WorkbookOpenPhase.Inspecting,
                8,
                16,
                EstimateStageDuration(fileBytes, secondsPerMegabyte: 0.5, floorSeconds: 0.4),
                cancellationToken,
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var fileStream = OpenFileStream(path);
                    return _inspectXlsx(fileStream);
                }).ConfigureAwait(false);
        }

        IReadOnlyList<string> loadWarnings = [];
        var parseStartPercent = inspectFeaturesDuringLoad ? 8 : 16;
        var workbook = await RunStageAsync(
            progress,
            WorkbookOpenPhase.Parsing,
            parseStartPercent,
            90,
            EstimateStageDuration(fileBytes, secondsPerMegabyte: 1.4, floorSeconds: 0.5),
            cancellationToken,
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var fileStream = OpenFileStream(path);
                if (adapter is XlsxFileAdapter xlsxAdapter)
                {
                    var result = xlsxAdapter.LoadWithWarnings(fileStream, inspectFeaturesDuringLoad);
                    cancellationToken.ThrowIfCancellationRequested();
                    loadWarnings = result.Warnings;
                    featureReport ??= result.FeatureReport;
                    return result.Workbook;
                }

                var loadedWorkbook = adapter.Load(fileStream);
                cancellationToken.ThrowIfCancellationRequested();
                return loadedWorkbook;
            }).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        WorkbookOpenNormalizer.ApplyTextWorkbookSheetName(workbook, extension, Path.GetFileNameWithoutExtension(path));

        // Excel applies pivot table AND structured table styles (PivotStyleLight16, TableStyleMedium2,
        // ...) dynamically rather than baking them into per-cell styles, so a pivot/table loaded from
        // xlsx has correct values but no header/banding formatting.  Materialize the styles onto the
        // loaded cells so they look like Excel.  This runs on the load's background thread.  When the
        // recalc branch below runs it rebases the patch-save snapshot (which captures the styling);
        // otherwise rebase here so the materialized styling persists through a later save instead of
        // being replaced by the original source bytes (the rebase keeps the SAVED file's tables part +
        // styles unchanged because the source package, not the materialized model, is what is written).
        var materializedDynamicStyles = adapter is XlsxFileAdapter &&
            (PivotTableRefreshService.ApplyLoadedPivotStyles(workbook) |
             StructuredTableStyleService.ApplyLoadedTableStyles(workbook));

        if (WorkbookFormulaScanner.HasFormulas(workbook) &&
            ShouldRecalculateLoadedFormulas(workbook, adapter, isOpenXmlExcelPackage))
        {
            await RunStageAsync(
                progress,
                WorkbookOpenPhase.Calculating,
                90,
                98,
                EstimateStageDuration(fileBytes, secondsPerMegabyte: 0.9, floorSeconds: 0.4),
                cancellationToken,
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _recalculateAllFormulas(workbook);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (adapter is XlsxFileAdapter xlsxAdapter)
                        xlsxAdapter.RebaseLoadedPackageSnapshot(workbook);
                    return true;
                }).ConfigureAwait(false);
        }
        else
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (materializedDynamicStyles && adapter is XlsxFileAdapter dynamicStyleAdapter)
                dynamicStyleAdapter.RebaseLoadedPackageSnapshot(workbook);
            ReportProgress(progress, WorkbookOpenPhase.Calculating, TimeSpan.Zero, 98);
        }
        cancellationToken.ThrowIfCancellationRequested();

        return new WorkbookOpenResult(
            workbook,
            featureReport,
            Path.GetFileNameWithoutExtension(path),
            format.OpensAsTemplate,
            loadWarnings);
    }

    private static async Task<T> RunStageAsync<T>(
        IProgress<WorkbookOpenProgressUpdate>? progress,
        WorkbookOpenPhase phase,
        double startPercent,
        double endPercent,
        TimeSpan expectedDuration,
        CancellationToken cancellationToken,
        Func<T> work)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReportProgress(progress, phase, TimeSpan.Zero, startPercent);
        if (progress is null)
            return await Task.Run(work, cancellationToken).ConfigureAwait(false);

        using var progressCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var progressTask = ReportStageProgressAsync(
            progress,
            phase,
            startPercent,
            endPercent,
            expectedDuration,
            progressCancellation.Token);

        try
        {
            return await Task.Run(work, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            progressCancellation.Cancel();
            try { await progressTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            if (!cancellationToken.IsCancellationRequested)
                ReportProgress(progress, phase, TimeSpan.Zero, endPercent);
        }
    }

    private static async Task ReportStageProgressAsync(
        IProgress<WorkbookOpenProgressUpdate> progress,
        WorkbookOpenPhase phase,
        double startPercent,
        double endPercent,
        TimeSpan expectedDuration,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            var percent = CalculateStageProgress(startPercent, endPercent, stopwatch.Elapsed, expectedDuration);
            ReportProgress(progress, phase, stopwatch.Elapsed, percent);
        }
    }

    private static void ReportProgress(
        IProgress<WorkbookOpenProgressUpdate>? progress,
        WorkbookOpenPhase phase,
        TimeSpan elapsed,
        double? percent)
    {
        progress?.Report(new WorkbookOpenProgressUpdate(phase, elapsed, percent));
    }

    private static FileStream OpenFileStream(string path)
    {
        return new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 128,
            useAsync: true);
    }

    // Estimates how long a load stage should take for a file of this size so the progress bar can
    // advance roughly linearly with real time instead of crawling against a fixed worst-case guess.
    // Calibrated from large-file measurements (~1.4 s/MB for the ClosedXML-backed parse).  Estimates
    // need only be in the right ballpark: the per-stage interpolation holds just short of the stage
    // end until the work actually completes, so an under- or over-estimate self-corrects gracefully.
    private static TimeSpan EstimateStageDuration(long fileBytes, double secondsPerMegabyte, double floorSeconds)
    {
        var megabytes = Math.Max(0, fileBytes) / (1024.0 * 1024.0);
        return TimeSpan.FromSeconds(Math.Max(floorSeconds, megabytes * secondsPerMegabyte));
    }

    private static double? CalculateStageProgress(
        double startPercent,
        double endPercent,
        TimeSpan elapsed,
        TimeSpan expectedDuration)
    {
        if (expectedDuration <= TimeSpan.Zero)
            return endPercent;

        var ratio = elapsed.TotalMilliseconds / expectedDuration.TotalMilliseconds;
        if (ratio >= 1)
            return null;

        ratio = Math.Clamp(ratio, 0, 0.92);
        return startPercent + ((endPercent - startPercent) * ratio);
    }

    private static bool IsOpenXmlExcelPackageExtension(string extension) =>
        extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".xlsm", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".xltx", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".xltm", StringComparison.OrdinalIgnoreCase);

    private static bool ShouldRecalculateLoadedFormulas(
        Workbook workbook,
        IFileAdapter adapter,
        bool isOpenXmlExcelPackage)
    {
        if (isOpenXmlExcelPackage && adapter is XlsxFileAdapter)
        {
            return workbook.FullCalculationOnLoad ||
                   workbook.ForceFullCalculation ||
                   workbook.Sheets.Any(sheet => sheet.FullCalculationOnLoad);
        }

        return true;
    }
}
