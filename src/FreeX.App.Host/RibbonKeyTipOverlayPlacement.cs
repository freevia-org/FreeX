using System.Windows;

namespace FreeX.App.Host;

public enum RibbonKeyTipBadgeKind
{
    Command,
    Tab
}

public static class RibbonKeyTipOverlayPlacement
{
    // Excel shows tab keytips a few pixels beneath the tab label rather than
    // straddling the bottom edge as command keytips do.
    private const double TabBelowGap = 2;

    public static Point PlaceBadge(Rect elementBounds, Size overlaySize, Size badgeSize) =>
        PlaceBadge(elementBounds, overlaySize, badgeSize, RibbonKeyTipBadgeKind.Command);

    public static Point PlaceBadge(Rect elementBounds, Size overlaySize, Size badgeSize, RibbonKeyTipBadgeKind kind)
    {
        var x = elementBounds.Left + (elementBounds.Width / 2) - (badgeSize.Width / 2);
        var y = kind switch
        {
            // Tab: center horizontally, anchored just below the tab with a small gap.
            RibbonKeyTipBadgeKind.Tab => elementBounds.Bottom + TabBelowGap,
            // Command: straddle the element's bottom edge (lower-center of the control).
            _ => elementBounds.Bottom - (badgeSize.Height / 2)
        };

        x = Math.Round(Clamp(x, 0, Math.Max(0, overlaySize.Width - badgeSize.Width)), MidpointRounding.AwayFromZero);
        y = Math.Round(Clamp(y, 0, Math.Max(0, overlaySize.Height - badgeSize.Height)), MidpointRounding.AwayFromZero);

        return new Point(x, y);
    }

    private static double Clamp(double value, double min, double max) =>
        Math.Min(max, Math.Max(min, value));
}
