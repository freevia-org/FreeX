using Free.Shared.AppServices;
using Free.Shared.Shell;

namespace FreeW.App.Presentation.Backstage;

public static class BackstageSharePanePlanner
{
    private static readonly DocumentShareActionSurface LocalShareSurface =
        new("Windows Share", CanShowShareSheet: false, CanOpenContainingFolder: true);

    private static readonly DocumentShareActionTextSpec ShareText =
        DocumentShareActionTextSpec.NeutralEnglish;

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

        var sharePlan = DocumentShareActionPlanner.CreatePlan(currentPath, LocalShareSurface, fileExists);
        var primaryAction = sharePlan.Kind == DocumentShareActionPlanKind.OpenContainingFolder &&
            !string.IsNullOrWhiteSpace(sharePlan.Path)
                ? new BackstageActionRow(
                    "Open Containing Folder",
                    DocumentShareActionPlanner.FormatStatus(sharePlan, ShareText),
                    () => openContainingFolder(sharePlan.Path!))
                : new BackstageActionRow(
                    "Save As",
                    DocumentShareActionPlanner.FormatStatus(sharePlan, ShareText),
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
