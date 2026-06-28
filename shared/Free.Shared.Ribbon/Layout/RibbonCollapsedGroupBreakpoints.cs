namespace Free.Shared.Ribbon;

/// <summary>
/// Pure width-breakpoint math for collapsed ribbon groups, shared by the layout engine and
/// tab profiles. Renderer-neutral footprint policy lives here; renderers map it to their native
/// spacing, visibility, and dependency-property value types.
/// </summary>
public static class RibbonCollapsedGroupBreakpoints
{
    private static readonly RibbonCollapsedGroupFootprint CaptionlessFootprint = new(
        RibbonCollapsedGroupFootprintMode.Captionless,
        Width: 52,
        PlannedWidth: 54,
        Margin: new RibbonCollapsedGroupInsets(0, 0, 2, 0),
        Padding: new RibbonCollapsedGroupInsets(1, 2, 1, 2),
        CaptionVisibility: RibbonCollapsedGroupCaptionVisibility.Collapsed,
        CaptionFontSize: 12,
        CaptionMaxWidth: 48,
        IconFontSize: 18,
        CacheKey: "captionless");

    private static readonly RibbonCollapsedGroupFootprint CompactFootprint = new(
        RibbonCollapsedGroupFootprintMode.Compact,
        Width: 52,
        PlannedWidth: 54,
        Margin: new RibbonCollapsedGroupInsets(0, 0, 2, 0),
        Padding: new RibbonCollapsedGroupInsets(1, 2, 1, 2),
        CaptionVisibility: RibbonCollapsedGroupCaptionVisibility.Visible,
        CaptionFontSize: 12,
        CaptionMaxWidth: 48,
        IconFontSize: 18,
        CacheKey: "compact");

    private static readonly RibbonCollapsedGroupFootprint NormalFootprint = new(
        RibbonCollapsedGroupFootprintMode.Normal,
        Width: 64,
        PlannedWidth: 68,
        Margin: new RibbonCollapsedGroupInsets(1, 0, 3, 0),
        Padding: new RibbonCollapsedGroupInsets(3, 2, 3, 2),
        CaptionVisibility: RibbonCollapsedGroupCaptionVisibility.Visible,
        CaptionFontSize: 12,
        CaptionMaxWidth: 60,
        IconFontSize: 22,
        CacheKey: "normal");

    /// <summary>Widths at which the collapsed-group footprint changes (captionless / compact / normal).</summary>
    public static IReadOnlyList<double> Thresholds { get; } = [700, 920];

    public static RibbonCollapsedGroupFootprint CreateFootprint(double availableWidth) =>
        GetFootprint(GetFootprintMode(availableWidth));

    public static RibbonCollapsedGroupFootprint GetFootprint(RibbonCollapsedGroupFootprintMode mode) =>
        mode switch
        {
            RibbonCollapsedGroupFootprintMode.Captionless => CaptionlessFootprint,
            RibbonCollapsedGroupFootprintMode.Compact => CompactFootprint,
            RibbonCollapsedGroupFootprintMode.Normal => NormalFootprint,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
        };

    public static RibbonCollapsedGroupFootprintMode GetFootprintMode(double availableWidth)
    {
        if (availableWidth <= 700)
            return RibbonCollapsedGroupFootprintMode.Captionless;

        return availableWidth <= 920
            ? RibbonCollapsedGroupFootprintMode.Compact
            : RibbonCollapsedGroupFootprintMode.Normal;
    }

    public static double GetPlannedWidth(double measuredCollapsedWidth, double availableWidth)
    {
        var footprint = CreateFootprint(availableWidth);
        return Math.Min(Math.Max(0, measuredCollapsedWidth), footprint.PlannedWidth);
    }

    public static string GetCacheKey(double availableWidth) =>
        CreateFootprint(availableWidth).CacheKey;
}

public readonly record struct RibbonCollapsedGroupFootprint(
    RibbonCollapsedGroupFootprintMode Mode,
    double Width,
    double PlannedWidth,
    RibbonCollapsedGroupInsets Margin,
    RibbonCollapsedGroupInsets Padding,
    RibbonCollapsedGroupCaptionVisibility CaptionVisibility,
    double CaptionFontSize,
    double CaptionMaxWidth,
    double IconFontSize,
    string CacheKey);

public readonly record struct RibbonCollapsedGroupInsets(
    double Left,
    double Top,
    double Right,
    double Bottom);

public enum RibbonCollapsedGroupCaptionVisibility
{
    Collapsed,
    Visible
}

public enum RibbonCollapsedGroupFootprintMode
{
    Captionless,
    Compact,
    Normal
}
