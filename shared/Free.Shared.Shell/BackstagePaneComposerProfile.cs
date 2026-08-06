namespace Free.Shared.Shell;

/// <summary>
/// Neutral layout profile for generic Backstage panes. Product presentation assemblies supply the
/// profile; WPF and Avalonia composers project the same metrics into native controls.
/// </summary>
public sealed record BackstagePaneComposerProfile
{
    public static BackstagePaneComposerProfile Default { get; } = new();

    public BackstagePaneMetrics Metrics { get; init; } = BackstageVisualContract.Pane;
    public double InfoPaneMaxWidth { get; init; } = 640;
    public double RecentPaneMaxWidth { get; init; } = 640;
    public double OptionsPaneMaxWidth { get; init; } = 560;
    public double AccountPaneMaxWidth { get; init; } = 640;
    public double ActionPaneMaxWidth { get; init; } = 720;
    public double PaneSpacing { get; init; } = 10;
    public BackstageVisualThickness DescriptionMargin { get; init; } = new(0, 0, 0, 16);
    public BackstageVisualThickness InfoEditActionMargin { get; init; } = new(0, 8, 0, 0);
    public BackstageVisualThickness OptionsEditActionMargin { get; init; } = new(0, 14, 0, 0);
    public double AccountOptionsFontSize { get; init; } = 13;
    public BackstageVisualThickness AccountOptionsMargin { get; init; } = new(0, 18, 0, 0);
    public bool UseLinkActionRows { get; init; }
    public bool UseTextBlockActionContent { get; init; }
    public bool WrapPanesInScrollViewer { get; init; }
    public string PaneFontFamilyName { get; init; } = "Segoe UI";
    public double PaneFontSize { get; init; } = 12;
    public bool DisableScrollBarAutoHide { get; init; } = true;
    public bool UseAntialiasTextRendering { get; init; } = true;
    public double ClassicScrollBarWidth { get; init; } = 17;
}
