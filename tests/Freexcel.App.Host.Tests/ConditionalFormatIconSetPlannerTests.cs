using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class ConditionalFormatIconSetPlannerTests
{
    [Fact]
    public void GalleryGroups_ExposeExcelStyleIconSetCategoriesInOrder()
    {
        var groups = ConditionalFormatIconSetPlanner.GalleryGroups;

        groups.Select(group => group.Name)
            .Should()
            .Equal("Directional", "Shapes", "Indicators", "Ratings");

        groups.Single(group => group.Name == "Directional").Options.Select(option => option.Style)
            .Should()
            .ContainInOrder("3Arrows", "3ArrowsGray", "4Arrows", "4ArrowsGray", "5Arrows", "5ArrowsGray");

        groups.Single(group => group.Name == "Ratings").Options.Select(option => option.Style)
            .Should()
            .ContainInOrder("4Rating", "5Rating", "5Quarters", "5Boxes");
    }
}
