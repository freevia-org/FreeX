using System.Windows;

namespace Freexcel.App.Host;

public static class RibbonKeyTipOverlayPlacement
{
    public static Point PlaceBadge(Rect elementBounds, Size overlaySize, Size badgeSize)
    {
        var x = elementBounds.Left + (elementBounds.Width / 2) - (badgeSize.Width / 2);
        var y = elementBounds.Top + Math.Min(Math.Max(0, elementBounds.Height - (badgeSize.Height / 2)), 18);

        x = Clamp(x, 0, Math.Max(0, overlaySize.Width - badgeSize.Width));
        y = Clamp(y, 0, Math.Max(0, overlaySize.Height - badgeSize.Height));

        return new Point(x, y);
    }

    private static double Clamp(double value, double min, double max) =>
        Math.Min(max, Math.Max(min, value));
}
