namespace Freexcel.App.Host;

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
        return new BackstageTabSelectionPlan(
            activeTab,
            activeTab == BackstageRecentTab.Recent,
            activeTab == BackstageRecentTab.Pinned,
            isShowingPinnedList != (activeTab == BackstageRecentTab.Pinned));
    }
}
