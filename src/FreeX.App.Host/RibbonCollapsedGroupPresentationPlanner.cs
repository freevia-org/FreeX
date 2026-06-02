using System.Windows;

namespace FreeX.App.Host;

internal static class RibbonCollapsedGroupPresentationPlanner
{
    public static IReadOnlyList<double> BreakpointThresholds { get; } = [700, 920];

    private static readonly RibbonCollapsedGroupFootprint NormalFootprint = CreateCachedFootprint(
        RibbonCollapsedGroupFootprintMode.Normal,
        width: 64,
        margin: new Thickness(1, 0, 3, 0),
        padding: new Thickness(3, 2, 3, 2),
        captionVisibility: Visibility.Visible,
        captionMaxWidth: 60,
        iconFontSize: 22);

    private static readonly RibbonCollapsedGroupFootprint CompactFootprint = CreateCachedFootprint(
        RibbonCollapsedGroupFootprintMode.Compact,
        width: 52,
        margin: new Thickness(0, 0, 2, 0),
        padding: new Thickness(1, 2, 1, 2),
        captionVisibility: Visibility.Visible,
        captionMaxWidth: 48,
        iconFontSize: 18);

    private static readonly RibbonCollapsedGroupFootprint CaptionlessFootprint = CreateCachedFootprint(
        RibbonCollapsedGroupFootprintMode.Captionless,
        width: 52,
        margin: new Thickness(0, 0, 2, 0),
        padding: new Thickness(1, 2, 1, 2),
        captionVisibility: Visibility.Collapsed,
        captionMaxWidth: 48,
        iconFontSize: 18);

    public static RibbonCollapsedGroupFootprint CreateFootprint(double availableWidth)
    {
        if (availableWidth <= 700)
            return CaptionlessFootprint;

        return availableWidth <= 920
            ? CompactFootprint
            : NormalFootprint;
    }

    public static double GetPlannedWidth(double measuredCollapsedWidth, double availableWidth)
    {
        var plannedWidth = availableWidth <= 920 ? 54 : 68;
        return Math.Min(Math.Max(0, measuredCollapsedWidth), plannedWidth);
    }

    public static string GetCacheKey(double availableWidth) =>
        CreateFootprint(availableWidth).Mode switch
        {
            RibbonCollapsedGroupFootprintMode.Captionless => "captionless",
            RibbonCollapsedGroupFootprintMode.Compact => "compact",
            _ => "normal"
        };

    private static RibbonCollapsedGroupFootprint CreateCachedFootprint(
        RibbonCollapsedGroupFootprintMode mode,
        double width,
        Thickness margin,
        Thickness padding,
        Visibility captionVisibility,
        double captionMaxWidth,
        double iconFontSize) =>
        new(
            mode,
            width,
            margin,
            padding,
            captionVisibility,
            12,
            captionMaxWidth,
            iconFontSize,
            width,
            margin,
            padding,
            captionVisibility,
            12d,
            captionMaxWidth,
            iconFontSize);
}

internal readonly record struct RibbonCollapsedGroupFootprint(
    RibbonCollapsedGroupFootprintMode Mode,
    double Width,
    Thickness Margin,
    Thickness Padding,
    Visibility CaptionVisibility,
    double CaptionFontSize,
    double CaptionMaxWidth,
    double IconFontSize,
    object BoxedWidth,
    object BoxedMargin,
    object BoxedPadding,
    object BoxedCaptionVisibility,
    object BoxedCaptionFontSize,
    object BoxedCaptionMaxWidth,
    object BoxedIconFontSize);

public enum RibbonCollapsedGroupFootprintMode
{
    Captionless,
    Compact,
    Normal
}
