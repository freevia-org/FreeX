using System.Xml.Linq;
using FreeX.Ribbon.Definitions;
using SharedRibbon = Free.Shared.Ribbon;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Builds the <see cref="RibbonCatalog"/> the ribbon tests assert against from the declarative single-source
/// ribbon definition (<see cref="FreeXRibbon.Build"/>) — the same model the live <c>RibbonWpfRenderer</c>
/// renders. It used to parse the hand-authored ribbon out of <c>MainWindow.xaml</c>, but the
/// XAML→declarative cutover stripped that markup, so the catalog now comes straight from the definition.
/// </summary>
internal static class RibbonXamlCatalogSnapshotReader
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace RibbonWpf =
        "clr-namespace:Free.Shared.Ribbon.Wpf;assembly=Free.Shared.Ribbon.Wpf";

    public static RibbonCatalog ReadMainWindow() => BuildCatalog(FreeXRibbon.Build());

    /// <summary>
    /// The top-level ribbon tab shells (header, key tip, contextual visibility) exactly as the live
    /// keytip router sees them: read from the <c>RibbonTabs</c> <c>TabItem</c>s in <c>MainWindow.xaml</c>.
    /// Those tab headers survived the declarative cutover (only the group <em>content</em> was stripped),
    /// and unlike the declarative catalog they still carry the File tab and the contextual J-prefixed key
    /// tips that <see cref="RibbonTopLevelKeyTipRouter"/> routes. Groups are intentionally empty here.
    /// </summary>
    public static IReadOnlyList<RibbonTabDefinition> ReadMainWindowTabShells()
    {
        var path = DialogSourceTestSupport.FindHostSourceFile("MainWindow.xaml");
        var document = XDocument.Load(path);
        var ribbonTabs = document
            .Descendants(Presentation + "TabControl")
            .Single(element => (string?)element.Attribute(Xaml + "Name") == "RibbonTabs");

        return ribbonTabs
            .Elements(Presentation + "TabItem")
            .Select(tab => new RibbonTabDefinition(
                LocalizedXamlTestSupport.ResolveLocalizedValue((string?)tab.Attribute("Header")) ?? "",
                (string?)tab.Attribute(RibbonWpf + "RibbonMetadata.CatalogId"),
                (string?)tab.Attribute(Xaml + "Name"),
                (string?)tab.Attribute(RibbonWpf + "RibbonTooltip.KeyTip"),
                string.Equals((string?)tab.Attribute("Visibility"), "Collapsed", StringComparison.Ordinal),
                []))
            .ToArray();
    }

    public static RibbonXamlCatalogSnapshot ReadMainWindowSnapshot()
    {
        // The catalog comes from the declarative model, but these counts track literal MainWindow.xaml
        // markup (their inventory notes describe exactly that), so count the raw attributes there. After
        // the declarative cutover the residual XAML wiring is the backstage/File surface and chrome.
        var path = DialogSourceTestSupport.FindHostSourceFile("MainWindow.xaml");
        var document = XDocument.Load(path);

        return new RibbonXamlCatalogSnapshot(
            BuildCatalog(FreeXRibbon.Build()),
            document.Descendants().Attributes("Click").Count(),
            document.Descendants().Attributes("AutomationProperties.AutomationId").Count(),
            document.Descendants().Attributes(RibbonWpf + "RibbonTooltip.KeyTip").Count());
    }

    private static RibbonCatalog BuildCatalog(SharedRibbon.RibbonDefinition definition) =>
        new(definition.Tabs.Select(ConvertTab).ToArray());

    private static RibbonTabDefinition ConvertTab(SharedRibbon.RibbonTab tab) =>
        new(
            tab.Header,
            tab.Id,
            tab.Id,
            tab.KeyTip,
            tab.IsContextual,
            tab.Groups.Select(ConvertGroup).ToArray());

    private static RibbonGroupDefinition ConvertGroup(SharedRibbon.RibbonGroup group) =>
        new(
            group.Header,
            group.Id,
            group.Controls
                .Where(IsCommandControl)
                .Select(ConvertCommand)
                .Where(command => !string.IsNullOrWhiteSpace(command.Title))
                .ToArray());

    private static bool IsCommandControl(SharedRibbon.RibbonControl control) =>
        control is not (SharedRibbon.RibbonSeparator or SharedRibbon.RibbonRowBreak) &&
        !string.IsNullOrWhiteSpace(control.Label);

    private static RibbonCommandDefinition ConvertCommand(SharedRibbon.RibbonControl control)
    {
        // A declarative CommandId may encode its legacy click handler as "Name#HandlerName_Click"
        // (the renderer routes these to the existing MainWindow handlers); split the two apart.
        var rawId = control.CommandId.Value;
        var hashIndex = rawId.IndexOf('#');
        var commandName = hashIndex >= 0 ? rawId[..hashIndex] : rawId;
        var clickHandler = hashIndex >= 0 ? rawId[(hashIndex + 1)..] : null;
        var width = control is SharedRibbon.RibbonComboBox combo ? combo.Width : null;

        return new RibbonCommandDefinition(
            control.Label,
            MapKind(control),
            string.IsNullOrEmpty(commandName) ? null : commandName,
            control.KeyTip,
            control.TooltipDescription,
            string.IsNullOrEmpty(clickHandler) ? null : clickHandler,
            control.Label,
            IsEnabled: null,
            IsExplicitlyDisabled: false,
            control.Label,
            Style: null,
            new RibbonCommandWidthHint(width, null, null, null),
            ConvertMenu(control));
    }

    private static RibbonCommandKind MapKind(SharedRibbon.RibbonControl control) =>
        control switch
        {
            SharedRibbon.RibbonToggleButton => RibbonCommandKind.ToggleButton,
            SharedRibbon.RibbonCheckBox => RibbonCommandKind.CheckBox,
            SharedRibbon.RibbonComboBox => RibbonCommandKind.ComboBox,
            SharedRibbon.RibbonLabel => RibbonCommandKind.Other,
            SharedRibbon.RibbonButton or SharedRibbon.RibbonSplitButton or
                SharedRibbon.RibbonDropdown or SharedRibbon.RibbonGallery => RibbonCommandKind.Button,
            _ => RibbonCommandKind.Other
        };

    private static IReadOnlyList<RibbonMenuItemDefinition> ConvertMenu(SharedRibbon.RibbonControl control) =>
        control switch
        {
            SharedRibbon.RibbonSplitButton split => ConvertMenuItems(split.Menu),
            SharedRibbon.RibbonDropdown dropdown => ConvertMenuItems(dropdown.Menu),
            _ => []
        };

    private static IReadOnlyList<RibbonMenuItemDefinition> ConvertMenuItems(SharedRibbon.RibbonMenu menu) =>
        menu.Items.Select(ConvertMenuItem).ToArray();

    private static RibbonMenuItemDefinition ConvertMenuItem(SharedRibbon.RibbonMenuItem item) =>
        new(
            item.Header,
            item.Kind == SharedRibbon.RibbonMenuItemKind.Separator
                ? RibbonMenuItemKind.Separator
                : RibbonMenuItemKind.Command,
            item.KeyTip,
            item.InputGesture,
            ClickHandler: null,
            IsEnabled: item.IsEnabled ? null : "False",
            IsExplicitlyDisabled: !item.IsEnabled,
            item.Children.Select(ConvertMenuItem).ToArray());
}

