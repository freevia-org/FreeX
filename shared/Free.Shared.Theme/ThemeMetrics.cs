namespace Free.Shared.Theme;

/// <summary>Layout/spacing metrics for a theme (not wired to rendering in round 1).</summary>
public sealed record ThemeMetrics(
    double RibbonRowHeight,
    double ControlHeight,
    double IconSize,
    double CornerRadius);
