using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FluentAssertions;
using Freexcel.App.Host;

namespace Freexcel.App.Host.Tests;

public sealed class RibbonTooltipTests
{
    [Fact]
    public void KeyTip_AttachedProperty_RoundTripsCommandKeyTip()
    {
        var button = new DependencyObject();

        RibbonTooltip.SetKeyTip(button, "H1");

        RibbonTooltip.GetKeyTip(button).Should().Be("H1");
    }

    [Fact]
    public void KeyTip_OnMenuItem_AlsoSetsInputGestureText()
    {
        RunSta(() =>
        {
            var menuItem = new MenuItem();

            RibbonTooltip.SetKeyTip(menuItem, "A");

            menuItem.InputGestureText.Should().Be("A");
        });
    }

    [Fact]
    public void Title_OnFrameworkElement_SetsAutomationNameWhenMissing()
    {
        RunSta(() =>
        {
            var button = new Button();

            RibbonTooltip.SetTitle(button, "Format Painter");

            AutomationProperties.GetName(button).Should().Be("Format Painter");
        });
    }

    [Fact]
    public void Title_OnFrameworkElement_PreservesExplicitAutomationName()
    {
        RunSta(() =>
        {
            var button = new Button();
            AutomationProperties.SetName(button, "Custom Accessible Name");

            RibbonTooltip.SetTitle(button, "Format Painter");

            AutomationProperties.GetName(button).Should().Be("Custom Accessible Name");
        });
    }

    [Fact]
    public void TryOpenSubmenuForKeyTip_OpensMatchingNestedMenuItem()
    {
        RunSta(() =>
        {
            var parent = new MenuItem { Header = "Highlight Cells Rules" };
            RibbonTooltip.SetKeyTip(parent, "H");
            parent.Items.Add(new MenuItem { Header = "Greater Than..." });

            var menu = new ContextMenu();
            menu.Items.Add(parent);
            menu.IsOpen = true;

            RibbonTooltip.TryOpenSubmenuForKeyTip(menu, "H").Should().BeTrue();
            parent.IsSubmenuOpen.Should().BeTrue();
            menu.IsOpen = false;
        });
    }

    private static void RunSta(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
            throw exception;
    }
}
