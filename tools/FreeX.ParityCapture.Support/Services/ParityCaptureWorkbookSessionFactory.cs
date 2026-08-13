using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public static class ParityCaptureWorkbookSessionFactory
{
    public static WorkbookSession Create(
        WorkbookSessionFactory sessionFactory,
        double viewportHeight,
        double viewportWidth,
        bool includeObjects = false,
        IEnumerable<IFileAdapter>? adapters = null,
        IViewportService? viewportService = null)
    {
        ArgumentNullException.ThrowIfNull(sessionFactory);

        var workbook = ParityDemoWorkbookFactory.Create();
        var source = new StartupWorkbookLoadResult(
            workbook,
            workbook.Name,
            "Showing parity demo workbook.",
            IsFallback: false);

        return sessionFactory.Create(
            source,
            viewportHeight,
            viewportWidth,
            includeObjects,
            adapters,
            viewportService);
    }
}
