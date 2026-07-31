namespace Free.Shared.Ribbon;

public enum RibbonPopupPlacement
{
    BelowAnchor,
    AboveAnchor,
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
    bool RestoreFocusToAnchorOnClose,
    bool RepositionAtScreenEdge,
    double AnchorGap)
{
    public static RibbonPopupInteractionContract CollapsedGroup { get; } = new(
        RibbonPopupPlacement.BelowAnchor,
        FocusFirstEnabledItemOnOpen: true,
        TraverseEnabledItems: true,
        DismissOnEscape: true,
        RestoreFocusToAnchorOnClose: true,
        RepositionAtScreenEdge: true,
        AnchorGap: RibbonVisualMetrics.PopupChrome.AnchorGap);
}

public readonly record struct RibbonPopupRect(double X, double Y, double Width, double Height);

public readonly record struct RibbonPopupPlacementResult(
    RibbonPopupPlacement Placement,
    double X,
    double Y);

/// <summary>
/// Computes a stable left-aligned popup position and flips above the anchor when the preferred
/// below-anchor position cannot fit. Native hosts can use the same result directly or opt into their
/// toolkit's equivalent edge constraint adjustment when the screen work area is not exposed.
/// </summary>
public static class RibbonPopupPlacementPlanner
{
    public static RibbonPopupPlacementResult Plan(
        RibbonPopupRect anchor,
        RibbonPopupRect popup,
        RibbonPopupRect workArea,
        RibbonPopupInteractionContract? contract = null)
    {
        contract ??= RibbonPopupInteractionContract.CollapsedGroup;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(popup.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(popup.Height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workArea.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workArea.Height);

        var x = Clamp(anchor.X, workArea.X, workArea.X + workArea.Width - popup.Width);
        var belowY = anchor.Y + anchor.Height + contract.AnchorGap;
        var aboveY = anchor.Y - popup.Height - contract.AnchorGap;
        var placement = contract.Placement;
        var y = placement == RibbonPopupPlacement.AboveAnchor ? aboveY : belowY;

        if (contract.RepositionAtScreenEdge && placement == RibbonPopupPlacement.BelowAnchor &&
            y + popup.Height > workArea.Y + workArea.Height && aboveY >= workArea.Y)
        {
            placement = RibbonPopupPlacement.AboveAnchor;
            y = aboveY;
        }
        else if (contract.RepositionAtScreenEdge && placement == RibbonPopupPlacement.AboveAnchor &&
                 y < workArea.Y && belowY + popup.Height <= workArea.Y + workArea.Height)
        {
            placement = RibbonPopupPlacement.BelowAnchor;
            y = belowY;
        }

        y = Clamp(y, workArea.Y, workArea.Y + workArea.Height - popup.Height);
        return new RibbonPopupPlacementResult(placement, x, y);
    }

    private static double Clamp(double value, double minimum, double maximum) =>
        Math.Min(Math.Max(value, minimum), Math.Max(minimum, maximum));
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