internal sealed record RibbonXamlCatalogSnapshot(
    RibbonCatalog Catalog,
    int ClickHandlerCount,
    int AutomationIdCount,
    int RibbonKeyTipCount);

internal sealed record RibbonCatalog(IReadOnlyList<RibbonTabDefinition> Tabs)
{
    public IEnumerable<RibbonTabDefinition> VisibleTabs =>
        Tabs.Where(tab => !tab.IsContextual);

    public IEnumerable<RibbonTabDefinition> ContextualTabs =>
        Tabs.Where(tab => tab.IsContextual);

    public RibbonTabDefinition? FindTab(string header)
    {
        foreach (var tab in Tabs)
            if (string.Equals(tab.Header, header, StringComparison.Ordinal))
                return tab;

        return null;
    }
}

internal sealed record RibbonTabDefinition(
    string Header,
    string? Id,
    string? Name,
    string? KeyTip,
    bool IsContextual,
    IReadOnlyList<RibbonGroupDefinition> Groups)
{
    public RibbonGroupDefinition? FindGroup(string name)
    {
        foreach (var group in Groups)
            if (string.Equals(group.Name, name, StringComparison.Ordinal))
                return group;

        return null;
    }
}

internal sealed record RibbonGroupDefinition(
    string Name,
    string? Id,
    IReadOnlyList<RibbonCommandDefinition> Commands)
{
    public RibbonCommandDefinition? FindCommand(string title)
    {
        foreach (var command in Commands)
            if (string.Equals(command.Title, title, StringComparison.Ordinal))
                return command;

        return null;
    }
}

internal sealed record RibbonCommandDefinition(
    string Title,
    RibbonCommandKind Kind,
    string? Name,
    string? KeyTip,
    string? Description,
    string? ClickHandler,
    string? AutomationName,
    string? IsEnabled,
    bool IsExplicitlyDisabled,
    string? Content,
    string? Style,
    RibbonCommandWidthHint WidthHint,
    IReadOnlyList<RibbonMenuItemDefinition> MenuItems)
{
    public IEnumerable<RibbonMenuItemDefinition> DescendantMenuItems =>
        MenuItems.SelectMany(EnumerateMenuItem);

    private static IEnumerable<RibbonMenuItemDefinition> EnumerateMenuItem(RibbonMenuItemDefinition item)
    {
        yield return item;

        foreach (var child in item.Children.SelectMany(EnumerateMenuItem))
            yield return child;
    }
}

internal sealed record RibbonMenuItemDefinition(
    string Header,
    RibbonMenuItemKind Kind,
    string? KeyTip,
    string? InputGestureText,
    string? ClickHandler,
    string? IsEnabled,
    bool IsExplicitlyDisabled,
    IReadOnlyList<RibbonMenuItemDefinition> Children);

internal readonly record struct RibbonCommandWidthHint(
    double? Width,
    double? Height,
    double? CompactFullWidth,
    double? CompactWidth);

internal enum RibbonCommandKind
{
    Button,
    ToggleButton,
    ComboBox,
    CheckBox,
    Other
}

internal enum RibbonMenuItemKind
{
    Command,
    Separator
}
