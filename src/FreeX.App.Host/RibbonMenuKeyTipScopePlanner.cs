using System.Windows;
using System.Windows.Controls;
using Free.Shared.Ribbon.KeyTips;

namespace FreeX.App.Host;

public static class RibbonMenuKeyTipScopePlanner
{
    public static string? GetScopePrefix(ItemsControl scopeOwner) =>
        scopeOwner is MenuItem menuItem
            ? RibbonKeyTipText.Normalize(RibbonTooltip.GetKeyTip(menuItem))
            : null;

    public static string? GetScopedKeyTip(DependencyObject element, ItemsControl scopeOwner) =>
        GetScopedKeyTip(element, GetScopePrefix(scopeOwner));

    public static string? GetScopedKeyTip(DependencyObject element, string? scopePrefix)
        => RibbonKeyTipText.ApplyScopePrefix(RibbonTooltip.GetKeyTip(element), scopePrefix);

    public static void ApplyScopedInputGestureText(ItemsControl scopeOwner)
    {
        var scopePrefix = GetScopePrefix(scopeOwner);
        foreach (var item in EnumerateMenuItems(scopeOwner))
            item.InputGestureText = GetScopedKeyTip(item, scopePrefix) ?? "";
    }

    public static void ClearInputGestureText(ItemsControl scopeOwner)
    {
        foreach (var item in EnumerateMenuItems(scopeOwner))
            item.InputGestureText = string.Empty;
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

}
