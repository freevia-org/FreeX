namespace FreeP.App.Compositor;

/// <summary>
/// Native operations required to project a portable domain context-menu plan. Renderers own
/// toolkit objects; recursion, separator placement, submenu structure, and leaf execution remain
/// shared.
/// </summary>
public sealed record PresentationDomainContextMenuNativeBindings<TMenu, TItem>(
    Func<PresentationDomainContextMenuEntryPlan, TItem> CreateItem,
    Action<TMenu> AddRootSeparator,
    Action<TMenu, TItem> AddRootItem,
    Action<TItem> AddChildSeparator,
    Action<TItem, TItem> AddChildItem,
    Action<TItem, Action> BindExecute);

public static class PresentationDomainContextMenuNativeAdapter
{
    public static void Populate<TMenu, TItem>(
        PresentationDomainContextMenuPlan plan,
        TMenu menu,
        PresentationDomainContextMenuNativeBindings<TMenu, TItem> bindings,
        Action<PresentationDomainContextAction> execute)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(execute);

        foreach (var entry in plan.Entries)
        {
            if (entry.Kind == PresentationDomainContextMenuEntryKind.Separator)
            {
                bindings.AddRootSeparator(menu);
                continue;
            }

            var item = BuildItem(entry, bindings, execute);
            bindings.AddRootItem(menu, item);
        }
    }

    private static TItem BuildItem<TMenu, TItem>(
        PresentationDomainContextMenuEntryPlan entry,
        PresentationDomainContextMenuNativeBindings<TMenu, TItem> bindings,
        Action<PresentationDomainContextAction> execute)
    {
        var item = bindings.CreateItem(entry);
        if (entry.Children is { Count: > 0 })
        {
            foreach (var child in entry.Children)
            {
                if (child.Kind == PresentationDomainContextMenuEntryKind.Separator)
                    bindings.AddChildSeparator(item);
                else
                    bindings.AddChildItem(item, BuildItem(child, bindings, execute));
            }
        }
        else if (entry.Action is { } action)
        {
            bindings.BindExecute(item, () => execute(action));
        }

        return item;
    }
}
