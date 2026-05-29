namespace FreeX.App.Host;

public enum BackstageRecentTab
{
    Recent,
    Pinned
}

public sealed record BackstageTabSelectionPlan(
    BackstageRecentTab ActiveTab,
    bool RecentListVisible,
    bool PinnedListVisible,
    bool Changed);

public static class BackstageTabSelectionPlanner
{
    public static BackstageTabSelectionPlan Select(bool isShowingPinnedList, BackstageRecentTab requestedTab)
    {
        var activeTab = requestedTab;
        var isPinnedTab = IsPinnedTab(activeTab);

        return new BackstageTabSelectionPlan(
            activeTab,
            !isPinnedTab,
            isPinnedTab,
            isShowingPinnedList != isPinnedTab);
    }

    private static bool IsPinnedTab(BackstageRecentTab tab) => tab == BackstageRecentTab.Pinned;
}
