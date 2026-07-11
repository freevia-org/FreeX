using System.Text.RegularExpressions;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
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
                CreateProgressUpdate).ConfigureAwait(false);
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

                var loadedWorkbook = adapter.Load(fileStream);
                cancellationToken.ThrowIfCancellationRequested();
                return loadedWorkbook;
            },
            CreateProgressUpdate).ConfigureAwait(false);
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
                CreateProgressUpdate).ConfigureAwait(false);
        }
        else
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Trusted cached values are kept as-is, but real Excel still refreshes volatile
            // functions (NOW/TODAY/RAND/OFFSET/INDIRECT/...) on an Automatic-mode open even though
            // it does not force a full recalculation of the rest of the workbook (Manual mode does
            // not even do that much -- Excel leaves everything, including volatiles, untouched
            // until an explicit F9/edit). A cheap text scan (no AST parse) checks whether the
            // workbook has any volatile-function calls at all before paying for the
            // dependency-tracked recalc pass scoped to just those cells and their dependents.
            var recalculatedVolatileCells =
                workbook.CalculationMode != WorkbookCalculationMode.Manual &&
                WorkbookHasVolatileFormulas(workbook) &&
                RecalculateVolatileFormulasOnLoad(workbook);
            if ((materializedDynamicStyles || recalculatedVolatileCells) && adapter is XlsxFileAdapter dynamicStyleAdapter)
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
        // LoadAsync's non-recalculating branch runs that narrower pass itself via
        // RecalculateVolatileFormulasOnLoad instead of forcing a full recalc here.
        return workbook.FullCalculationOnLoad ||
               workbook.ForceFullCalculation ||
               workbook.Sheets.Any(sheet => sheet.FullCalculationOnLoad);
    }

    /// <summary>
    /// Runs a throwaway, dependency-tracked recalculation pass scoped to only the workbook's
    /// volatile-function cells (NOW/TODAY/RAND/OFFSET/INDIRECT/...) and their dependents, leaving
    /// every other formula's trusted cached value untouched. Used when
    /// <see cref="ShouldRecalculateLoadedFormulas"/> declined a full recalc (the file's cached
    /// values are otherwise trusted) but real Excel would still refresh volatiles on open.
    /// RecalcEngine.Recalculate with an empty changed-cell set only evaluates cells it already
    /// tracks as volatile (populated by RebuildFormulaDependencies) plus their dependents, so this
    /// is a cheap no-op when the workbook has no volatile formulas. Callers should gate this with
    /// <see cref="WorkbookHasVolatileFormulas"/> first to skip the dependency-graph rebuild
    /// entirely for the common case of a workbook with none.
    /// </summary>
    private static bool RecalculateVolatileFormulasOnLoad(Workbook workbook)
    {
        var recalcEngine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        recalcEngine.RebuildFormulaDependencies(workbook);
        var report = recalcEngine.Recalculate(workbook, []);
        return report.RecalculatedCells.Count > 0;
    }

    // Matches an identifier immediately followed by '(' (optionally separated by spaces, which the
    // formula lexer also tolerates before an open paren) -- a candidate function-call name, used to
    // detect volatile functions without a full parse of every cached formula.
    private static readonly Regex FunctionCallNamePattern =
        new(@"[A-Za-z_][A-Za-z0-9_.]*(?=[ \t]*\()", RegexOptions.Compiled);

    private static bool WorkbookHasVolatileFormulas(Workbook workbook)
    {
        foreach (var sheet in workbook.Sheets)
        {
            if (!sheet.HasFormulas)
                continue;

            foreach (var address in sheet.EnumerateFormulaCells())
            {
                var formulaText = sheet.GetCell(address)?.FormulaText;
                if (formulaText is null)
                    continue;

                foreach (Match match in FunctionCallNamePattern.Matches(formulaText))
                {
                    if (BuiltInFunctions.IsVolatile(match.Value.ToUpperInvariant()))
                        return true;
                }
            }
        }

        return false;
    }
}
