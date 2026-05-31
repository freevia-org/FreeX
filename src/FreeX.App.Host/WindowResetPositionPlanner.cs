using System;
using System.Windows;

namespace FreeX.App.Host;

/// <summary>
/// Pure geometry for "Reset Window Position": computes a standard, work-area-relative
/// size and position for a workbook window, cascading later windows down-and-right and
/// wrapping so a window is never pushed off the right or bottom of the work area.
/// WPF-free so it can be unit-tested without standing up a window.
/// </summary>
public static class WindowResetPositionPlanner
{
    /// <summary>Standard window size as a fraction of the work area.</summary>
    public const double StandardSizeFraction = 0.75;

    /// <summary>Down-and-right offset applied per window index when cascading.</summary>
    public const double CascadeOffset = 24;

    /// <summary>Fallback width used when the work area is non-positive/unknown.</summary>
    public const double FallbackWidth = 1024;

    /// <summary>Fallback height used when the work area is non-positive/unknown.</summary>
    public const double FallbackHeight = 768;

    public static Rect Compute(double workAreaWidth, double workAreaHeight, int windowIndex)
    {
        if (workAreaWidth <= 0 || workAreaHeight <= 0)
            return new Rect(0, 0, FallbackWidth, FallbackHeight);

        var width = workAreaWidth * StandardSizeFraction;
        var height = workAreaHeight * StandardSizeFraction;
        var index = Math.Max(0, windowIndex);

        var left = CascadeWithinSlack(workAreaWidth - width, index);
        var top = CascadeWithinSlack(workAreaHeight - height, index);

        return new Rect(left, top, width, height);
    }

    // Centered for index 0, then cascaded by index*offset, wrapped to stay within [0, slack)
    // so the window never leaves the work area on the right/bottom edge.
    private static double CascadeWithinSlack(double slack, int index)
    {
        if (slack <= 0)
            return 0;

        var centered = slack / 2;
        return (centered + index * CascadeOffset) % slack;
    }
}
