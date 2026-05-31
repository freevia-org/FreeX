using System.Windows;

namespace FreeX.App.Host;

/// <summary>
/// Pure geometry for "View Side by Side": splits the work area into two equal, non-overlapping
/// left/right halves so two workbook windows can be compared together. WPF-free so it can be
/// unit-tested without standing up windows.
/// </summary>
public static class SideBySideLayoutPlanner
{
    /// <summary>Fallback width used when the work area is non-positive/unknown.</summary>
    public const double FallbackWidth = 1024;

    /// <summary>Fallback height used when the work area is non-positive/unknown.</summary>
    public const double FallbackHeight = 768;

    /// <summary>
    /// Returns the bounds for the primary (left) and secondary (right) windows. The two halves
    /// abut without a gap or overlap and together cover the full work-area width.
    /// </summary>
    public static (Rect Primary, Rect Secondary) Tile(double workAreaWidth, double workAreaHeight)
    {
        var width = workAreaWidth > 0 ? workAreaWidth : FallbackWidth;
        var height = workAreaHeight > 0 ? workAreaHeight : FallbackHeight;

        var halfWidth = width / 2;
        var primary = new Rect(0, 0, halfWidth, height);
        var secondary = new Rect(halfWidth, 0, width - halfWidth, height);
        return (primary, secondary);
    }
}
