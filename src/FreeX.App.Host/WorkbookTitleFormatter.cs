using Free.Shared.AppServices;

namespace FreeX.App.Host;

public static class WorkbookTitleFormatter
{
    private const string ApplicationTitle = "FreeX";
    private const string GroupSuffix = " [Group]";
    private const string DirtySuffix = "*";
    private const string Separator = " - ";

    public static string Format(string workbookName, bool isDirty, bool isGrouped, string windowSuffix = "") =>
        WindowTitlePlanner.Compose(
            displayName: workbookName,
            applicationName: ApplicationTitle,
            isDirty: isDirty,
            dirtyMarker: DirtySuffix,
            separator: Separator,
            windowSuffix: windowSuffix,
            groupSuffix: isGrouped ? GroupSuffix : "");

    public static string DisplayNameFromPath(string path) =>
        WindowTitlePlanner.DisplayNameFromPath(path);
}
