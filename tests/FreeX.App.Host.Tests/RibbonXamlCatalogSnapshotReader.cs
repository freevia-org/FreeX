using System.Globalization;
using System.Xml.Linq;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

internal static class RibbonXamlCatalogSnapshotReader
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Local = "clr-namespace:FreeX.App.Host";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    public static RibbonCatalog ReadMainWindow()
    {
        var path = WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml");
        return Read(path);
    }

    public static RibbonXamlCatalogSnapshot ReadMainWindowSnapshot()
    {
        var path = WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml");
        return ReadSnapshot(path);
    }

    public static RibbonCatalog Read(string path)
    {
        return ReadSnapshot(path).Catalog;
    }

    public static RibbonXamlCatalogSnapshot ReadSnapshot(string path)
    {
        var document = XDocument.Load(path, LoadOptions.SetLineInfo);
        var ribbonTabs = document
            .Descendants(Presentation + "TabControl")
            .Single(element => AttributeValue(element, Xaml + "Name") == "RibbonTabs");

        var catalog = new RibbonCatalog(
            ribbonTabs
                .Elements(Presentation + "TabItem")
                .Select(ReadTab)
                .ToArray());

        return new RibbonXamlCatalogSnapshot(
            catalog,
            document.Descendants().Attributes("Click").Count(),
            document.Descendants().Attributes("AutomationProperties.AutomationId").Count(),
            document.Descendants().Attributes(Local + "RibbonTooltip.KeyTip").Count());
    }

    private static RibbonTabDefinition ReadTab(XElement tab)
    {
        var header = AttributeValue(tab, "Header") ?? "";
        var groups = tab
            .Descendants(Presentation + "Grid")
            .Where(IsRibbonGroupPanel)
            .Select(ReadGroup)
            .ToArray();

        return new RibbonTabDefinition(
            header,
            AttributeValue(tab, Xaml + "Name"),
            AttributeValue(tab, Local + "RibbonTooltip.KeyTip"),
            string.Equals(AttributeValue(tab, "Visibility"), "Collapsed", StringComparison.Ordinal),
            groups);
    }

    private static RibbonGroupDefinition ReadGroup(XElement group)
    {
        var groupName = group
            .Descendants(Presentation + "TextBlock")
            .FirstOrDefault(IsGroupLabel)?
            .Attribute("Text")?
            .Value ?? "";

        return new RibbonGroupDefinition(
            groupName,
            group
                .Descendants()
                .Where(IsRibbonCommandElement)
                .Select(ReadCommand)
                .Where(command => !string.IsNullOrWhiteSpace(command.Title))
                .ToArray());
    }

    private static RibbonCommandDefinition ReadCommand(XElement element)
    {
        var title =
            AttributeValue(element, Local + "RibbonTooltip.Title") ??
            AttributeValue(element, "Content") ??
            AttributeValue(element, "Header") ??
            AttributeValue(element, "AutomationProperties.Name") ??
            "";

        return new RibbonCommandDefinition(
            title,
            GetCommandKind(element),
            AttributeValue(element, Xaml + "Name"),
            AttributeValue(element, Local + "RibbonTooltip.KeyTip"),
            AttributeValue(element, Local + "RibbonTooltip.Description"),
            AttributeValue(element, "Click"),
            AttributeValue(element, "AutomationProperties.Name"),
            AttributeValue(element, "IsEnabled"),
            string.Equals(AttributeValue(element, "IsEnabled"), "False", StringComparison.OrdinalIgnoreCase),
            AttributeValue(element, "Content"),
            AttributeValue(element, "Style"),
            ReadWidthHint(element),
            ReadContextMenuItems(element));
    }

    private static IReadOnlyList<RibbonMenuItemDefinition> ReadContextMenuItems(XElement element)
    {
        var contextMenu = element
            .Elements()
            .Where(child => child.Name.LocalName.EndsWith(".ContextMenu", StringComparison.Ordinal))
            .Elements(Presentation + "ContextMenu")
            .FirstOrDefault();

        return contextMenu is null ? [] : ReadMenuItems(contextMenu);
    }

    private static IReadOnlyList<RibbonMenuItemDefinition> ReadMenuItems(XElement parent) =>
        parent
            .Elements()
            .Where(element => element.Name == Presentation + "MenuItem" ||
                              element.Name == Presentation + "Separator")
            .Select(ReadMenuItem)
            .ToArray();

    private static RibbonMenuItemDefinition ReadMenuItem(XElement element)
    {
        if (element.Name == Presentation + "Separator")
        {
            return new RibbonMenuItemDefinition(
                "",
                RibbonMenuItemKind.Separator,
                null,
                null,
                null,
                null,
                false,
                []);
        }

        return new RibbonMenuItemDefinition(
            AttributeValue(element, "Header") ?? "",
            RibbonMenuItemKind.Command,
            AttributeValue(element, Local + "RibbonTooltip.KeyTip"),
            AttributeValue(element, "InputGestureText"),
            AttributeValue(element, "Click"),
            AttributeValue(element, "IsEnabled"),
            string.Equals(AttributeValue(element, "IsEnabled"), "False", StringComparison.OrdinalIgnoreCase),
            ReadMenuItems(element));
    }

    private static bool IsRibbonGroupPanel(XElement element) =>
        string.Equals(AttributeValue(element, "Style"), "{StaticResource RibbonGroupPanel}", StringComparison.Ordinal);

    private static bool IsGroupLabel(XElement element) =>
        string.Equals(AttributeValue(element, "Style"), "{StaticResource GroupLbl}", StringComparison.Ordinal);

    private static bool IsRibbonCommandElement(XElement element)
    {
        if (IsInsideContextMenu(element))
            return false;

        var kind = GetCommandKind(element);
        if (kind == RibbonCommandKind.Other)
            return false;

        return AttributeValue(element, Local + "RibbonTooltip.Title") is not null ||
               AttributeValue(element, "Click") is not null ||
               AttributeValue(element, "AutomationProperties.Name") is not null;
    }

    private static bool IsInsideContextMenu(XElement element) =>
        element
            .Ancestors()
            .Any(ancestor => ancestor.Name == Presentation + "ContextMenu");

    private static RibbonCommandKind GetCommandKind(XElement element) =>
        element.Name.LocalName switch
        {
            "AutomationInvokeButton" => RibbonCommandKind.Button,
            "Button" => RibbonCommandKind.Button,
            "ToggleButton" => RibbonCommandKind.ToggleButton,
            "ComboBox" => RibbonCommandKind.ComboBox,
            "CheckBox" => RibbonCommandKind.CheckBox,
            _ => RibbonCommandKind.Other
        };

    private static RibbonCommandWidthHint ReadWidthHint(XElement element) =>
        new(
            ParseDouble(AttributeValue(element, "Width")),
            ParseDouble(AttributeValue(element, "Height")),
            ParseDouble(AttributeValue(element, Local + "RibbonMetadata.CompactFullWidth")),
            ParseDouble(AttributeValue(element, Local + "RibbonMetadata.CompactWidth")));

    private static double? ParseDouble(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static string? AttributeValue(XElement element, XName name) =>
        element.Attribute(name)?.Value;
}

internal sealed record RibbonXamlCatalogSnapshot(
    RibbonCatalog Catalog,
    int ClickHandlerCount,
    int AutomationIdCount,
    int RibbonKeyTipCount);
