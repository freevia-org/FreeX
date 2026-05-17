using System.Windows;
using System.Windows.Controls;

namespace Freexcel.App.Host;

public static class RibbonKeyTipRouting
{
    public static FrameworkElement? ResolveKeyTipElement(IEnumerable<FrameworkElement> elements, string keyTip) =>
        ResolveSingle(elements, keyTip, preferLongerPrefix: false);

    public static bool HasKeyTipPrefix(IEnumerable<FrameworkElement> elements, string keyTipPrefix) =>
        HasPrefix(elements, keyTipPrefix);

    public static MenuItem? ResolveMenuItem(IEnumerable<MenuItem> menuItems, string keyTip) =>
        ResolveSingle(FlattenMenuItems(menuItems), keyTip, preferLongerPrefix: true);

    public static bool HasMenuItemKeyTipPrefix(IEnumerable<MenuItem> menuItems, string keyTipPrefix) =>
        HasPrefix(FlattenMenuItems(menuItems), keyTipPrefix);

    private static T? ResolveSingle<T>(IEnumerable<T> elements, string keyTip, bool preferLongerPrefix)
        where T : DependencyObject
    {
        var candidates = elements.ToList();
        var matches = candidates
            .Where(element => string.Equals(RibbonTooltip.GetKeyTip(element), keyTip, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToList();

        if (matches.Count == 1 && !preferLongerPrefix)
            return matches[0];

        var longerMatchExists = candidates.Any(element =>
            RibbonTooltip.GetKeyTip(element) is { } candidate &&
            candidate.Length > keyTip.Length &&
            candidate.StartsWith(keyTip, StringComparison.OrdinalIgnoreCase));

        if (longerMatchExists)
            return null;

        return matches.Count == 1 ? matches[0] : null;
    }

    private static bool HasPrefix<T>(IEnumerable<T> elements, string keyTipPrefix)
        where T : DependencyObject =>
        elements.Any(element =>
            RibbonTooltip.GetKeyTip(element) is { } keyTip &&
            keyTip.StartsWith(keyTipPrefix, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<MenuItem> FlattenMenuItems(IEnumerable<MenuItem> menuItems)
    {
        foreach (var menuItem in menuItems)
        {
            yield return menuItem;

            foreach (var child in menuItem.Items.OfType<MenuItem>())
            {
                foreach (var descendant in FlattenMenuItems([child]))
                    yield return descendant;
            }
        }
    }
}
