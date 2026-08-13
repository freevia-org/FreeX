using System.Windows.Input;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowXamlKeyTipTests
{
    [Fact]
    public void TitledRibbonControls_HaveAltKeyTips()
    {
        var document = DialogSourceTestSupport.LoadHostXamlDocument("MainWindow.xaml");
        XNamespace ribbonWpf = "clr-namespace:Free.Shared.Ribbon.Wpf;assembly=Free.Shared.Ribbon.Wpf";

        var missing = document
            .Descendants()
            .Where(element => element.Attribute(ribbonWpf + "RibbonTooltip.Title") is not null)
            .Where(element => element.Attribute("Click")?.Value is not ("SsPinItem_Click" or "SsUnpinItem_Click"))
            .Where(element => element.Attribute(ribbonWpf + "RibbonTooltip.KeyTip") is null)
            .Select(element => LocalizedAttribute(element, ribbonWpf + "RibbonTooltip.Title") ?? element.Name.LocalName)
            .ToList();

        missing.Should().BeEmpty("visible titled ribbon controls should participate in Excel-style Alt keytip navigation");
    }

    [Fact]
    public void RibbonTabs_DoNotReuseCommandKeyTipsWithinTheSameTab()
    {
        var document = DialogSourceTestSupport.LoadHostXamlDocument("MainWindow.xaml");
        XNamespace ribbonWpf = "clr-namespace:Free.Shared.Ribbon.Wpf;assembly=Free.Shared.Ribbon.Wpf";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var duplicates = document
            .Descendants(presentation + "TabItem")
            .SelectMany(tab =>
                tab.Descendants()
                    .Where(element => element.Attribute(ribbonWpf + "RibbonTooltip.KeyTip") is not null)
                    .Where(element => element.Name != presentation + "MenuItem")
                    .GroupBy(element => element.Attribute(ribbonWpf + "RibbonTooltip.KeyTip")!.Value, StringComparer.OrdinalIgnoreCase)
                    .Where(group => group.Count() > 1)
                    .Select(group => $"{LocalizedAttribute(tab, "Header") ?? "Tab"}:{group.Key}"))
            .ToList();

        duplicates.Should().BeEmpty("unique per-tab keytips are required for deterministic Excel-style command routing");
    }

    [Fact]
    public void RibbonTabs_DoNotUseCommandKeyTipPrefixesWithinTheSameTab()
    {
        var document = DialogSourceTestSupport.LoadHostXamlDocument("MainWindow.xaml");
        XNamespace ribbonWpf = "clr-namespace:Free.Shared.Ribbon.Wpf;assembly=Free.Shared.Ribbon.Wpf";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var collisions = document
            .Descendants(presentation + "TabItem")
            .SelectMany(tab =>
            {
                var commands = tab.Descendants()
                    .Where(element => element.Attribute(ribbonWpf + "RibbonTooltip.KeyTip") is not null)
                    .Where(element => element.Name != presentation + "MenuItem")
                    .Select(element => new
                    {
                        Scope = LocalizedAttribute(tab, "Header") ?? "Tab",
                        Name = LocalizedAttribute(element, ribbonWpf + "RibbonTooltip.Title")
                            ?? LocalizedAttribute(element, "Content")
                            ?? LocalizedAttribute(element, "Header")
                            ?? element.Attribute("Click")?.Value
                            ?? element.Name.LocalName,
                        KeyTip = element.Attribute(ribbonWpf + "RibbonTooltip.KeyTip")!.Value
                    })
                    .ToList();

                return commands.SelectMany(command => commands
                    .Where(other => !ReferenceEquals(command, other))
                    .Where(other => other.KeyTip.StartsWith(command.KeyTip, StringComparison.OrdinalIgnoreCase))
                    .Select(other => $"{command.Scope}:{command.Name}:{command.KeyTip} prefixes {other.Name}:{other.KeyTip}"));
            })
            .ToList();

        collisions.Should().BeEmpty("command keytips in the same ribbon scope must not shadow longer sibling keytips");
    }

    [Fact]
    public void TopLevelKeyTipHandling_WaitsForVisibleContextualTabPrefixes()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.KeyTips.cs");

        var prefixGuardIndex = source.IndexOf("HasVisibleTopLevelKeyTipLongerPrefix(step.Input)", StringComparison.Ordinal);
        var topLevelRouteIndex = source.IndexOf("TryHandleTopLevelRibbonKeyTip(step.Input)", StringComparison.Ordinal);

        prefixGuardIndex.Should().BeGreaterThanOrEqualTo(0);
        topLevelRouteIndex.Should().BeGreaterThanOrEqualTo(0);
        prefixGuardIndex.Should().BeLessThan(topLevelRouteIndex, "Alt, J should wait for visible JA/JD contextual tabs before selecting Draw");
    }

    [Fact]
    public void KeyedRibbonDropDowns_HaveKeyTipsForDirectMenuItems()
    {
        var document = DialogSourceTestSupport.LoadHostXamlDocument("MainWindow.xaml");
        XNamespace ribbonWpf = "clr-namespace:Free.Shared.Ribbon.Wpf;assembly=Free.Shared.Ribbon.Wpf";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var missing = document
            .Descendants(presentation + "Button")
            .SelectMany(button => button
                .Descendants(presentation + "ContextMenu")
                .Elements(presentation + "MenuItem")
                .Where(menuItem => menuItem.Attribute(ribbonWpf + "RibbonTooltip.KeyTip") is null)
                .Select(menuItem =>
                    $"{LocalizedAttribute(button, ribbonWpf + "RibbonTooltip.Title")}:{LocalizedAttribute(menuItem, "Header")}"))
            .ToList();

        missing.Should().BeEmpty("audited ribbon dropdown menus should be reachable through staged Alt keytips");
    }

    [Fact]
    public void AllContextMenuCommands_HaveKeyTipsForDirectMenuItems()
    {
        var document = DialogSourceTestSupport.LoadHostXamlDocument("MainWindow.xaml");
        XNamespace ribbonWpf = "clr-namespace:Free.Shared.Ribbon.Wpf;assembly=Free.Shared.Ribbon.Wpf";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var missing = document
            .Descendants(presentation + "ContextMenu")
            .Elements(presentation + "MenuItem")
            .Where(menuItem => menuItem.Attribute(ribbonWpf + "RibbonTooltip.KeyTip") is null)
            .Select(menuItem => LocalizedAttribute(menuItem, "Header") ?? "MenuItem")
            .ToList();

        missing.Should().BeEmpty("every command surfaced through a context menu should have deterministic keyboard access metadata");
    }

    [Fact]
    public void DirectContextMenuKeyTips_DoNotUsePrefixCollisions()
    {
        var document = DialogSourceTestSupport.LoadHostXamlDocument("MainWindow.xaml");
        XNamespace ribbonWpf = "clr-namespace:Free.Shared.Ribbon.Wpf;assembly=Free.Shared.Ribbon.Wpf";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var collisions = document
            .Descendants(presentation + "ContextMenu")
            .SelectMany(menu =>
            {
                var directItems = menu
                    .Elements(presentation + "MenuItem")
                    .Select(item => new
                    {
                        Header = LocalizedAttribute(item, "Header") ?? "MenuItem",
                        KeyTip = item.Attribute(ribbonWpf + "RibbonTooltip.KeyTip")?.Value
                    })
                    .Where(item => !string.IsNullOrWhiteSpace(item.KeyTip))
                    .ToList();

                return directItems
                    .SelectMany(item => directItems
                        .Where(other => !ReferenceEquals(item, other))
                        .Where(other => other.KeyTip!.StartsWith(item.KeyTip!, StringComparison.OrdinalIgnoreCase))
                        .Select(other => $"{item.Header}:{item.KeyTip} prefixes {other.Header}:{other.KeyTip}"));
            })
            .ToList();

        collisions.Should().BeEmpty("leaf menu keytips must resolve without waiting for longer sibling keytips");
    }
}
