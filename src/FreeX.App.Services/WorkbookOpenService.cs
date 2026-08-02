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

        var openedFileInfo = new FileInfo(path);
        var fileBytes = openedFileInfo.Length;
        WorkbookOpenSizeGuard.EnsureFileWithinLimit(fileBytes, _maxFileBytes);
        // Snapshot the write time before any parsing so a later save can detect the file having
        // changed on disk since this open -- see WorkbookOpenResult.SourceLastWriteTimeUtc.
        var sourceLastWriteTimeUtc = openedFileInfo.LastWriteTimeUtc;
        ReportProgress(progress, WorkbookOpenPhase.Reading, TimeSpan.Zero, 8);

        XlsxFeatureReport? featureReport = null;
        var isOpenXmlExcelPackage = IsOpenXmlExcelPackageExtension(extension);
        var inspectFeaturesDuringLoad = isOpenXmlExcelPackage && adapter is XlsxFileAdapter && !_hasCustomInspectXlsx;
        if (isOpenXmlExcelPackage && !inspectFeaturesDuringLoad)
        {
            featureReport = await WorkbookProgressStageRunner.RunStageAsync(
                progress,
                WorkbookOpenPhase.Inspecting,
                8,
                16,
                WorkbookProgressStageRunner.EstimateStageDuration(fileBytes, secondsPerMegabyte: 0.5, floorSeconds: 0.4),
                cancellationToken,
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var fileStream = OpenFileStream(path);
                    return _inspectXlsx(fileStream);
                },
                CreateProgressUpdate,
                // R119-appservices-open-cancel-eager: safe to opt in -- this only ever touches a
                // private FileStream nothing else can reach, unlike WorkbookSaveService's Writing
                // stage which serializes the live, possibly shared Workbook (see
                // WorkbookProgressStageRunner.RunWorkAsync's doc comment for the full rationale).
                observeCancellationEagerly: true).ConfigureAwait(false);
        }

        IReadOnlyList<string> loadWarnings = [];
        var parseStartPercent = inspectFeaturesDuringLoad ? 8 : 16;
        var workbook = await WorkbookProgressStageRunner.RunStageAsync(
            progress,
            WorkbookOpenPhase.Parsing,
            parseStartPercent,
            90,
            WorkbookProgressStageRunner.EstimateStageDuration(fileBytes, secondsPerMegabyte: 1.4, floorSeconds: 0.5),
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

                // R92-io-legacy-format-read-5-1/5-2/5-3: a legacy .xls/.xlsb open never gets an
                // XlsxFeatureReport (isOpenXmlExcelPackage above is false for these extensions), so
                // this Warnings list is the ONLY open-time signal the shell has for lossy legacy
                // features (values-only BIFF12/parse-failure fallback, undroppable VBA macros,
                // dropped embedded charts). ShowXlsxLoadWarningsIfNeeded already displays
                // loadWarnings unconditionally regardless of extension, so populating it here is
                // enough to wire the warning through to the user.
                if (adapter is LegacyXlsFileAdapter legacyAdapter)
                {
                    var legacyResult = legacyAdapter.LoadWithWarnings(fileStream);
                    cancellationToken.ThrowIfCancellationRequested();
                    loadWarnings = legacyResult.Warnings;
                    return legacyResult.Workbook;
                }

                var loadedWorkbook = adapter.Load(fileStream);
                cancellationToken.ThrowIfCancellationRequested();
                return loadedWorkbook;
            },
            CreateProgressUpdate,
            // R119-appservices-open-cancel-eager: see the Inspecting stage above -- this parse only
            // ever builds a fresh, not-yet-published Workbook over a private FileStream.
            observeCancellationEagerly: true).ConfigureAwait(false);
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
            await WorkbookProgressStageRunner.RunStageAsync(
                progress,
                WorkbookOpenPhase.Calculating,
                90,
                98,
                WorkbookProgressStageRunner.EstimateStageDuration(fileBytes, secondsPerMegabyte: 0.9, floorSeconds: 0.4),
                cancellationToken,
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _recalculateAllFormulas(workbook);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (adapter is XlsxFileAdapter xlsxAdapter)
                        xlsxAdapter.RebaseLoadedPackageSnapshot(workbook);
                    return true;
                },
                CreateProgressUpdate,
                // R119-appservices-open-cancel-eager: `workbook` here is the same not-yet-published
                // instance built by the Parsing stage above -- nothing else can reach it until
                // LoadAsync returns, so eagerly observing cancellation is safe.
                observeCancellationEagerly: true).ConfigureAwait(false);
        }
        else
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Trusted cached values are kept as-is here. Real Excel still refreshes volatile
            // functions (NOW/TODAY/RAND/OFFSET/INDIRECT/...) on an Automatic-mode open even though
            // it does not force a full recalculation of the rest of the workbook, but that scoped
            // pass needs an accurate, already-built dependency graph to catch volatility hidden
            // behind a defined name (e.g. =SUM(SalesRange) where SalesRange=OFFSET(...)) -- so it
            // is left to the session engine that opens this workbook next
            // (WorkbookSessionFactory.Create runs it right after RebuildFormulaDependencies)
            // instead of duplicated here against a second, throwaway graph.
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
            loadWarnings,
            sourceLastWriteTimeUtc);
    }

    private static void ReportProgress(
        IProgress<WorkbookOpenProgressUpdate>? progress,
        WorkbookOpenPhase phase,
        TimeSpan elapsed,
        double? percent)
    {
        progress?.Report(new WorkbookOpenProgressUpdate(phase, elapsed, percent));
    }

    private static WorkbookOpenProgressUpdate CreateProgressUpdate(
        WorkbookOpenPhase phase,
        TimeSpan elapsed,
        double? percent) =>
        new(phase, elapsed, percent);

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
        // Both the OOXML package (.xlsx/.xlsm/.xltx/.xltm) and legacy BIFF (.xls/.xlsb) loaders
        // parse the source file's own "please fully recalculate" signal (calcPr fullCalcOnLoad /
        // forceFullCalc for OOXML, the BIFF recalc-id record's ForceFormulaRecalculation for
        // legacy) into these same workbook/sheet flags -- real Excel trusts a file's cached
        // formula values unless that flag says otherwise, for either container format.
        var hasTrustedCachedValues = (isOpenXmlExcelPackage && adapter is XlsxFileAdapter) ||
                                      adapter is LegacyXlsFileAdapter;
        if (!hasTrustedCachedValues)
            return true;

        // Real Excel trusts a file's cached formula values on an Automatic-mode open and does not
        // force a full workbook recalculation merely because the workbook happens to contain a
        // volatile function (NOW/TODAY/RAND/OFFSET/INDIRECT/...) somewhere -- it only marks the
        // volatile cells themselves (and their dependents) dirty for the next calculation pass.
        // That narrower pass is left to whichever session engine opens this workbook next
        // (WorkbookSessionFactory.Create), which runs it against its own already-built
        // dependency graph right after RebuildFormulaDependencies instead of this load path
        // building a second, throwaway graph just to answer the question.
        return workbook.FullCalculationOnLoad ||
               workbook.ForceFullCalculation ||
               workbook.Sheets.Any(sheet => sheet.FullCalculationOnLoad);
    }
}
