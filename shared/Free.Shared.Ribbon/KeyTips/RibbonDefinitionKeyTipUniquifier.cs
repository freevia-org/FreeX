namespace Free.Shared.Ribbon.KeyTips;

/// <summary>Normalizes keytips and makes them unique within each ribbon scope.</summary>
public static class RibbonDefinitionKeyTipUniquifier
{
    public static RibbonDefinition Normalize(RibbonDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var tabKeyTips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tabs = definition.Tabs.Select(tab =>
        {
            var tabKeyTip = MakeUnique(tab.KeyTip, tabKeyTips);
            var groupKeyTips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var controlKeyTips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var groups = tab.Groups.Select(group =>
            {
                var groupKeyTip = MakeUnique(group.KeyTip, groupKeyTips);
                var controls = group.Controls.Select(control =>
                {
                    var normalized = control switch
                    {
                        RibbonSplitButton split => split with { Menu = NormalizeMenu(split.Menu) },
                        RibbonDropdown dropdown => dropdown with { Menu = NormalizeMenu(dropdown.Menu) },
                        _ => control,
                    };
                    return normalized with
                    {
                        KeyTip = MakeUnique(normalized.KeyTip, controlKeyTips),
                    };
                }).ToArray();

                return group with { KeyTip = groupKeyTip, Controls = controls };
            }).ToArray();

            return tab with { KeyTip = tabKeyTip, Groups = groups };
        }).ToArray();

        return definition with { Tabs = tabs };
    }

    private static RibbonMenu NormalizeMenu(RibbonMenu menu) =>
        menu with { Items = NormalizeMenuItems(menu.Items) };

    private static IReadOnlyList<RibbonMenuItem> NormalizeMenuItems(
        IReadOnlyList<RibbonMenuItem> source)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return source.Select(item => item with
        {
            KeyTip = MakeUnique(item.KeyTip, used),
            Children = NormalizeMenuItems(item.Children),
        }).ToArray();
    }

    private static string? MakeUnique(string? keyTip, HashSet<string> used)
    {
        var normalized = RibbonKeyTipText.Normalize(keyTip);
        if (normalized is null || used.Add(normalized))
            return normalized ?? keyTip;

        for (var suffix = 2; ; suffix++)
        {
            var candidate = normalized.StartsWith("[[", StringComparison.Ordinal) &&
                            normalized.EndsWith("]]", StringComparison.Ordinal)
                ? $"{normalized[..^2]}{suffix}]]"
                : $"{normalized}{suffix}";
            if (used.Add(candidate))
                return candidate;
        }
    }
}
