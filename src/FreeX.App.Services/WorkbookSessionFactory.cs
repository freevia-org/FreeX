using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public sealed class WorkbookSessionFactory
{
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
}
