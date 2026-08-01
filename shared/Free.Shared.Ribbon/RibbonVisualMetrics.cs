namespace Free.Shared.Ribbon;

/// <summary>
/// Platform-neutral dimensions used by both ribbon renderers.
/// </summary>
public static class RibbonVisualMetrics
{
    public const double SmallRowHeight = 22;
    public const double LargeIconSize = 32;
    public const double MediumIconSize = 16;
    public const double SmallIconSize = 18;
    public const double TabContentMinHeight = 88;
    public const double TabContentTopPadding = 4;
    public const double GroupLabelHeight = 18;

    /// <summary>Shared chrome values for collapsed-group command popups.</summary>
    public static RibbonPopupChromeMetrics PopupChrome { get; } = new(
        MinWidth: 220,
        MaxWidth: 360,
        ItemMinHeight: 28,
        PopupPadding: new RibbonPopupInsets(4, 4, 4, 4),
        ItemPadding: new RibbonPopupInsets(10, 5, 10, 5),
        BorderThickness: 1,
        CornerRadius: 2,
        ShadowDepth: 2,
        ShadowBlurRadius: 8,
        ShadowOpacity: 0.22,
        AnchorGap: 1);
}

public sealed record RibbonPopupChromeMetrics(
    double MinWidth,
    double MaxWidth,
    double ItemMinHeight,
    RibbonPopupInsets PopupPadding,
    RibbonPopupInsets ItemPadding,
    double BorderThickness,
    double CornerRadius,
    double ShadowDepth,
    double ShadowBlurRadius,
    double ShadowOpacity,
    double AnchorGap)
{
    /// <summary>Chrome shared by native submenu presenters; toolkit arrows remain native.</summary>
    public RibbonPopupSubmenuChromeMetrics Submenu { get; init; } = new(
        ItemMinHeight,
        ItemPadding,
        AnchorGap: 2,
        BorderThickness);
}

public sealed record RibbonPopupSubmenuChromeMetrics(
    double ItemMinHeight,
    RibbonPopupInsets ItemPadding,
    double AnchorGap,
    double BorderThickness);

public readonly record struct RibbonPopupInsets(double Left, double Top, double Right, double Bottom);
