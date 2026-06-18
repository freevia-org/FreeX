namespace Free.Shared.Shell;

/// <summary>
/// Neutral, WPF-free description of what a backstage progress overlay should show:
/// the composed status line, whether the bar is indeterminate, and (when determinate)
/// the value already clamped into the bar's range. A thin platform binder turns this into
/// control state.
/// </summary>
public readonly record struct BackstageProgressOverlayState(
    string StatusText,
    bool IsIndeterminate,
    double Value);

/// <summary>
/// Neutral decision logic for the backstage "operation in progress" overlay, extracted from
/// the former WPF-coupled binder so the same rules can drive an Avalonia/FreeW overlay. Holds
/// no platform types; the WPF binder applies <see cref="BackstageProgressOverlayState"/> to a
/// panel/text/progress-bar.
/// </summary>
public static class BackstageProgressOverlayPlanner
{
    /// <summary>
    /// Composes the overlay status line. A leading title is prefixed as "title: detail";
    /// an empty title yields just the detail.
    /// </summary>
    public static string FormatStatusText(string title, string detail) =>
        string.IsNullOrEmpty(title) ? detail : $"{title}: {detail}";

    /// <summary>
    /// Plans the overlay state. A null <paramref name="percent"/> means indeterminate; a value
    /// is clamped into [<paramref name="minimum"/>, <paramref name="maximum"/>] so it is safe to
    /// assign to the bar directly.
    /// </summary>
    public static BackstageProgressOverlayState Plan(
        string title,
        string detail,
        double? percent,
        double minimum,
        double maximum) =>
        new(
            FormatStatusText(title, detail),
            !percent.HasValue,
            percent.HasValue ? Math.Clamp(percent.Value, minimum, maximum) : minimum);
}
