using FluentAssertions;
using FreeX.App.Presentation.Backstage;

namespace FreeX.App.Presentation.Tests.Backstage;

public sealed class FreeXBackstageOpenPanePlannerTests
{
    [Fact]
    public void Build_ExposesLocalizedSearchNameAlongsideRecentAndBrowseLocations()
    {
        var plan = FreeXBackstageOpenPanePlanner.Build();

        plan.SearchAutomationNameKey.Should().Be("MainWindow_AutomationName_SearchRecentFiles");
        plan.Locations.Select(location => location.Id).Should().Equal(
            FreeXBackstageOpenLocationId.Recent,
            FreeXBackstageOpenLocationId.Browse);
    }

    [Theory]
    [InlineData(false, 0, "Backstage_Home_NoRecentWorkbooks")]
    [InlineData(true, 0, "Backstage_Home_NoMatchingWorkbooks")]
    [InlineData(false, 1, null)]
    [InlineData(true, 1, null)]
    public void ResolveEmptyStateKey_DistinguishesEmptyHistoryFromNoSearchMatches(
        bool hasFilter,
        int rowCount,
        string? expected)
    {
        FreeXBackstageOpenPanePlanner.ResolveEmptyStateKey(hasFilter, rowCount).Should().Be(expected);
    }
}
