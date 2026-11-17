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
    IReadOnlyList<FreeXBackstageOpenLocationDescriptor> Locations);

public static class FreeXBackstageOpenPanePlanner
{
    public static FreeXBackstageOpenPanePlan Build() => new(
        "MainWindow_Text_Open",
        "MainWindow_Text_Recent",
        [
            new(FreeXBackstageOpenLocationId.Recent, "MainWindow_Text_Recent", "BackstageOpenRecentLocation"),
            new(FreeXBackstageOpenLocationId.Browse, "GetData_BrowseButton", "BackstageOpenBrowseButton"),
        ]);
}
