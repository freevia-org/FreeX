using Free.Shared.AppServices;

namespace FreeX.App.Presentation.Shell;

public static class WorkbookTitleFormatter
{
    private const string GroupSuffix = " [Group]";
    private static readonly ApplicationWindowTitleSpec Title = new(
        ApplicationName: "FreeX",
        DefaultDocumentDisplayName: "Book1",
        DirtyMarker: "*",
        Separator: " - ",
        ApplicationPlacement: WindowTitleApplicationPlacement.DocumentThenApplication);

    public static string Format(string workbookName, bool isDirty, bool isGrouped, string windowSuffix = "") =>
        ApplicationWindowTitlePolicy.Compose(
            Title,
            workbookName,
            isDirty,
            windowSuffix,
            isGrouped ? GroupSuffix : "");

    public static string DisplayNameFromPath(string path) =>
        WindowTitlePlanner.DisplayNameFromPath(path);
}
