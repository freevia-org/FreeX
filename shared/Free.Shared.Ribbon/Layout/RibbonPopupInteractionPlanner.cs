namespace Free.Shared.Ribbon;

public enum RibbonPopupPlacement
{
    BelowAnchor,
    AboveAnchor,
}

public enum RibbonPopupDismissKey
{
    Escape,
    Left,
}

public enum RibbonPopupDismissal
{
    None,
    CloseSubmenu,
    ClosePopup,
}

public enum RibbonPopupNavigationKey
{
    Right,
}

public enum RibbonPopupNavigation
{
    None,
    OpenSubmenu,
}

/// <summary>Neutral submenu behavior shared by the WPF and Avalonia menu controls.</summary>
public sealed record RibbonPopupSubmenuContract(
    bool FocusFirstEnabledItemOnOpen,
    bool TraverseEnabledItems,
    bool DismissOnEscape,
    bool DismissOnLeft,
    bool RestoreFocusToParentOnClose,
    bool RepositionAtScreenEdge,
    double AnchorGap)
{
    public bool OpenOnRight { get; init; } = true;

    public static RibbonPopupSubmenuContract Default { get; } = new(
        FocusFirstEnabledItemOnOpen: true,
        TraverseEnabledItems: true,
        DismissOnEscape: true,
        DismissOnLeft: true,
        RestoreFocusToParentOnClose: true,
        RepositionAtScreenEdge: true,
        AnchorGap: RibbonVisualMetrics.PopupChrome.Submenu.AnchorGap);
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
    public bool DismissOnLeft { get; init; } = true;
    public RibbonPopupSubmenuContract Submenu { get; init; } = RibbonPopupSubmenuContract.Default;

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
/// Describes one monitor in device pixels while keeping the work area in the normalized popup
/// coordinate space used by the shared placement planner.
/// </summary>
public readonly record struct RibbonPopupMonitorWorkArea(
    RibbonPopupRect DeviceBounds,
    RibbonPopupRect WorkArea);

public static class RibbonPopupMonitorPlanner
{
    public static RibbonPopupRect SelectWorkArea(
        RibbonPopupRect anchorDeviceRect,
        IReadOnlyList<RibbonPopupMonitorWorkArea> monitors,
        RibbonPopupRect fallbackWorkArea)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        if (monitors.Count == 0)
            return fallbackWorkArea;

        var point = new RibbonPopupPoint(
            anchorDeviceRect.X + anchorDeviceRect.Width / 2,
            anchorDeviceRect.Y + anchorDeviceRect.Height / 2);
        var containing = monitors.FirstOrDefault(m => Contains(m.DeviceBounds, point));
        if (containing.DeviceBounds.Width > 0 && containing.DeviceBounds.Height > 0)
            return containing.WorkArea;

        var nearest = monitors
            .OrderBy(m => DistanceSquared(m.DeviceBounds, point))
            .First();
        return nearest.WorkArea;
    }

    public static RibbonPopupRect NormalizeFromDevicePixels(
        RibbonPopupRect deviceRect,
        RibbonPopupPoint deviceOrigin,
        RibbonPopupPoint dipOrigin,
        double scaleX,
        double scaleY)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scaleX);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scaleY);
        return new RibbonPopupRect(
            dipOrigin.X + (deviceRect.X - deviceOrigin.X) / scaleX,
            dipOrigin.Y + (deviceRect.Y - deviceOrigin.Y) / scaleY,
            deviceRect.Width / scaleX,
            deviceRect.Height / scaleY);
    }

    private static bool Contains(RibbonPopupRect rect, RibbonPopupPoint point) =>
        point.X >= rect.X && point.X < rect.X + rect.Width &&
        point.Y >= rect.Y && point.Y < rect.Y + rect.Height;

    private static double DistanceSquared(RibbonPopupRect rect, RibbonPopupPoint point)
    {
        var dx = point.X < rect.X ? rect.X - point.X : point.X > rect.X + rect.Width ? point.X - (rect.X + rect.Width) : 0;
        var dy = point.Y < rect.Y ? rect.Y - point.Y : point.Y > rect.Y + rect.Height ? point.Y - (rect.Y + rect.Height) : 0;
        return (dx * dx) + (dy * dy);
    }
}

public readonly record struct RibbonPopupPoint(double X, double Y);

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
    public static RibbonPopupNavigation PlanNavigation(
        RibbonPopupNavigationKey key,
        bool hasChildren,
        RibbonPopupInteractionContract? contract = null)
    {
        contract ??= RibbonPopupInteractionContract.CollapsedGroup;
        return key == RibbonPopupNavigationKey.Right && hasChildren && contract.Submenu.OpenOnRight
            ? RibbonPopupNavigation.OpenSubmenu
            : RibbonPopupNavigation.None;
    }

    public static RibbonPopupDismissal PlanDismissal(
        RibbonPopupDismissKey key,
        bool isNestedSubmenu,
        RibbonPopupInteractionContract? contract = null)
    {
        contract ??= RibbonPopupInteractionContract.CollapsedGroup;
        var submenu = contract.Submenu;
        var enabled = isNestedSubmenu
            ? key == RibbonPopupDismissKey.Escape ? submenu.DismissOnEscape : submenu.DismissOnLeft
            : key == RibbonPopupDismissKey.Escape ? contract.DismissOnEscape : contract.DismissOnLeft;
        if (!enabled)
            return RibbonPopupDismissal.None;

        return isNestedSubmenu
            ? RibbonPopupDismissal.CloseSubmenu
            : RibbonPopupDismissal.ClosePopup;
    }

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
