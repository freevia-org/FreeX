namespace FreeX.App.Presentation.Backstage;

public enum FreeXBackstageOpenLocationId
{
    Recent,
    Browse
}

public sealed record FreeXBackstageOpenLocationDescriptor(
    FreeXBackstageOpenLocationId Id,
    string LabelKey,
    string AutomationId);

public sealed record FreeXBackstageOpenPanePlan(
    string TitleKey,
    string RecentSectionKey,
    string SearchAutomationNameKey,
    IReadOnlyList<FreeXBackstageOpenLocationDescriptor> Locations);

public static class FreeXBackstageOpenPanePlanner
{
    public static string? ResolveEmptyStateKey(bool hasFilter, int rowCount)
    {
        if (rowCount > 0)
            return null;

        return hasFilter
            ? "Backstage_Home_NoMatchingWorkbooks"
            : "Backstage_Home_NoRecentWorkbooks";
    }

    public static FreeXBackstageOpenPanePlan Build() => new(
        "MainWindow_Text_Open",
        "MainWindow_Text_Recent",
        "MainWindow_AutomationName_SearchRecentFiles",
        [
            new(FreeXBackstageOpenLocationId.Recent, "MainWindow_Text_Recent", "BackstageOpenRecentLocation"),
            new(FreeXBackstageOpenLocationId.Browse, "GetData_BrowseButton", "BackstageOpenBrowseButton"),
        ]);
}
