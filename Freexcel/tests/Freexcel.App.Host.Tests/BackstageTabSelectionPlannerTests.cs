using FluentAssertions;
using Freexcel.App.Host;

namespace Freexcel.App.Host.Tests;

public sealed class BackstageTabSelectionPlannerTests
{
    [Fact]
    public void Select_RecentFromPinned_ShowsRecentAndReportsChange()
    {
        var plan = BackstageTabSelectionPlanner.Select(
            isShowingPinnedList: true,
            BackstageRecentTab.Recent);

        plan.ActiveTab.Should().Be(BackstageRecentTab.Recent);
        plan.RecentListVisible.Should().BeTrue();
        plan.PinnedListVisible.Should().BeFalse();
        plan.Changed.Should().BeTrue();
    }

    [Fact]
    public void Select_PinnedFromRecent_ShowsPinnedAndReportsChange()
    {
        var plan = BackstageTabSelectionPlanner.Select(
            isShowingPinnedList: false,
            BackstageRecentTab.Pinned);

        plan.ActiveTab.Should().Be(BackstageRecentTab.Pinned);
        plan.RecentListVisible.Should().BeFalse();
        plan.PinnedListVisible.Should().BeTrue();
        plan.Changed.Should().BeTrue();
    }

    [Theory]
    [InlineData(false, BackstageRecentTab.Recent)]
    [InlineData(true, BackstageRecentTab.Pinned)]
    public void Select_AlreadyActiveTab_ReportsNoChange(bool isShowingPinnedList, BackstageRecentTab requestedTab)
    {
        var plan = BackstageTabSelectionPlanner.Select(isShowingPinnedList, requestedTab);

        plan.Changed.Should().BeFalse();
    }
}
