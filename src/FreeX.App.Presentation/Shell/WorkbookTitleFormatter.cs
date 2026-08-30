using Free.Shared.AppServices;

namespace FreeX.App.Presentation.Shell;

public static class WorkbookTitleFormatter
{
    private const string GroupSuffix = " [Group]";
    private const string ReadOnlySuffix = " [Read-Only]";
    private static readonly ApplicationFrameDescriptor Frame = ApplicationFrameDescriptor.Create(
        applicationName: "FreeX",
        defaultDocumentDisplayName: "Book1",
        dirtyMarker: "*",
        separator: " - ");

    public static string Format(
        string workbookName,
        bool isDirty,
        bool isGrouped,
        string windowSuffix = "",
        bool isReadOnly = false) =>
        ApplicationWindowTitlePolicy.Compose(
            Frame.Title,
            workbookName,
            isDirty,
            windowSuffix,
            (isGrouped ? GroupSuffix : "") + (isReadOnly ? ReadOnlySuffix : ""));

    public static string DisplayNameFromPath(string path) =>
        WindowTitlePlanner.DisplayNameFromPath(path);
}
