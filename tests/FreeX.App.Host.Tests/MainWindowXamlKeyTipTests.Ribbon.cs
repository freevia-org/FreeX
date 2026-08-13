using System.Windows.Input;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowXamlKeyTipTests
{
    [Fact]
    public void RibbonCheckBoxCommands_HaveTooltipTitlesDescriptionsAndKeyTips()
    {
        var document = DialogSourceTestSupport.LoadHostXamlDocument("MainWindow.xaml");
        XNamespace ribbonWpf = "clr-namespace:Free.Shared.Ribbon.Wpf;assembly=Free.Shared.Ribbon.Wpf";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var missing = document
            .Descendants(presentation + "CheckBox")
            .Where(checkBox =>
                checkBox.Attribute("Click") is not null ||
                checkBox.Attribute("Checked") is not null ||
                checkBox.Attribute("Unchecked") is not null)
            .Where(checkBox =>
                checkBox.Attribute(ribbonWpf + "RibbonTooltip.Title") is null ||
                checkBox.Attribute(ribbonWpf + "RibbonTooltip.Description") is null ||
                checkBox.Attribute(ribbonWpf + "RibbonTooltip.KeyTip") is null)
            .Select(checkBox => LocalizedAttribute(checkBox, "Content") ?? checkBox.Name.LocalName)
            .ToList();

        missing.Should().BeEmpty("visible ribbon checkbox commands should expose the same Excel-style tooltip and keytip metadata as button commands");
    }

    [Fact]
    public void RibbonComboBoxCommands_HaveAccessibleNamesMatchingTooltipTitles()
    {
        var document = DialogSourceTestSupport.LoadHostXamlDocument("MainWindow.xaml");
        XNamespace ribbonWpf = "clr-namespace:Free.Shared.Ribbon.Wpf;assembly=Free.Shared.Ribbon.Wpf";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var missing = document
            .Descendants(presentation + "ComboBox")
            .Where(comboBox => comboBox.Attribute(ribbonWpf + "RibbonTooltip.Title") is not null)
            .Where(comboBox =>
                LocalizedAttribute(comboBox, "AutomationProperties.Name") !=
                LocalizedAttribute(comboBox, ribbonWpf + "RibbonTooltip.Title")!)
            .Select(comboBox => LocalizedAttribute(comboBox, ribbonWpf + "RibbonTooltip.Title")!)
            .ToList();

        missing.Should().BeEmpty("focusable ribbon combo box commands should announce the same command name shown in Excel-style tooltips");
    }

    [Fact]
    public void MainRibbon_DoesNotUseTextBlockIconPlaceholders()
    {
        var document = DialogSourceTestSupport.LoadHostXamlDocument("MainWindow.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var placeholders = document
            .Descendants(presentation + "TextBlock")
            .Where(element => element.Attribute("Tag")?.Value == "RibbonIcon")
            .Select(element => LocalizedAttribute(element, "Text") ?? "<unnamed>")
            .ToList();

        placeholders.Should().BeEmpty("the ribbon screenshot sweep should render actual SVG/vector icons, not text stand-ins");
    }

    [Fact]
    public void NestedRibbonMenuItems_HaveStagedKeyTips()
    {
        var document = DialogSourceTestSupport.LoadHostXamlDocument("MainWindow.xaml");
        XNamespace ribbonWpf = "clr-namespace:Free.Shared.Ribbon.Wpf;assembly=Free.Shared.Ribbon.Wpf";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var missing = document
            .Descendants(presentation + "MenuItem")
            .Where(menuItem => menuItem.Descendants(presentation + "MenuItem").Any())
            .SelectMany(menuItem => menuItem
                .Elements(presentation + "MenuItem")
                .Where(child => child.Attribute(ribbonWpf + "RibbonTooltip.KeyTip") is null)
                .Select(child => $"{LocalizedAttribute(menuItem, "Header")}:{LocalizedAttribute(child, "Header")}"))
            .ToList();

        missing.Should().BeEmpty("nested ribbon menu choices should be reachable through staged Alt keytips");
    }

    [Fact]
    public void RibbonMenus_DoNotReuseKeyTipsWithinTheSameMenu()
    {
        var document = DialogSourceTestSupport.LoadHostXamlDocument("MainWindow.xaml");
        XNamespace ribbonWpf = "clr-namespace:Free.Shared.Ribbon.Wpf;assembly=Free.Shared.Ribbon.Wpf";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var duplicates = document
            .Descendants(presentation + "ContextMenu")
            .Concat(document.Descendants(presentation + "MenuItem")
                .Where(menuItem => menuItem.Elements(presentation + "MenuItem").Any()))
            .SelectMany(menu =>
                menu.Elements(presentation + "MenuItem")
                    .Where(menuItem => menuItem.Attribute(ribbonWpf + "RibbonTooltip.KeyTip") is not null)
                    .GroupBy(menuItem => menuItem.Attribute(ribbonWpf + "RibbonTooltip.KeyTip")!.Value, StringComparer.OrdinalIgnoreCase)
                    .Where(group => group.Count() > 1)
                    .Select(group => $"{LocalizedAttribute(menu, "Header") ?? "ContextMenu"}:{group.Key}"))
            .ToList();

        duplicates.Should().BeEmpty("menu-level keytips must be unique for deterministic staged Alt routing");
    }

    [Fact]
    public void RibbonMenus_DoNotUseKeyTipPrefixesWithinTheSameMenu()
    {
        var document = DialogSourceTestSupport.LoadHostXamlDocument("MainWindow.xaml");
        XNamespace ribbonWpf = "clr-namespace:Free.Shared.Ribbon.Wpf;assembly=Free.Shared.Ribbon.Wpf";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var collisions = document
            .Descendants(presentation + "ContextMenu")
            .Concat(document.Descendants(presentation + "MenuItem")
                .Where(menuItem => menuItem.Elements(presentation + "MenuItem").Any()))
            .SelectMany(menu =>
            {
                var items = menu.Elements(presentation + "MenuItem")
                    .Select(item => new
                    {
                        Header = LocalizedAttribute(item, "Header") ?? item.Attribute("Click")?.Value ?? "MenuItem",
                        KeyTip = item.Attribute(ribbonWpf + "RibbonTooltip.KeyTip")?.Value
                    })
                    .Where(item => !string.IsNullOrWhiteSpace(item.KeyTip))
                    .ToList();

                return items.SelectMany(item => items
                    .Where(other => !ReferenceEquals(item, other))
                    .Where(other => other.KeyTip!.StartsWith(item.KeyTip!, StringComparison.OrdinalIgnoreCase))
                    .Select(other => $"{LocalizedAttribute(menu, "Header") ?? "ContextMenu"}:{item.Header}:{item.KeyTip} prefixes {other.Header}:{other.KeyTip}"));
            })
            .ToList();

        collisions.Should().BeEmpty("menu-level keytips must not shadow longer sibling keytips");
    }

    [Fact]
    public void ViewWindowState_UsesLocalizedLiveCommandTooltips()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ViewCommands.cs");

        source.Should().Contain("UiText.Get(\"MainWindow_TooltipDescription_OpenAnotherLiveWindowForThisWorkbook\")");
        source.Should().Contain("UiText.Get(canSwitchWindows");
        source.Should().Contain("MainWindow_TooltipDescription_SwitchToAnotherVisibleWorkbookWindow");
        source.Should().Contain("MainWindow_TooltipDescription_UnavailableSwitchWindowsRequiresSecondVisibleWindow");

        // Hide / Unhide / Reset / Side by Side / Synchronous Scrolling state uses localized live tooltips too.
        source.Should().Contain("MainWindow_TooltipDescription_HideThisWorkbookWindowFromView");
        source.Should().Contain("MainWindow_TooltipDescription_UnavailableHideRequiresSecondVisibleWindow");
        source.Should().Contain("MainWindow_TooltipDescription_RestoreAHiddenWorkbookWindow");
        source.Should().Contain("MainWindow_TooltipDescription_UnavailableUnhideRequiresAHiddenWindow");
        source.Should().Contain("MainWindow_TooltipDescription_ResetThisWindowToAStandardSizeAndPosition");
        source.Should().Contain("MainWindow_TooltipDescription_TileThisWindowAndAnotherSideBySideToCompareThem");
        source.Should().Contain("MainWindow_TooltipDescription_UnavailableViewSideBySideRequiresSecondVisibleWindow");
        source.Should().Contain("MainWindow_TooltipDescription_ScrollBothSideBySideWindowsTogether");
        source.Should().Contain("MainWindow_TooltipDescription_UnavailableSynchronousScrollingRequiresViewSideBySide");

        source.Should().NotContain("ViewWindowCommandPlanner");
        source.Should().NotContain("ViewWindowCommandBtn_Click");
    }
}
