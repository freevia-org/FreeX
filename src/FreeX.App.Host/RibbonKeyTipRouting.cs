using System.Windows;
using System.Windows.Controls;

namespace FreeX.App.Host;

public static class RibbonKeyTipRouting
{
    public static FrameworkElement? ResolveKeyTipElement(IEnumerable<FrameworkElement> elements, string keyTip) =>
        ResolveSingle(
            elements,
            keyTip,
            preferLongerPrefix: false,
            element => NormalizeKeyTip(RibbonTooltip.GetKeyTip(element)));

    public static bool HasKeyTipPrefix(IEnumerable<FrameworkElement> elements, string keyTipPrefix) =>
        HasPrefix(elements, keyTipPrefix, element => NormalizeKeyTip(RibbonTooltip.GetKeyTip(element)));

    public static MenuItem? ResolveMenuItem(IEnumerable<MenuItem> menuItems, string keyTip) =>
        ResolveMenuItem(menuItems, keyTip, scopePrefix: null);

    public static MenuItem? ResolveMenuItem(IEnumerable<MenuItem> menuItems, string keyTip, string? scopePrefix) =>
        ResolveSingle(
            FlattenMenuItems(menuItems),
            keyTip,
            preferLongerPrefix: true,
            item => RibbonMenuKeyTipScopePlanner.GetScopedKeyTip(item, scopePrefix));

    public static bool HasMenuItemKeyTipPrefix(IEnumerable<MenuItem> menuItems, string keyTipPrefix) =>
        HasMenuItemKeyTipPrefix(menuItems, keyTipPrefix, scopePrefix: null);

    public static bool HasMenuItemKeyTipPrefix(IEnumerable<MenuItem> menuItems, string keyTipPrefix, string? scopePrefix) =>
        HasPrefix(
            FlattenMenuItems(menuItems),
            keyTipPrefix,
            item => RibbonMenuKeyTipScopePlanner.GetScopedKeyTip(item, scopePrefix));

    private static T? ResolveSingle<T>(
        IEnumerable<T> elements,
        string keyTip,
        bool preferLongerPrefix,
        Func<T, string?> keyTipSelector)
        where T : DependencyObject
    {
        if (string.IsNullOrWhiteSpace(keyTip))
            return null;

        var normalizedKeyTip = keyTip.Trim();
        var candidates = elements.ToList();
        var matches = candidates
            .Where(element => string.Equals(keyTipSelector(element), normalizedKeyTip, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToList();

        if (matches.Count == 1 && !preferLongerPrefix)
            return matches[0];

        var longerMatchExists = candidates.Any(element =>
            keyTipSelector(element) is { } candidate &&
            candidate.Length > normalizedKeyTip.Length &&
            candidate.StartsWith(normalizedKeyTip, StringComparison.OrdinalIgnoreCase));

        if (longerMatchExists)
            return null;

        return matches.Count == 1 ? matches[0] : null;
    }

    private static bool HasPrefix<T>(IEnumerable<T> elements, string keyTipPrefix, Func<T, string?> keyTipSelector)
        where T : DependencyObject
    {
        if (string.IsNullOrWhiteSpace(keyTipPrefix))
            return false;

        var normalizedPrefix = keyTipPrefix.Trim();
        return elements.Any(element =>
            keyTipSelector(element) is { } keyTip &&
            keyTip.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase));
    }

    private static string? NormalizeKeyTip(string? keyTip) =>
        string.IsNullOrWhiteSpace(keyTip) ? null : keyTip.Trim();

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
