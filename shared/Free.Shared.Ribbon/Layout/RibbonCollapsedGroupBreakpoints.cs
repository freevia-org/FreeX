namespace Free.Shared.Ribbon;

/// <summary>
/// Pure width-breakpoint math for collapsed ribbon groups, shared by the layout engine and
/// tab profiles. The WPF-specific footprint (margins, padding, caption visibility) stays in the
/// renderer; only these resolution-independent thresholds and planned widths live in the core.
/// </summary>
public static class RibbonCollapsedGroupBreakpoints
{
    /// <summary>Widths at which the collapsed-group footprint changes (captionless / compact / normal).</summary>
    public static IReadOnlyList<double> Thresholds { get; } = [700, 920];

    public static double GetPlannedWidth(double measuredCollapsedWidth, double availableWidth)
    {
        var plannedWidth = availableWidth <= 920 ? 54 : 68;
        return Math.Min(Math.Max(0, measuredCollapsedWidth), plannedWidth);
    }

    public static string GetCacheKey(double availableWidth)
    {
        if (availableWidth <= 700)
            return "captionless";

        return availableWidth <= 920 ? "compact" : "normal";
    }
}
