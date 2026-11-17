using FluentAssertions;
using FreeX.App.Presentation.Backstage;

namespace FreeX.App.Presentation.Tests.Backstage;

public sealed class FreeXBackstageOpenPanePlannerTests
{
    [Fact]
    public void Build_ExposesRecentAndBrowseLocations()
    {
        var plan = FreeXBackstageOpenPanePlanner.Build();

        plan.TitleKey.Should().Be("MainWindow_Text_Open");
        plan.RecentSectionKey.Should().Be("MainWindow_Text_Recent");
        plan.Locations.Select(location => location.Id)
            .Should().Equal(FreeXBackstageOpenLocationId.Recent, FreeXBackstageOpenLocationId.Browse);
        plan.Locations.Single(location => location.Id == FreeXBackstageOpenLocationId.Recent)
            .AutomationId.Should().Be("BackstageOpenRecentLocation");
        plan.Locations.Single(location => location.Id == FreeXBackstageOpenLocationId.Browse)
            .LabelKey.Should().Be("GetData_BrowseButton");
    }
}
