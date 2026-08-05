using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public sealed class WorkbookSessionFactory
{
    /// <summary>
    /// Creates a session over host-provided command, calculation, viewport, and document-state
    /// services. This lets an existing renderer migrate to <see cref="WorkbookSession"/> ownership
    /// without creating a second command history or recalculation graph.
    /// </summary>
    public WorkbookSession CreateHostOwned(
        StartupWorkbookLoadResult source,
        ICommandBus commandBus,
        RecalcEngine recalcEngine,
        IViewportService viewportService,
        IEnumerable<IFileAdapter> adapters,
        WorkbookDocumentState documentState,
        double viewportHeight,
        double viewportWidth,
        bool includeObjects = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(commandBus);
        ArgumentNullException.ThrowIfNull(recalcEngine);
        ArgumentNullException.ThrowIfNull(viewportService);
        ArgumentNullException.ThrowIfNull(adapters);
        ArgumentNullException.ThrowIfNull(documentState);

        return new WorkbookSession(
            source,
            adapters.ToList(),
            new WorkbookCellEditService(commandBus, recalcEngine),
            new WorkbookSheetSelectionService(),
            viewportService,
            viewportHeight,
            viewportWidth,
            includeObjects,
            documentState: documentState);
    }

    public WorkbookSession Create(
        StartupWorkbookLoadResult source,
        double viewportHeight,
        double viewportWidth,
        bool includeObjects = false,
        IEnumerable<IFileAdapter>? adapters = null,
        IViewportService? viewportService = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        var adapterCatalog = (adapters ?? WorkbookFileAdapterCatalog.CreateDefaultAdapters()).ToList();
        var workbook = source.Workbook;
        var recalcEngine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        var commandBus = new CommandBus(
            _ => new WorkbookCommandContext(workbook),
            (workbookId, ctx) => XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(ctx.Workbook, out _));
        var cellEditService = new WorkbookCellEditService(commandBus, recalcEngine);
        recalcEngine.RebuildFormulaDependencies(workbook);
        ApplyOnOpenVolatileRecalc(recalcEngine, workbook, adapterCatalog);

        return new WorkbookSession(
            source,
            adapterCatalog,
            cellEditService,
            new WorkbookSheetSelectionService(),
            viewportService ?? new ViewportService(),
            viewportHeight,
            viewportWidth,
            includeObjects);
    }

    public WorkbookSession CreateNew(
        double viewportHeight,
        double viewportWidth,
        bool includeObjects = false,
        WorkbookCreationOptions? options = null,
        IEnumerable<IFileAdapter>? adapters = null,
        IViewportService? viewportService = null)
    {
        var workbook = WorkbookFactory.Create(options);
        var source = new StartupWorkbookLoadResult(
            workbook,
            workbook.Name,
            "Created new workbook.",
            IsFallback: false);

        return Create(
            source,
            viewportHeight,
            viewportWidth,
            includeObjects,
            adapters,
            viewportService);
    }

    /// <summary>
    /// Creates a session over the fixed parity demo workbook (<see cref="ParityDemoWorkbookFactory"/>) so the
    /// Avalonia <c>--parity-capture</c> grid surface renders the SAME content the WPF host adopts, instead of
    /// the rich macOS-preview demo. Used only by the headless capture path.
    /// </summary>
    public WorkbookSession CreateParityDemo(
        double viewportHeight,
        double viewportWidth,
        bool includeObjects = false,
        IEnumerable<IFileAdapter>? adapters = null,
        IViewportService? viewportService = null)
    {
        var workbook = ParityDemoWorkbookFactory.Create();
        var source = new StartupWorkbookLoadResult(
            workbook,
            workbook.Name,
            "Showing parity demo workbook.",
            IsFallback: false);

        return Create(
            source,
            viewportHeight,
            viewportWidth,
            includeObjects,
            adapters,
            viewportService);
    }

    public WorkbookSession CreateOpened(
        WorkbookOpenTarget target,
        WorkbookOpenResult result,
        double viewportHeight,
        double viewportWidth,
        bool includeObjects = false,
        IEnumerable<IFileAdapter>? adapters = null,
        IViewportService? viewportService = null,
        WorkbookOpenCompletionPlan? completionPlan = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(result);

        var plan = completionPlan ?? WorkbookFileCompletionPlanner.PlanOpen(
            target,
            result,
            displayName: Path.GetFileName(target.Path));
        plan.Workbook.Name = plan.DisplayName;
        var source = new StartupWorkbookLoadResult(
            plan.Workbook,
            plan.DisplayName,
            plan.Status,
            IsFallback: false,
            SourcePath: plan.SourcePath,
            OpenedAsTemplate: plan.OpenedAsTemplate,
            FeatureReport: plan.FeatureReport,
            LoadWarnings: result.LoadWarnings,
            SourceFileAccessIdentity: plan.SourceFileAccessIdentity);

        return Create(
            source,
            viewportHeight,
            viewportWidth,
            includeObjects,
            adapters,
            viewportService);
    }

    /// <summary>
    /// Real Excel refreshes volatile functions (NOW/TODAY/RAND/OFFSET/INDIRECT/...) on an
    /// Automatic-mode open even for a workbook whose other cached formula values are trusted
    /// as-is -- it does not force a full recalculation of the rest of the workbook just because a
    /// volatile function appears somewhere (Manual mode does not even do that much; Excel leaves
    /// everything, including volatiles, untouched until an explicit F9/edit). <paramref
    /// name="recalcEngine"/>'s dependency graph must already be rebuilt (e.g. via <see
    /// cref="RecalcEngine.RebuildFormulaDependencies"/>) before calling this, so its internal
    /// volatile-cell tracking is accurate -- including volatility hidden behind a defined name
    /// (e.g. =SUM(SalesRange) where SalesRange=OFFSET(...): RecalcEngine.CollectReferences
    /// recurses into a NamedRangeNode's formula text and propagates its volatility up). Recalculate
    /// with an empty changed-cell set only evaluates cells already tracked as volatile plus their
    /// dependents, so this is a cheap no-op for the common case of a workbook with none, and there
    /// is no separate throwaway dependency graph built just to answer that question first.
    ///
    /// Public and static so every host that opens a workbook onto a live <see cref="RecalcEngine"/>
    /// can share this exact policy: <see cref="Create"/> uses it for every session it builds, and
    /// the WPF host's File&gt;Open path (which does not go through <see cref="Create"/>) calls it
    /// directly after its own <see cref="RecalcEngine.RebuildFormulaDependencies"/> call.
    /// </summary>
    public static void ApplyOnOpenVolatileRecalc(
        RecalcEngine recalcEngine,
        Workbook workbook,
        IEnumerable<IFileAdapter> adapterCatalog)
    {
        if (workbook.CalculationMode == WorkbookCalculationMode.Manual)
            return;

        var report = recalcEngine.Recalculate(workbook, []);
        if (report.RecalculatedCells.Count == 0)
            return;

        // Keep a since-loaded xlsx package snapshot's cached values in sync with the freshly
        // recalculated volatile cells, so a save without any further edits writes the refreshed
        // values instead of the stale ones read from disk. Rebase is a no-op for a workbook with
        // no tracked source package (new workbook, non-xlsx format, etc.) -- see
        // XlsxFileAdapter.RebaseLoadedPackageSnapshot. The package snapshot is keyed by workbook
        // identity in a static table, so any XlsxFileAdapter instance from the catalog can rebase
        // it; it need not be the specific instance that originally loaded the file.
        foreach (var adapter in adapterCatalog)
        {
            if (adapter is XlsxFileAdapter xlsxAdapter)
            {
                xlsxAdapter.RebaseLoadedPackageSnapshot(workbook);
                break;
            }
        }
    }
}
