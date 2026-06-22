using Free.Shared.AppServices;
using Free.Shared.Shell.Wpf;

namespace FreeW.App.Host.Backstage;

internal static class BackstageSharePanePlanner
{
    private static readonly WorkbookShareActionSurface LocalShareSurface =
        new("Windows Share", CanShowShareSheet: false, CanOpenContainingFolder: true);

    public static IReadOnlyList<BackstageActionGroup> Build(
        string? currentPath,
        Func<string, bool> fileExists,
        Action saveAs,
        Action<string> openContainingFolder,
        Action saveCopy,
        Action exportPdf)
    {
        ArgumentNullException.ThrowIfNull(fileExists);
        ArgumentNullException.ThrowIfNull(saveAs);
        ArgumentNullException.ThrowIfNull(openContainingFolder);
        ArgumentNullException.ThrowIfNull(saveCopy);
        ArgumentNullException.ThrowIfNull(exportPdf);

        var sharePlan = WorkbookShareActionPlanner.CreatePlan(currentPath, LocalShareSurface, fileExists);
        var primaryAction = sharePlan.Kind == WorkbookShareActionPlanKind.OpenContainingFolder &&
            !string.IsNullOrWhiteSpace(sharePlan.Path)
                ? new BackstageActionRow(
                    "Open Containing Folder",
                    WorkbookShareActionPlanner.FormatStatus(sharePlan),
                    () => openContainingFolder(sharePlan.Path!))
                : new BackstageActionRow(
                    "Save As",
                    WorkbookShareActionPlanner.FormatStatus(sharePlan),
                    saveAs);

        return
        [
            new("Share",
            [
                primaryAction,
            ]),
            new("Send a Copy",
            [
                new("Save a Copy", "Create a separate editable copy without changing the current document.", saveCopy),
                new("Create PDF/XPS", "Publish a fixed-layout copy for sharing or printing.", exportPdf),
            ]),
        ];
    }
}
