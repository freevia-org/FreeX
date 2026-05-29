using System.Windows;

namespace FreeX.App.Host;

public static class RibbonKeyTipOverlayPlacement
{
    public static Point PlaceBadge(Rect elementBounds, Size overlaySize, Size badgeSize)
    {
        var x = elementBounds.Left + (elementBounds.Width / 2) - (badgeSize.Width / 2);
        var y = elementBounds.Bottom - (badgeSize.Height / 2);

        x = Math.Round(Clamp(x, 0, Math.Max(0, overlaySize.Width - badgeSize.Width)), MidpointRounding.AwayFromZero);
        y = Math.Round(Clamp(y, 0, Math.Max(0, overlaySize.Height - badgeSize.Height)), MidpointRounding.AwayFromZero);

        return new Point(x, y);
    }

    private static double Clamp(double value, double min, double max) =>
        Math.Min(max, Math.Max(min, value));
}
