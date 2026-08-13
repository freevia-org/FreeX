namespace Free.Shared.Theme;

/// <summary>
/// The complete design-token contract for a FreeFamily app theme.
/// A Theme object is neutral data (no WPF/Avalonia types); platform appliers convert it.
/// </summary>
public sealed record Theme(
    string Name,
    ThemeColors Colors,
    ThemeTypography Typography,
    ThemeMetrics Metrics,
    string IconSetId);
