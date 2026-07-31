namespace Free.Shared.Ribbon;

public enum RibbonPopupPlacement
{
    BelowAnchor,
}

/// <summary>
/// Shared interaction contract for transient ribbon popups. Native renderers own the popup controls,
/// but both hosts must give a collapsed group the same focus and dismissal lifecycle.
/// </summary>
public sealed record RibbonPopupInteractionContract(
    RibbonPopupPlacement Placement,
    bool FocusFirstEnabledItemOnOpen,
    bool TraverseEnabledItems,
    bool DismissOnEscape,
    bool RestoreFocusToAnchorOnClose)
{
    public static RibbonPopupInteractionContract CollapsedGroup { get; } = new(
        RibbonPopupPlacement.BelowAnchor,
        FocusFirstEnabledItemOnOpen: true,
        TraverseEnabledItems: true,
        DismissOnEscape: true,
        RestoreFocusToAnchorOnClose: true);
}

public readonly record struct RibbonPopupFocusItem(bool IsFocusable, bool IsEnabled)
{
    public bool CanReceiveFocus => IsFocusable && IsEnabled;
}

public static class RibbonPopupInteractionPlanner
{
    public static int FindFirstFocusableItem(IReadOnlyList<RibbonPopupFocusItem> items) =>
        FindFocusableItem(items, start: 0, step: 1);

    public static int FindLastFocusableItem(IReadOnlyList<RibbonPopupFocusItem> items) =>
        FindFocusableItem(items, start: items.Count - 1, step: -1);

    public static int FindAdjacentFocusableItem(
        IReadOnlyList<RibbonPopupFocusItem> items,
        int currentIndex,
        int step)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0 || currentIndex < 0 || currentIndex >= items.Count || step is not (-1 or 1))
            return -1;

        return FindFocusableItem(items, currentIndex + step, step);
    }

    private static int FindFocusableItem(
        IReadOnlyList<RibbonPopupFocusItem> items,
        int start,
        int step)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
            return -1;

        for (var offset = 0; offset < items.Count; offset++)
        {
            var index = ((start + (offset * step)) % items.Count + items.Count) % items.Count;
            if (items[index].CanReceiveFocus)
                return index;
        }

        return -1;
    }
}
