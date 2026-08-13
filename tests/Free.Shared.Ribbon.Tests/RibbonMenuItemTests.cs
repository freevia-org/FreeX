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

    [Fact]
    public void Builder_CarriesOptionalMenuIconKindAndAccent()
    {
        var definition = new RibbonDefinitionBuilder()
            .Tab("tab", "Tab", "T", tab => tab.Group("group", "Group", "G", 1, group =>
                group.Medium("menu", "Menu", RibbonCommandIconKind.More, menu: menu =>
                    menu.Item(
                        "warning",
                        "Warning",
                        RibbonCommandIconKind.Warning,
                        "W",
                        accent: RibbonCommandIconAccent.Warning))))
            .Build();

        var item = definition.Tabs.Single().Groups.Single().Controls
            .OfType<RibbonDropdown>().Single().Menu.Items.Single();

        item.Icon.Should().Be(new RibbonCommandIcon(
            RibbonCommandIconKind.Warning,
            RibbonCommandIconAccent.Warning));
    }
}
