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

    public WorkbookSession CreateOpened(
        WorkbookOpenTarget target,
        WorkbookOpenResult result,
        double viewportHeight,
        double viewportWidth,
        bool includeObjects = false,
        IEnumerable<IFileAdapter>? adapters = null,
        IViewportService? viewportService = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(result);

        result.Workbook.Name = Path.GetFileName(target.Path);
        var source = new StartupWorkbookLoadResult(
            result.Workbook,
            result.Workbook.Name,
            $"Opened {FileFormatResolver.NormalizeExtension(target.Extension)}.",
            IsFallback: false,
            SourcePath: target.Path,
            OpenedAsTemplate: result.OpenedAsTemplate,
            FeatureReport: result.FeatureReport,
            LoadWarnings: result.LoadWarnings);

        return Create(
            source,
            viewportHeight,
            viewportWidth,
            includeObjects,
            adapters,
            viewportService);
    }
}
