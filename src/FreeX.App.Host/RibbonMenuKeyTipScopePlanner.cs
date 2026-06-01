using System.Windows;
using System.Windows.Controls;

namespace FreeX.App.Host;

public static class RibbonMenuKeyTipScopePlanner
{
    public static string? GetScopePrefix(ItemsControl scopeOwner) =>
        scopeOwner is MenuItem menuItem
            ? NormalizeKeyTip(RibbonTooltip.GetKeyTip(menuItem))
            : null;

    public static string? GetScopedKeyTip(DependencyObject element, ItemsControl scopeOwner) =>
        GetScopedKeyTip(element, GetScopePrefix(scopeOwner));

    public static string? GetScopedKeyTip(DependencyObject element, string? scopePrefix)
    {
        var keyTip = NormalizeKeyTip(RibbonTooltip.GetKeyTip(element));
        if (keyTip is null)
            return null;

        var prefix = NormalizeKeyTip(scopePrefix);
        if (prefix is { Length: > 0 } &&
            keyTip.Length > prefix.Length &&
            keyTip.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return keyTip[prefix.Length..];
        }

        return keyTip;
    }

    public static void ApplyScopedInputGestureText(ItemsControl scopeOwner)
    {
        var scopePrefix = GetScopePrefix(scopeOwner);
        foreach (var item in EnumerateMenuItems(scopeOwner))
            item.InputGestureText = GetScopedKeyTip(item, scopePrefix) ?? "";
    }

    private static IEnumerable<MenuItem> EnumerateMenuItems(ItemsControl scopeOwner)
    {
        foreach (var item in scopeOwner.Items.OfType<MenuItem>())
        {
            yield return item;

            foreach (var child in EnumerateMenuItems(item))
                yield return child;
        }
    }

    private static string? NormalizeKeyTip(string? keyTip) =>
        string.IsNullOrWhiteSpace(keyTip) ? null : keyTip.Trim().ToUpperInvariant();
}
