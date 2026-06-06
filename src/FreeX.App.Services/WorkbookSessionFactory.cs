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
}
