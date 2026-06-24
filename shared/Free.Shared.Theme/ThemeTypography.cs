namespace Free.Shared.Theme;

/// <summary>Font weight semantic for theme tokens.</summary>
public enum ThemeFontWeight { Normal, SemiBold, Bold }

/// <summary>A single typography token: family, size, weight.</summary>
public sealed record ThemeTypeToken(string FontFamily, double SizePt, ThemeFontWeight Weight);

/// <summary>
/// The typography roles in a theme.
/// <para>
/// Round 3 additions: <see cref="StatusBarText"/> — status-bar and selection-stats labels
/// (FontSize=12, no explicit FontFamily so it inherits the system default; both WPF and Avalonia
/// renderers apply the same value).
/// </para>
/// </summary>
public sealed record ThemeTypography(
    ThemeTypeToken Body,
    ThemeTypeToken Caption,
    ThemeTypeToken RibbonLabel,
    ThemeTypeToken Heading,
    ThemeTypeToken StatusBarText);
