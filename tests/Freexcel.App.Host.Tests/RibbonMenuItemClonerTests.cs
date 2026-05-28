using System.Windows.Controls;
using FluentAssertions;
using Freexcel.App.Host;

namespace Freexcel.App.Host.Tests;

public sealed class RibbonMenuItemClonerTests
{
    [Fact]
    public void SynchronizeClonedMenuItems_RefreshesChangedKeyTipForRouting()
    {
        StaTestRunner.Run(() =>
        {
            var source = new MenuItem { Header = "Dynamic Choice" };
            RibbonTooltip.SetKeyTip(source, "A");
            var clone = (MenuItem)RibbonMenuItemCloner.CloneRibbonMenuItem(source)!;

            RibbonTooltip.SetKeyTip(source, "B");
            RibbonMenuItemCloner.SynchronizeClonedMenuItems(CreateMenu(source).Items, CreateMenu(clone).Items);

            RibbonTooltip.GetKeyTip(clone).Should().Be("B");
            clone.InputGestureText.Should().Be("B");
        });
    }

    [Fact]
    public void SynchronizeClonedMenuItems_ClearsStaleKeyTipWhenSourceNoLongerHasOne()
    {
        StaTestRunner.Run(() =>
        {
            var source = new MenuItem { Header = "Dynamic Choice" };
            RibbonTooltip.SetKeyTip(source, "A");
            var clone = (MenuItem)RibbonMenuItemCloner.CloneRibbonMenuItem(source)!;

            RibbonTooltip.SetKeyTip(source, "");
            source.InputGestureText = "Ctrl+D";
            RibbonMenuItemCloner.SynchronizeClonedMenuItems(CreateMenu(source).Items, CreateMenu(clone).Items);

            RibbonTooltip.GetKeyTip(clone).Should().Be("");
            clone.InputGestureText.Should().Be("Ctrl+D");
        });
    }

    private static ContextMenu CreateMenu(MenuItem item)
    {
        var menu = new ContextMenu();
        menu.Items.Add(item);
        return menu;
    }
}
