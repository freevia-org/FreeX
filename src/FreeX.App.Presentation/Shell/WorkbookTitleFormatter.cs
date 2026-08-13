using Free.Shared.AppServices;

namespace FreeX.App.Presentation.Shell;

public static class WorkbookTitleFormatter
{
    private const string GroupSuffix = " [Group]";
    private static readonly ApplicationFrameDescriptor Frame = ApplicationFrameDescriptor.Create(
        applicationName: "FreeX",
        defaultDocumentDisplayName: "Book1",
        dirtyMarker: "*",
        separator: " - ");

    public static string Format(string workbookName, bool isDirty, bool isGrouped, string windowSuffix = "") =>
        ApplicationWindowTitlePolicy.Compose(
            Frame.Title,
            workbookName,
            isDirty,
            windowSuffix,
            isGrouped ? GroupSuffix : "");

    public static string DisplayNameFromPath(string path) =>
        WindowTitlePlanner.DisplayNameFromPath(path);
}
