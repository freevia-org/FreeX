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
        if (string.IsNullOrWhiteSpace(keyTip))
            return null;

        var normalizedKeyTip = keyTip.Trim();
        var candidates = elements.ToList();
        var matches = candidates
            .Where(element => string.Equals(NormalizeKeyTip(RibbonTooltip.GetKeyTip(element)), normalizedKeyTip, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToList();

        if (matches.Count == 1 && !preferLongerPrefix)
            return matches[0];

        var longerMatchExists = candidates.Any(element =>
            NormalizeKeyTip(RibbonTooltip.GetKeyTip(element)) is { } candidate &&
            candidate.Length > normalizedKeyTip.Length &&
            candidate.StartsWith(normalizedKeyTip, StringComparison.OrdinalIgnoreCase));

        if (longerMatchExists)
            return null;

        return matches.Count == 1 ? matches[0] : null;
    }

    private static bool HasPrefix<T>(IEnumerable<T> elements, string keyTipPrefix)
        where T : DependencyObject
    {
        if (string.IsNullOrWhiteSpace(keyTipPrefix))
            return false;

        var normalizedPrefix = keyTipPrefix.Trim();
        return elements.Any(element =>
            NormalizeKeyTip(RibbonTooltip.GetKeyTip(element)) is { } keyTip &&
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
