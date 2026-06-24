namespace Free.Shared.Theme;

/// <summary>
/// Layout/spacing metrics for a theme.
/// <para>
/// Round 3 additions:
/// <list type="bullet">
///   <item><see cref="StatusBarHeight"/> — explicit height of the status bar (28 px on both WPF and Avalonia).</item>
///   <item><see cref="TitleBarCaptionHeight"/> — WPF <c>WindowChrome.CaptionHeight</c> (34 px).
///     Avalonia uses the native OS title bar so this metric is not applied by the Avalonia applier,
///     but the value is still carried in the token for parity documentation.</item>
/// </list>
/// </para>
/// </summary>
public sealed record ThemeMetrics(
    double RibbonRowHeight,
    double ControlHeight,
    double IconSize,
    double CornerRadius,
    double StatusBarHeight,
    double TitleBarCaptionHeight);
