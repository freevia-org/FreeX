using System.IO;
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
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";

        var missing = document
            .Descendants()
            .Where(element => element.Attribute(local + "RibbonTooltip.Title") is not null)
            .Where(element => element.Attribute("Click")?.Value is not ("SsPinItem_Click" or "SsUnpinItem_Click"))
            .Where(element => element.Attribute(local + "RibbonTooltip.KeyTip") is null)
            .Select(element => LocalizedAttribute(element, local + "RibbonTooltip.Title") ?? element.Name.LocalName)
            .ToList();

        missing.Should().BeEmpty("visible titled ribbon controls should participate in Excel-style Alt keytip navigation");
    }

    [Fact]
    public void RibbonTabs_DoNotReuseCommandKeyTipsWithinTheSameTab()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var duplicates = document
            .Descendants(presentation + "TabItem")
            .SelectMany(tab =>
                tab.Descendants()
                    .Where(element => element.Attribute(local + "RibbonTooltip.KeyTip") is not null)
                    .Where(element => element.Name != presentation + "MenuItem")
                    .GroupBy(element => element.Attribute(local + "RibbonTooltip.KeyTip")!.Value, StringComparer.OrdinalIgnoreCase)
                    .Where(group => group.Count() > 1)
                    .Select(group => $"{LocalizedAttribute(tab, "Header") ?? "Tab"}:{group.Key}"))
            .ToList();

        duplicates.Should().BeEmpty("unique per-tab keytips are required for deterministic Excel-style command routing");
    }

    [Fact]
    public void RibbonTabs_DoNotUseCommandKeyTipPrefixesWithinTheSameTab()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var collisions = document
            .Descendants(presentation + "TabItem")
            .SelectMany(tab =>
            {
                var commands = tab.Descendants()
                    .Where(element => element.Attribute(local + "RibbonTooltip.KeyTip") is not null)
                    .Where(element => element.Name != presentation + "MenuItem")
                    .Select(element => new
                    {
                        Scope = LocalizedAttribute(tab, "Header") ?? "Tab",
                        Name = LocalizedAttribute(element, local + "RibbonTooltip.Title")
                            ?? LocalizedAttribute(element, "Content")
                            ?? LocalizedAttribute(element, "Header")
                            ?? element.Attribute("Click")?.Value
                            ?? element.Name.LocalName,
                        KeyTip = element.Attribute(local + "RibbonTooltip.KeyTip")!.Value
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
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.KeyTips.cs"));

        var prefixGuardIndex = source.IndexOf("HasVisibleTopLevelKeyTipLongerPrefix(_ribbonKeyTipSequence)", StringComparison.Ordinal);
        var topLevelRouteIndex = source.IndexOf("TryHandleTopLevelRibbonKeyTip(topLevelSequence)", StringComparison.Ordinal);

        prefixGuardIndex.Should().BeGreaterThanOrEqualTo(0);
        topLevelRouteIndex.Should().BeGreaterThanOrEqualTo(0);
        prefixGuardIndex.Should().BeLessThan(topLevelRouteIndex, "Alt, J should wait for visible JA/JD contextual tabs before selecting Draw");
    }

    [Fact]
    public void KeyedRibbonDropDowns_HaveKeyTipsForDirectMenuItems()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var missing = document
            .Descendants(presentation + "Button")
            .SelectMany(button => button
                .Descendants(presentation + "ContextMenu")
                .Elements(presentation + "MenuItem")
                .Where(menuItem => menuItem.Attribute(local + "RibbonTooltip.KeyTip") is null)
                .Select(menuItem =>
                    $"{LocalizedAttribute(button, local + "RibbonTooltip.Title")}:{LocalizedAttribute(menuItem, "Header")}"))
            .ToList();

        missing.Should().BeEmpty("audited ribbon dropdown menus should be reachable through staged Alt keytips");
    }

    [Fact]
    public void AllContextMenuCommands_HaveKeyTipsForDirectMenuItems()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var missing = document
            .Descendants(presentation + "ContextMenu")
            .Elements(presentation + "MenuItem")
            .Where(menuItem => menuItem.Attribute(local + "RibbonTooltip.KeyTip") is null)
            .Select(menuItem => LocalizedAttribute(menuItem, "Header") ?? "MenuItem")
            .ToList();

        missing.Should().BeEmpty("every command surfaced through a context menu should have deterministic keyboard access metadata");
    }

    [Fact]
    public void DirectContextMenuKeyTips_DoNotUsePrefixCollisions()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
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
                        KeyTip = item.Attribute(local + "RibbonTooltip.KeyTip")?.Value
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
