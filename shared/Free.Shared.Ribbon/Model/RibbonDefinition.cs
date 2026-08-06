namespace Free.Shared.Ribbon;

public enum RibbonContextColor { None, Green, Orange, Purple, Blue, Red, Teal }

/// <summary>Marks a tab as contextual: shown only while <see cref="ActivationKey"/> is active.</summary>
public sealed record RibbonTabContext(
    string ActivationKey,
    string Label,
    RibbonContextColor Color,
    string? KeyTip = null,
    int DisplayOrder = 100);

public sealed record RibbonTab(
    string Id,
    string Header,
    string? KeyTip,
    RibbonTabContext? Context,
    IReadOnlyList<RibbonGroup> Groups)
{
    public bool IsContextual => Context is not null;

    public RibbonGroup? FindGroup(string id)
    {
        foreach (var group in Groups)
            if (string.Equals(group.Id, id, StringComparison.Ordinal))
                return group;
        return null;
    }
}

public sealed record RibbonDefinition(IReadOnlyList<RibbonTab> Tabs)
{
    public IEnumerable<RibbonTab> VisibleTabs => Tabs.Where(t => !t.IsContextual);
    public IEnumerable<RibbonTab> ContextualTabs => Tabs.Where(t => t.IsContextual);

    public RibbonTab? FindTab(string id)
    {
        foreach (var tab in Tabs)
            if (string.Equals(tab.Id, id, StringComparison.Ordinal))
                return tab;
        return null;
    }
}
