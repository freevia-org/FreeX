using System.Windows;

namespace Freexcel.App.Host;

internal static class RibbonCollapsedGroupPresentationPlanner
{
    public static RibbonCollapsedGroupFootprint CreateFootprint(double availableWidth)
    {
        var compact = availableWidth <= 920;
        var captionless = availableWidth <= 760;
        return new RibbonCollapsedGroupFootprint(
            Mode: captionless
                ? RibbonCollapsedGroupFootprintMode.Captionless
                : compact
                    ? RibbonCollapsedGroupFootprintMode.Compact
                    : RibbonCollapsedGroupFootprintMode.Normal,
            Width: compact ? 44 : 64,
            Margin: compact ? new Thickness(0, 0, 2, 0) : new Thickness(1, 0, 3, 0),
            Padding: compact ? new Thickness(1, 2, 1, 2) : new Thickness(3, 2, 3, 2),
            CaptionVisibility: captionless ? Visibility.Collapsed : Visibility.Visible,
            CaptionFontSize: compact ? 9 : 10,
            CaptionMaxWidth: compact ? 40 : 60,
            IconFontSize: compact ? 18 : 22);
    }

    public static double GetPlannedWidth(double measuredCollapsedWidth, double availableWidth)
    {
        var plannedWidth = availableWidth <= 920 ? 46 : 68;
        return Math.Min(Math.Max(0, measuredCollapsedWidth), plannedWidth);
    }

    public static string GetCacheKey(double availableWidth) =>
        CreateFootprint(availableWidth).Mode switch
        {
            RibbonCollapsedGroupFootprintMode.Captionless => "captionless",
            RibbonCollapsedGroupFootprintMode.Compact => "compact",
            _ => "normal"
        };
}

internal readonly record struct RibbonCollapsedGroupFootprint(
    RibbonCollapsedGroupFootprintMode Mode,
    double Width,
    Thickness Margin,
    Thickness Padding,
    Visibility CaptionVisibility,
    double CaptionFontSize,
    double CaptionMaxWidth,
    double IconFontSize);

public enum RibbonCollapsedGroupFootprintMode
{
    Captionless,
    Compact,
    Normal
}
