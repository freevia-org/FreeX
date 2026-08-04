namespace Free.Shared.Shell;

/// <summary>
/// Host-neutral values shared by the WPF and Avalonia Backstage realizers.
/// Keep app accents, pane-specific widths, and control-template details outside this contract.
/// </summary>
public static class BackstageVisualContract
{
    public static BackstageVisualTheme Theme { get; } = new(
        PrimaryText: new BackstageVisualColor(0x33, 0x33, 0x33),
        SecondaryText: new BackstageVisualColor(0x70, 0x70, 0x70));

    public static BackstagePaneMetrics Pane { get; } = new(
        HeadingFontSize: 26,
        HeadingMargin: new BackstageVisualThickness(0, 0, 0, 18),
        DescriptionFontSize: 12,
        SectionHeaderFontSize: 15,
        SectionHeaderMargin: new BackstageVisualThickness(0, 16, 0, 6),
        DetailGridMargin: new BackstageVisualThickness(0, 2, 0, 2),
        DetailLabelColumnWidth: 120,
        DetailFontSize: 12,
        ActionFontSize: 14,
        ActionDescriptionFontSize: 11,
        ActionRowMargin: new BackstageVisualThickness(0, 0, 0, 10),
        ActionDescriptionMargin: new BackstageVisualThickness(0, 2, 0, 0));

    public static BackstageFrameMetrics Frame { get; } = new(
        RailWidth: 190,
        ContentPadding: new BackstageVisualThickness(40, 28),
        BottomNavigationMargin: new BackstageVisualThickness(0, 0, 0, 10),
        SeparatorMargin: new BackstageVisualThickness(0, 6));
}

public sealed record BackstageVisualTheme(
    BackstageVisualColor PrimaryText,
    BackstageVisualColor SecondaryText);

public sealed record BackstagePaneMetrics(
    double HeadingFontSize,
    BackstageVisualThickness HeadingMargin,
    double DescriptionFontSize,
    double SectionHeaderFontSize,
    BackstageVisualThickness SectionHeaderMargin,
    BackstageVisualThickness DetailGridMargin,
    double DetailLabelColumnWidth,
    double DetailFontSize,
    double ActionFontSize,
    double ActionDescriptionFontSize,
    BackstageVisualThickness ActionRowMargin,
    BackstageVisualThickness ActionDescriptionMargin);

public sealed record BackstageFrameMetrics(
    double RailWidth,
    BackstageVisualThickness ContentPadding,
    BackstageVisualThickness BottomNavigationMargin,
    BackstageVisualThickness SeparatorMargin);

public readonly record struct BackstageVisualThickness(double Left, double Top, double Right, double Bottom)
{
    public BackstageVisualThickness(double horizontal, double vertical)
        : this(horizontal, vertical, horizontal, vertical)
    {
    }
}

public readonly record struct BackstageVisualColor(byte Red, byte Green, byte Blue);
