using System.Windows;
using SharedCollapsedGroupFootprint = Free.Shared.Ribbon.RibbonCollapsedGroupFootprint;
using SharedCollapsedGroupInsets = Free.Shared.Ribbon.RibbonCollapsedGroupInsets;

namespace FreeX.App.Host;

internal static class RibbonCollapsedGroupPresentationPlanner
{
    public static IReadOnlyList<double> BreakpointThresholds =>
        Free.Shared.Ribbon.RibbonCollapsedGroupBreakpoints.Thresholds;

    private static readonly RibbonCollapsedGroupFootprint NormalFootprint = CreateCachedFootprint(
        Free.Shared.Ribbon.RibbonCollapsedGroupBreakpoints.GetFootprint(RibbonCollapsedGroupFootprintMode.Normal));

    private static readonly RibbonCollapsedGroupFootprint CompactFootprint = CreateCachedFootprint(
        Free.Shared.Ribbon.RibbonCollapsedGroupBreakpoints.GetFootprint(RibbonCollapsedGroupFootprintMode.Compact));

    private static readonly RibbonCollapsedGroupFootprint CaptionlessFootprint = CreateCachedFootprint(
        Free.Shared.Ribbon.RibbonCollapsedGroupBreakpoints.GetFootprint(RibbonCollapsedGroupFootprintMode.Captionless));

    public static RibbonCollapsedGroupFootprint CreateFootprint(double availableWidth)
    {
        var sharedFootprint = Free.Shared.Ribbon.RibbonCollapsedGroupBreakpoints.CreateFootprint(availableWidth);
        return sharedFootprint.Mode switch
        {
            RibbonCollapsedGroupFootprintMode.Captionless => CaptionlessFootprint,
            RibbonCollapsedGroupFootprintMode.Compact => CompactFootprint,
            RibbonCollapsedGroupFootprintMode.Normal => NormalFootprint,
            _ => throw new ArgumentOutOfRangeException(nameof(availableWidth), sharedFootprint.Mode, null)
        };
    }

    public static double GetPlannedWidth(double measuredCollapsedWidth, double availableWidth) =>
        Free.Shared.Ribbon.RibbonCollapsedGroupBreakpoints.GetPlannedWidth(measuredCollapsedWidth, availableWidth);

    public static string GetCacheKey(double availableWidth) =>
        Free.Shared.Ribbon.RibbonCollapsedGroupBreakpoints.GetCacheKey(availableWidth);

    private static RibbonCollapsedGroupFootprint CreateCachedFootprint(
        SharedCollapsedGroupFootprint sharedFootprint)
    {
        var margin = ToThickness(sharedFootprint.Margin);
        var padding = ToThickness(sharedFootprint.Padding);
        var captionVisibility = ToVisibility(sharedFootprint.CaptionVisibility);

        return new RibbonCollapsedGroupFootprint(
            sharedFootprint.Mode,
            sharedFootprint.Width,
            margin,
            padding,
            captionVisibility,
            sharedFootprint.CaptionFontSize,
            sharedFootprint.CaptionMaxWidth,
            sharedFootprint.IconFontSize,
            sharedFootprint.Width,
            margin,
            padding,
            captionVisibility,
            sharedFootprint.CaptionFontSize,
            sharedFootprint.CaptionMaxWidth,
            sharedFootprint.IconFontSize);
    }

    private static Thickness ToThickness(SharedCollapsedGroupInsets insets) =>
        new(insets.Left, insets.Top, insets.Right, insets.Bottom);

    private static Visibility ToVisibility(RibbonCollapsedGroupCaptionVisibility visibility) =>
        visibility == RibbonCollapsedGroupCaptionVisibility.Visible
            ? Visibility.Visible
            : Visibility.Collapsed;
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
