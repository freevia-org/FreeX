using System.Windows.Automation;
using System.Windows.Controls;
using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed class RibbonMenuItemClonerTests
{
    [Fact]
    public void CloneRibbonMenuItem_CopiesTooltipCommandAndAutomationMetadata()
    {
        StaTestRunner.Run(() =>
        {
            var source = new MenuItem { Header = "Paste Values" };
            RibbonTooltip.SetTitle(source, "Paste Values");
            RibbonTooltip.SetDescription(source, "Paste only values from the clipboard.");
            RibbonTooltip.SetKeyTip(source, "V");
            RibbonMetadata.SetCommandName(source, "Paste Values");
            AutomationProperties.SetName(source, "Paste values");
            AutomationProperties.SetHelpText(source, "Pastes clipboard values without formulas.");
            AutomationProperties.SetAutomationId(source, "PasteValuesMenuItem");

            var clone = (MenuItem)RibbonMenuItemCloner.CloneRibbonMenuItem(source)!;

            RibbonTooltip.GetTitle(clone).Should().Be("Paste Values");
            RibbonTooltip.GetDescription(clone).Should().Be("Paste only values from the clipboard.");
            RibbonTooltip.GetKeyTip(clone).Should().Be("V");
            RibbonMetadata.GetCommandName(clone).Should().Be("Paste Values");
            AutomationProperties.GetName(clone).Should().Be("Paste values");
            AutomationProperties.GetHelpText(clone).Should().Be("Pastes clipboard values without formulas.");
            AutomationProperties.GetAutomationId(clone).Should().Be("PasteValuesMenuItem");
            clone.InputGestureText.Should().Be("V");
        });
    }

    [Fact]
    public void SynchronizeClonedMenuItems_RefreshesTooltipCommandAndAutomationMetadata()
    {
        StaTestRunner.Run(() =>
        {
            var source = new MenuItem { Header = "Dynamic Choice" };
            var clone = (MenuItem)RibbonMenuItemCloner.CloneRibbonMenuItem(source)!;

            RibbonTooltip.SetTitle(source, "Dynamic");
            RibbonTooltip.SetDescription(source, "Updated metadata.");
            RibbonMetadata.SetCommandName(source, "Dynamic Command");
            AutomationProperties.SetName(source, "Dynamic choice");
            AutomationProperties.SetHelpText(source, "Updated help.");
            AutomationProperties.SetAutomationId(source, "DynamicChoiceMenuItem");
            RibbonMenuItemCloner.SynchronizeClonedMenuItems(CreateMenu(source).Items, CreateMenu(clone).Items);

            RibbonTooltip.GetTitle(clone).Should().Be("Dynamic");
            RibbonTooltip.GetDescription(clone).Should().Be("Updated metadata.");
            RibbonMetadata.GetCommandName(clone).Should().Be("Dynamic Command");
            AutomationProperties.GetName(clone).Should().Be("Dynamic choice");
            AutomationProperties.GetHelpText(clone).Should().Be("Updated help.");
            AutomationProperties.GetAutomationId(clone).Should().Be("DynamicChoiceMenuItem");
        });
    }

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
