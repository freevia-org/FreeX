namespace FreeX.Ribbon;

/// <summary>
/// Resolves which tabs are visible given the active context: all normal tabs, plus any
/// contextual tab whose activation key is currently active, preserving declaration order.
/// </summary>
public static class RibbonContextResolver
{
    public static IReadOnlyList<RibbonTab> Resolve(RibbonDefinition definition, RibbonContextState state)
    {
        var result = new List<RibbonTab>();
        foreach (var tab in definition.Tabs)
        {
            if (tab.Context is null)
                result.Add(tab);
            else if (state.IsActive(tab.Context.ActivationKey))
                result.Add(tab);
        }

        return result;
    }
}
