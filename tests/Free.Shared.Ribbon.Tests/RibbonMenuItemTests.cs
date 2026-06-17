using FluentAssertions;

namespace Free.Shared.Ribbon.Tests;

public class RibbonMenuItemTests
{
    [Fact]
    public void IsChecked_DefaultsToNull_SoExistingItemsAreNotCheckable()
    {
        var item = new RibbonMenuItem("Cu_t", CommandId: new RibbonCommandId("Cut"));

        item.IsChecked.Should().BeNull();
    }

    [Fact]
    public void IsChecked_CanCarryCheckState()
    {
        new RibbonMenuItem("_Set as Total") { IsChecked = true }.IsChecked.Should().BeTrue();
        new RibbonMenuItem("_Set as Total") { IsChecked = false }.IsChecked.Should().BeFalse();
    }

    [Fact]
    public void Separator_IsNotCheckable()
    {
        RibbonMenuItem.Separator().IsChecked.Should().BeNull();
    }
}
