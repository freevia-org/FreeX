namespace FreeX.App.Host;

public sealed record RibbonCatalog(IReadOnlyList<RibbonTabDefinition> Tabs)
{
    public IEnumerable<RibbonTabDefinition> VisibleTabs =>
        Tabs.Where(tab => !tab.IsContextual);

    public IEnumerable<RibbonTabDefinition> ContextualTabs =>
        Tabs.Where(tab => tab.IsContextual);

    public RibbonTabDefinition? FindTab(string header) =>
        Tabs.FirstOrDefault(tab => string.Equals(tab.Header, header, StringComparison.Ordinal));
}

public sealed record RibbonTabDefinition(
    string Header,
    string? Id,
    string? Name,
    string? KeyTip,
    bool IsContextual,
    IReadOnlyList<RibbonGroupDefinition> Groups)
{
    public RibbonGroupDefinition? FindGroup(string name) =>
        Groups.FirstOrDefault(group => string.Equals(group.Name, name, StringComparison.Ordinal));
}

public sealed record RibbonGroupDefinition(
    string Name,
    string? Id,
    IReadOnlyList<RibbonCommandDefinition> Commands)
{
    public RibbonCommandDefinition? FindCommand(string title) =>
        Commands.FirstOrDefault(command => string.Equals(command.Title, title, StringComparison.Ordinal));
}

public sealed record RibbonCommandDefinition(
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

public sealed record RibbonMenuItemDefinition(
    string Header,
    RibbonMenuItemKind Kind,
    string? KeyTip,
    string? InputGestureText,
    string? ClickHandler,
    string? IsEnabled,
    bool IsExplicitlyDisabled,
    IReadOnlyList<RibbonMenuItemDefinition> Children);

public readonly record struct RibbonCommandWidthHint(
    double? Width,
    double? Height,
    double? CompactFullWidth,
    double? CompactWidth);

public enum RibbonCommandKind
{
    Button,
    ToggleButton,
    ComboBox,
    CheckBox,
    Other
}

public enum RibbonMenuItemKind
{
    Command,
    Separator
}
