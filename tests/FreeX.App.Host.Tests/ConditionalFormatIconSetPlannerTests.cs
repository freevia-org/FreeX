using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class ConditionalFormatIconSetPlannerTests
{
    [Fact]
    public void GalleryGroups_ExposeExcelStyleIconSetCategoriesInOrder()
    {
        var groups = ConditionalFormatIconSetPlanner.GalleryGroups;

        groups.Select(group => group.Name)
            .Should()
            .Equal(
                UiText.Get("ConditionalFormatIconSet_Category_Directional"),
                UiText.Get("ConditionalFormatIconSet_Category_Shapes"),
                UiText.Get("ConditionalFormatIconSet_Category_Indicators"),
                UiText.Get("ConditionalFormatIconSet_Category_Ratings"));

        groups.Single(group => group.Name == UiText.Get("ConditionalFormatIconSet_Category_Directional")).Options.Select(option => option.Style)
            .Should()
            .ContainInOrder("3Arrows", "3ArrowsGray", "4Arrows", "4ArrowsGray", "5Arrows", "5ArrowsGray");

        groups.Single(group => group.Name == UiText.Get("ConditionalFormatIconSet_Category_Ratings")).Options.Select(option => option.Style)
            .Should()
            .ContainInOrder("4Rating", "5Rating", "5Quarters", "5Boxes");

        groups.Single(group => group.Name == UiText.Get("ConditionalFormatIconSet_Category_Directional"))
            .Options[0]
            .Label
            .Should()
            .Be(UiText.Get("ConditionalFormatIconSet_3Arrows_Label"));
    }
}
